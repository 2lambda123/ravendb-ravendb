﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Exceptions.Database;
using Raven.Client.Extensions;
using Raven.Client.ServerWide;
using Raven.Client.Util;
using Raven.Server.Config;
using Raven.Server.Documents.Handlers;
using Raven.Server.NotificationCenter.Notifications;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.NotificationCenter.Notifications.Server;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Commands;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Raven.Server.Web.System;
using Sparrow;
using Sparrow.Logging;
using Sparrow.Utils;

namespace Raven.Server.Documents
{
    public class DatabasesLandlord : IDisposable
    {
        public const string DoNotRemove = "DoNotRemove";
        private readonly ReaderWriterLockSlim _disposing = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        public readonly ConcurrentDictionary<StringSegment, DateTime> LastRecentlyUsed =
            new ConcurrentDictionary<StringSegment, DateTime>(CaseInsensitiveStringSegmentEqualityComparer.Instance);

        public readonly ResourceCache<DocumentDatabase> DatabasesCache = new ResourceCache<DocumentDatabase>();
        private readonly TimeSpan _concurrentDatabaseLoadTimeout;
        private readonly Logger _logger;
        private readonly SemaphoreSlim _databaseSemaphore;
        private readonly ServerStore _serverStore;

        public DatabasesLandlord(ServerStore serverStore)
        {
            _serverStore = serverStore;
            _databaseSemaphore = new SemaphoreSlim(_serverStore.Configuration.Databases.MaxConcurrentLoads);
            _concurrentDatabaseLoadTimeout = _serverStore.Configuration.Databases.ConcurrentLoadTimeout.AsTimeSpan;
            _logger = LoggingSource.Instance.GetLogger<DatabasesLandlord>("Server");
            CatastrophicFailureHandler = new CatastrophicFailureHandler(this, _serverStore);
        }

        public CatastrophicFailureHandler CatastrophicFailureHandler { get; }

        public void ClusterOnDatabaseChanged(object sender, (string DatabaseName, long Index, string Type, ClusterDatabaseChangeType ChangeType) t)
        {
            HandleClusterDatabaseChanged(t.DatabaseName, t.Index, t.Type, t.ChangeType);
        }

        private void HandleClusterDatabaseChanged(string databaseName, long index, string type, ClusterDatabaseChangeType changeType)
        {
            _disposing.EnterReadLock();
            try
            {
                if (_serverStore.Disposed)
                    return;

                // response to changed database.
                // if disabled, unload

                var record = _serverStore.LoadDatabaseRecord(databaseName, out long _);
                if (record == null)
                {
                    // was removed, need to make sure that it isn't loaded 
                    DatabasesCache.RemoveLockAndReturn(databaseName, CompleteDatabaseUnloading, out var _).Dispose();
                    return;
                }

                if (ShouldDeleteDatabase(databaseName, record)) 
                    return;

                if (record.Topology.RelevantFor(_serverStore.NodeTag) == false)
                    return;

                if (record.Disabled)
                {
                    DatabasesCache.RemoveLockAndReturn(databaseName, CompleteDatabaseUnloading, out var _).Dispose();
                    return;
                }

                if (DatabasesCache.TryGetValue(databaseName, out var task) == false)
                {
                    // if the database isn't loaded, but it is relevant for this node, we need to create
                    // it. This is important so things like replication will start pumping, and that 
                    // configuration changes such as running periodic backup will get a chance to run, which
                    // they wouldn't unless the database is loaded / will have a request on it.          
                    task = TryGetOrCreateResourceStore(databaseName);
                }

                if (task.IsCanceled || task.IsFaulted)
                    return;

                switch (changeType)
                {
                    case ClusterDatabaseChangeType.RecordChanged:
                        if (task.IsCompleted)
                        {
                            NotifyDatabaseAboutStateChange(databaseName, task, index);
                            if (type == ClusterStateMachine.SnapshotInstalled)
                            {
                                NotifyPendingClusterTransaction(databaseName, task, index);
                            }
                            return;
                        }
                        task.ContinueWith(done =>
                        {
                            NotifyDatabaseAboutStateChange(databaseName, done, index);
                            if (type == ClusterStateMachine.SnapshotInstalled)
                            {
                                NotifyPendingClusterTransaction(databaseName, done, index);
                            }
                        });
                        break;
                    case ClusterDatabaseChangeType.ValueChanged:
                        if (task.IsCompleted)
                        {
                            NotifyDatabaseAboutValueChange(databaseName, task, index);
                            return;
                        }

                        task.ContinueWith(done =>
                        {
                            NotifyDatabaseAboutValueChange(databaseName, done, index);
                        });
                        break;
                    case ClusterDatabaseChangeType.PendingClusterTransactions:
                        if (task.IsCompleted)
                        {
                            NotifyPendingClusterTransaction(databaseName, task, index);
                            return;
                        }

                        task.ContinueWith(done =>
                        {
                            NotifyPendingClusterTransaction(databaseName, done, index);
                        });
                        break;
                    default:
                        ThrowUnknownClusterDatabaseChangeType(changeType);
                        break;
                }

                // if deleted, unload / deleted and then notify leader that we removed it
            }
            catch (Exception e)
            {
                var title = $"Failed to digest change of type '{changeType}' for database '{databaseName}'";
                if (_logger.IsInfoEnabled)
                    _logger.Info(title, e);
                _serverStore.NotificationCenter.Add(AlertRaised.Create(databaseName, title, e.Message, AlertType.DeletionError, NotificationSeverity.Error,
                    details: new ExceptionDetails(e)));
            }
            finally
            {
                _disposing.ExitReadLock();
            }
        }

        private void NotifyPendingClusterTransaction(string databaseName, Task<DocumentDatabase> task, long index)
        {
            DocumentDatabase database;
            try
            {
                database = task.Result;
            }
            catch (Exception e)
            {
                if (_logger.IsInfoEnabled)
                {
                    _logger.Info($"Failed to load database '{databaseName}'", e);
                }
                return;
            }

            try
            {
                ExecutePendingClusterTransactions(database, index);
            }
            catch (Exception e)
            {
                // nothing we can do
                if (_logger.IsInfoEnabled)
                {
                    _logger.Info($"Failed enqueue cluster transaction command for database: '{databaseName}'", e);
                }
            }
        }

        public void ExecutePendingClusterTransactions(DocumentDatabase database, long index, Action<List<Task>> customWait = null)
        {
            var transactionsTasks = new List<Task>();
            string global;

            using (database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext docContext))
            using (docContext.OpenReadTransaction())
            {
                global = DocumentsStorage.GetDatabaseChangeVector(docContext);
            }

            var dbGrpId = database.DatabaseGroupId;
            var lastDocFromClusterTransaction = ChangeVectorUtils.GetEtagById(global, dbGrpId);

            using (database.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext serverContext))
            using (serverContext.OpenReadTransaction())
            {
                // TODO: There is a race between replication the transaction result from other node and executing the transaction on this one.
                // If the above occur, the user will wait for ever for a result. 
                // we need to correlate between the TaskId and the already executed cluster transactions (maybe use the Raft Index?).
                // As a work around we pass 'fromCount: 0'
                var commands = ClusterTransactionCommand.ReadCommandsBatch(serverContext, database.Name, fromCount: 0).ToList();

                foreach (var command in commands)
                {
                    var mergedCommands = new BatchHandler.ClusterTransactionMergedCommand(database, command);
                    var task = database.TxMerger.Enqueue(mergedCommands);
                    transactionsTasks.Add(task);
                    NotifyDatabaseAfterCompletion(database, index, task, mergedCommands);
                }

                if (transactionsTasks.Count == 0)
                {
                    database.RachisLogIndexNotifications.NotifyListenersAbout(index, null);
                    return;
                }

                if (customWait != null)
                {
                    customWait(transactionsTasks);
                    return;
                }

                Task.WaitAll(transactionsTasks.ToArray());
            }
        }

        private void NotifyDatabaseAfterCompletion(DocumentDatabase database, long index, Task task, BatchHandler.ClusterTransactionMergedCommand mergedCommands)
        {
            task.ContinueWith(t =>
            {
                try
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        var options = mergedCommands.Options;
                        Task indexTask = null;
                        if (options?.WaitForIndexesTimeout != null)
                        {
                            indexTask = BatchHandler.WaitForIndexesAsync(database.DocumentsStorage.ContextPool, database, options.WaitForIndexesTimeout.Value,
                                options.SpecifiedIndexesQueryString, options.WaitForIndexThrow,
                                mergedCommands.LastChangeVector, mergedCommands.LastTombstoneEtag, mergedCommands.ModifiedCollections);
                        }

                        var result = new BatchHandler.ClusterTransactionCompletionResult
                        {
                            Array = mergedCommands.Reply,
                            IndexTask = indexTask,
                        };
                        database.ClusterTransactionWaiter.SetResult(mergedCommands.Options.TaskId, index, result);
                        return;
                    }

                    database.ClusterTransactionWaiter.SetException(mergedCommands.Options.TaskId, index, t.Exception);
                }
                catch (Exception e)
                {
                    // nothing we can do
                    if (_logger.IsInfoEnabled)
                    {
                        _logger.Info($"Failed to notify about transaction completion for database '{database.Name}'.", e);
                    }
                }
            });
        }

        public bool ShouldDeleteDatabase(string dbName, DatabaseRecord record)
        {
            var deletionInProgress = DeletionInProgressStatus.No;
            var directDelete = record.DeletionInProgress != null &&
                               record.DeletionInProgress.TryGetValue(_serverStore.NodeTag, out deletionInProgress) &&
                               deletionInProgress != DeletionInProgressStatus.No;

            if (directDelete && 
                record.Topology.Count == record.Topology.ReplicationFactor) 
                // If the deletion was issued form the cluster observer to maintain the replication factor we need to make sure
                // the all the documents were replicated from this node, therefor the deletion will be called from the replication code.
            {
                DeleteDatabase(dbName, deletionInProgress, record);
                return true;
            }
            return false;
        }

        public void DeleteDatabase(string dbName, DeletionInProgressStatus deletionInProgress, DatabaseRecord record)
        {
            IDisposable removeLockAndReturn = null;
            try
            {
                try
                {
                    removeLockAndReturn = DatabasesCache.RemoveLockAndReturn(dbName, CompleteDatabaseUnloading, out _);
                }
                catch (AggregateException ae ) when (nameof(DeleteDatabase).Equals(ae.InnerException.Data["Source"]))
                {
                    // this is already in the process of being deleted, we can just exit and let another thread handle it
                    return;
                }
                if (deletionInProgress == DeletionInProgressStatus.HardDelete)
                {
                    RavenConfiguration configuration;
                    try
                    {
                        configuration = CreateDatabaseConfiguration(dbName, ignoreDisabledDatabase: true, ignoreBeenDeleted: true, ignoreNotRelevant: true,
                            databaseRecord: record);
                    }
                    catch (Exception ex)
                    {
                        configuration = null;
                        if (_logger.IsInfoEnabled)
                            _logger.Info("Could not create database configuration", ex);
                    }

                    // this can happen if the database record was already deleted
                    if (configuration != null)
                    {
                        DatabaseHelper.DeleteDatabaseFiles(configuration);
                    }

                    // At this point the db record still exists but the db was effectively deleted 
                    // from this node so we can also remove its secret key from this node.
                    if (record.Encrypted)
                    {
                        using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                        using (var tx = context.OpenWriteTransaction())
                        {
                            _serverStore.DeleteSecretKey(context, dbName);
                            tx.Commit();
                        }
                    }
                }

                // delete the cache info
                DeleteDatabaseCachedInfo(dbName, _serverStore);
            }
            finally
            {
                removeLockAndReturn?.Dispose();
            }
            NotifyLeaderAboutRemoval(dbName);
        }

        private static void ThrowUnknownClusterDatabaseChangeType(ClusterDatabaseChangeType type)
        {
            throw new InvalidOperationException($"Unknown cluster database change type: {type}");
        }

        private void NotifyLeaderAboutRemoval(string dbName)
        {
            var cmd = new RemoveNodeFromDatabaseCommand(dbName)
            {
                NodeTag = _serverStore.NodeTag
            };
            _serverStore.SendToLeaderAsync(cmd)
                .ContinueWith(t =>
                {
                    if (t.Exception != null)
                    {
                        if (_logger.IsInfoEnabled)
                        {
                            _logger.Info($"Failed to notify leader about removal of node {_serverStore.NodeTag} from database {dbName}", t.Exception);
                        }
                    }
                });
        }

        private void NotifyDatabaseAboutStateChange(string changedDatabase, Task<DocumentDatabase> done, long index)
        {
            try
            {
                done.Result.StateChanged(index);
            }
            catch (Exception e)
            {
                if (_logger.IsInfoEnabled)
                {
                    _logger.Info($"Failed to update database {changedDatabase} about new state", e);
                }
                // nothing to do here
            }
        }

        private void NotifyDatabaseAboutValueChange(string changedDatabase, Task<DocumentDatabase> done, long index)
        {
            try
            {
                done.Result.ValueChanged(index);
            }
            catch (Exception e)
            {
                if (_logger.IsInfoEnabled)
                {
                    _logger.Info($"Failed to update database {changedDatabase} about new value", e);
                }
                // nothing to do here
            }
        }

        public TimeSpan DatabaseLoadTimeout => _serverStore.Configuration.Server.MaxTimeForTaskToWaitForDatabaseToLoad.AsTimeSpan;

        public void Dispose()
        {
            _disposing.EnterWriteLock();
            try
            {
                var exceptionAggregator = new ExceptionAggregator(_logger, "Failure to dispose landlord");

                // shut down all databases in parallel, avoid having to wait for each one
                Parallel.ForEach(DatabasesCache.Values, new ParallelOptions
                {
                    // we limit the number of resources we dispose concurrently to avoid
                    // putting too much pressure on the I/O system if a disposing db need
                    // to flush data to disk
                    MaxDegreeOfParallelism = Math.Max(1, ProcessorInfo.ProcessorCount / 2)
                }, dbTask =>
                {
                    if (dbTask.IsCompleted == false)
                        dbTask.ContinueWith(task =>
                        {
                            if (task.Status != TaskStatus.RanToCompletion)
                                return;

                            try
                            {
                                task.Result.Dispose();
                            }
                            catch (Exception e)
                            {
                                if (_logger.IsInfoEnabled)
                                    _logger.Info("Failure in deferred disposal of a database", e);
                            }
                        });
                    else if (dbTask.Status == TaskStatus.RanToCompletion && dbTask.Result != null)
                        exceptionAggregator.Execute(dbTask.Result.Dispose);
                    // there is no else, the db is probably faulted
                });
                DatabasesCache.Clear();

                try
                {
                    _databaseSemaphore.Dispose();
                }
                catch (Exception e)
                {
                    if (_logger.IsInfoEnabled)
                        _logger.Info("Failed to dispose resource semaphore", e);
                }

                exceptionAggregator.ThrowIfNeeded();
            }
            finally
            {
                _disposing.ExitWriteLock();
            }
        }

        public event Action<string> OnDatabaseLoaded = delegate { };

        public bool IsDatabaseLoaded(StringSegment databaseName)
        {
            if (DatabasesCache.TryGetValue(databaseName, out var task))
                return task != null && task.IsCompletedSuccessfully;

            return false;
        }

        public Task<DocumentDatabase> TryGetOrCreateResourceStore(StringSegment databaseName, bool ignoreDisabledDatabase = false)
        {
            try
            {
                if (_disposing.TryEnterReadLock(0) == false)
                    ThrowServerIsBeingDisposed(databaseName);

                if (DatabasesCache.TryGetValue(databaseName, out var database))
                {
                    if (database.IsFaulted)
                    {
                        // If a database was unloaded, this is what we get from DatabasesCache. 
                        // We want to keep the exception there until UnloadAndLockDatabase is disposed.
                        var extractSingleInnerException = database.Exception.ExtractSingleInnerException();
                        if (Equals(extractSingleInnerException.Data[DoNotRemove], true))
                            return database;
                    }

                    if (database.IsFaulted || database.IsCanceled)
                    {
                        DatabasesCache.TryRemove(databaseName, out database);
                        LastRecentlyUsed.TryRemove(databaseName, out var _);
                        // and now we will try creating it again
                    }
                    else
                    {
                        if (database.IsCompletedSuccessfully)
                            database.Result.LastAccessTime = database.Result.Time.GetUtcNow();

                        return database;
                    }
                }
                return CreateDatabase(databaseName, ignoreDisabledDatabase);
            }
            finally
            {
                if (_disposing.IsReadLockHeld)
                    _disposing.ExitReadLock();
            }
        }

        private static void ThrowServerIsBeingDisposed(StringSegment databaseName)
        {
            throw new ObjectDisposedException("The server is being disposed, cannot load database " + databaseName);
        }

        private Task<DocumentDatabase> CreateDatabase(StringSegment databaseName, bool ignoreDisabledDatabase = false)
        {
            var config = CreateDatabaseConfiguration(databaseName, ignoreDisabledDatabase);
            if (config == null)
                return Task.FromResult<DocumentDatabase>(null);

            if (!_databaseSemaphore.Wait(0))
                return UnlikelyCreateDatabaseUnderContention(databaseName, config);

            return CreateDatabaseUnderResourceSemaphore(databaseName, config);
        }

        private async Task<DocumentDatabase> UnlikelyCreateDatabaseUnderContention(StringSegment databaseName, RavenConfiguration config)
        {
            if (await _databaseSemaphore.WaitAsync(_concurrentDatabaseLoadTimeout) == false)
                throw new DatabaseConcurrentLoadTimeoutException("Too many databases loading concurrently, timed out waiting for them to load.");

            return await CreateDatabaseUnderResourceSemaphore(databaseName, config);
        }

        private Task<DocumentDatabase> CreateDatabaseUnderResourceSemaphore(StringSegment databaseName, RavenConfiguration config)
        {
            try
            {
                var task = new Task<DocumentDatabase>(() => ActuallyCreateDatabase(databaseName, config), TaskCreationOptions.RunContinuationsAsynchronously);
                var database = DatabasesCache.GetOrAdd(databaseName, task);
                if (database == task)
                    task.Start(); // the semaphore will be released here at the end of the task
                else
                    _databaseSemaphore.Release();

                return database;
            }
            catch (Exception)
            {
                _databaseSemaphore.Release();
                throw;
            }
        }

        private DocumentDatabase ActuallyCreateDatabase(StringSegment databaseName, RavenConfiguration config)
        {
            try
            {
                if (_serverStore.Disposed)
                    ThrowServerIsBeingDisposed(databaseName);

                //if false, this means we have started disposing, so we shouldn't create a database now
                if (_disposing.TryEnterReadLock(0) == false)
                    ThrowServerIsBeingDisposed(databaseName);

                var db = CreateDocumentsStorage(databaseName, config);
                _serverStore.NotificationCenter.Add(
                    DatabaseChanged.Create(databaseName, DatabaseChangeType.Load));

                return db;
            }
            catch (Exception e)
            {
                // if we are here, there is an error, and if there is an error, we need to clear it from the 
                // resource store cache so we can try to reload it.
                // Note that we return the faulted task anyway, because we need the user to look at the error
                if (e.Data.Contains("Raven/KeepInResourceStore") == false)
                {
                    DatabasesCache.TryRemove(databaseName, out _);
                }

                if (_logger.IsInfoEnabled && e.InnerException is UnauthorizedAccessException)
                {
                    _logger.Info("Failed to load database because couldn't access certain file. Please check permissions, and make sure that nothing locks that file (an antivirus is a good example of something that can lock the file)", e);
                }

                throw;
            }
            finally
            {
                try
                {
                    _databaseSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // nothing to do
                }

                if (_disposing.IsReadLockHeld)
                    _disposing.ExitReadLock();

            }
        }

        public ConcurrentDictionary<string, ConcurrentQueue<string>> InitLog =
            new ConcurrentDictionary<string, ConcurrentQueue<string>>(StringComparer.OrdinalIgnoreCase);

        private DocumentDatabase CreateDocumentsStorage(StringSegment databaseName, RavenConfiguration config)
        {
            void AddToInitLog(string txt)
            {
                string msg = txt;
                msg = $"[Load Database] {DateTime.UtcNow} :: Database '{(string)databaseName}' : {msg}";
                if(InitLog.TryGetValue(databaseName, out var q))
                    q.Enqueue(msg);
                if (_logger.IsInfoEnabled)
                    _logger.Info(msg);
            }

            try
            {
                // force this to have a new value if one already exists
                InitLog.AddOrUpdate(databaseName,
                    s => new ConcurrentQueue<string>(),
                    (s, existing) => new ConcurrentQueue<string>());


                AddToInitLog("Starting database initialization");

                var sp = Stopwatch.StartNew();
                var documentDatabase = new DocumentDatabase(config.ResourceName, config, _serverStore, AddToInitLog);
                documentDatabase.Initialize();
                AddToInitLog("Finish database initialization");
                DeleteDatabaseCachedInfo(documentDatabase.Name, _serverStore);
                if (_logger.IsInfoEnabled)
                    _logger.Info($"Started database {config.ResourceName} in {sp.ElapsedMilliseconds:#,#;;0}ms");

                OnDatabaseLoaded(config.ResourceName);

                // if we have a very long init process, make sure that we reset the last idle time for this db.
                LastRecentlyUsed.AddOrUpdate(databaseName, SystemTime.UtcNow, (_, time) => SystemTime.UtcNow);


                return documentDatabase;
            }
            catch (Exception e)
            {
                if (_logger.IsInfoEnabled)
                    _logger.Info($"Failed to start database {config.ResourceName}", e);
                throw new DatabaseLoadFailureException($"Failed to start database {config.ResourceName}" + Environment.NewLine +
                                                       $"At {config.Core.DataDirectory}", e);
            }
            finally
            {
                InitLog.TryRemove(databaseName, out var _);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DeleteDatabaseCachedInfo(string databaseName, ServerStore serverStore)
        {
            serverStore.DatabaseInfoCache.Delete(databaseName);
        }

        public RavenConfiguration CreateDatabaseConfiguration(StringSegment databaseName, bool ignoreDisabledDatabase = false, bool ignoreBeenDeleted = false, bool ignoreNotRelevant = false)
        {
            if (databaseName.IsNullOrWhiteSpace())
                throw new ArgumentNullException(nameof(databaseName), "Database name cannot be empty");
            if (databaseName.Equals("<system>")) // This is here to guard against old ravendb tests
                throw new ArgumentNullException(nameof(databaseName),
                    "Database name cannot be <system>. Using of <system> database indicates outdated code that was targeted RavenDB 3.5.");

            Debug.Assert(_serverStore.Disposed == false);

            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                context.OpenReadTransaction();

                var doc = _serverStore.Cluster.Read(context, "db/" + databaseName.Value.ToLowerInvariant());
                if (doc == null)
                    return null;

                var databaseRecord = JsonDeserializationCluster.DatabaseRecord(doc);

                if (databaseRecord.Encrypted)
                {
                    if (_serverStore.Server.WebUrl?.StartsWith("https:", StringComparison.OrdinalIgnoreCase) == false)
                    {
                        throw new DatabaseDisabledException(
                            $"The database {databaseName.Value} is encrypted, and must be accessed only via HTTPS, but the web url used is {_serverStore.Server.WebUrl}");
                    }
                }

                return CreateDatabaseConfiguration(databaseName, ignoreDisabledDatabase, ignoreBeenDeleted, ignoreNotRelevant, databaseRecord);
            }
        }

        public RavenConfiguration CreateDatabaseConfiguration(StringSegment databaseName, bool ignoreDisabledDatabase, bool ignoreBeenDeleted, bool ignoreNotRelevant, DatabaseRecord databaseRecord)
        {
            if (databaseRecord.Disabled && ignoreDisabledDatabase == false)
                throw new DatabaseDisabledException(databaseName + " has been disabled");

            var databaseIsBeenDeleted = databaseRecord.DeletionInProgress != null &&
                            databaseRecord.DeletionInProgress.TryGetValue(_serverStore.NodeTag, out DeletionInProgressStatus deletionInProgress) &&
                            deletionInProgress != DeletionInProgressStatus.No;
            if (ignoreBeenDeleted == false && databaseIsBeenDeleted)
                throw new DatabaseDisabledException(databaseName + " is currently being deleted on " + _serverStore.NodeTag);

            if (ignoreNotRelevant == false && databaseRecord.Topology.RelevantFor(_serverStore.NodeTag) == false &&
                databaseIsBeenDeleted == false)
                throw new DatabaseNotRelevantException(databaseName + " is not relevant for " + _serverStore.NodeTag);
            return CreateConfiguration(databaseRecord);
        }

        protected RavenConfiguration CreateConfiguration(DatabaseRecord record)
        {
            Debug.Assert(_serverStore.Disposed == false);
            var config = RavenConfiguration.CreateFrom(_serverStore.Configuration, record.DatabaseName, ResourceType.Database);

            foreach (var setting in record.Settings)
                config.SetSetting(setting.Key, setting.Value);

            config.Initialize();

            return config;
        }

        public DateTime LastWork(DocumentDatabase resource)
        {
            // This allows us to increase the time large databases will be held in memory
            // Using this method, we'll add 0.5 ms per KB, or roughly half a second of idle time per MB.

            var envs = resource.GetAllStoragesEnvironment();

            long dbSize = 0;
            var maxLastWork = resource.LastAccessTime;

            foreach (var env in envs)
            {
                dbSize += env.Environment.Stats().AllocatedDataFileSizeInBytes;

                if (env.Environment.LastWorkTime > maxLastWork)
                    maxLastWork = env.Environment.LastWorkTime;
            }

            return maxLastWork.AddMilliseconds(dbSize / 1024L);
        }

        public async Task<IDisposable> UnloadAndLockDatabase(string dbName, string reason)
        {
            var tcs = Task.FromException<DocumentDatabase>(new DatabaseDisabledException($"The database {dbName} is currently locked because {reason}" )
            {
                Data =
                {
                    [DoNotRemove] = true
                }
            });

            try
            {
                var existing = DatabasesCache.Replace(dbName, tcs);
                if(existing != null)
                    (await existing)?.Dispose();

                return new DisposableAction(() =>
                {
                    DatabasesCache.TryRemove(dbName, out var _);
                });
            }
            catch (Exception)
            {
                DatabasesCache.TryRemove(dbName, out var _);
                throw;
            }
        }

        public enum ClusterDatabaseChangeType
        {
            RecordChanged,
            ValueChanged,
            PendingClusterTransactions
        }

        public void UnloadDirectly(StringSegment databaseName, [CallerMemberName] string caller = null)
        {
            LastRecentlyUsed.TryRemove(databaseName, out _);
            DatabasesCache.RemoveLockAndReturn(databaseName, CompleteDatabaseUnloading, out var _, caller).Dispose();
        }

        private void CompleteDatabaseUnloading(DocumentDatabase database)
        {
            if (database == null)
                return;

            try
            {
                database.Dispose();
            }
            catch (Exception e)
            {
                if (_logger.IsInfoEnabled)
                    _logger.Info("Could not dispose database: " + database.Name, e);
            }

            database.DatabaseShutdownCompleted.Set();
        }
    }
}
