﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Database;
using Raven.Client.Json.Converters;
using Raven.Client.Http;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.ETL;
using Raven.Client.ServerWide.ETL.SQL;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.PeriodicBackup;
using Raven.Server.Documents;
using Raven.Server.Documents.ETL;
using Raven.Server.Documents.ETL.Providers.Raven;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Commands;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Web.System
{
    public class OngoingTasksHandler : DatabaseRequestHandler
    {
        [RavenAction("/databases/*/tasks", "GET", AuthorizationStatus.ValidUser)]
        public Task GetOngoingTasks()
        {
            var result = GetOngoingTasksFor(Database.Name, ServerStore);

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                context.Write(writer, result.ToJson());
            }

            return Task.CompletedTask;
        }

        public static OngoingTasksResult GetOngoingTasksFor(string dbName, ServerStore store)
        {
            var ongoingTasksResult = new OngoingTasksResult();
            using (store.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                DatabaseTopology dbTopology;
                ClusterTopology clusterTopology;
                DatabaseRecord databaseRecord;

                using (context.OpenReadTransaction())
                {
                    databaseRecord = store.Cluster.ReadDatabase(context, dbName);

                    if (databaseRecord == null)
                    {
                        return ongoingTasksResult;
                    }

                    dbTopology = databaseRecord.Topology;
                    clusterTopology = store.GetClusterTopology(context);

                    ongoingTasksResult.OngoingTasksList.AddRange(CollectSubscriptionTasks(context, databaseRecord, clusterTopology, store));
                }

                foreach (var tasks in new[]
                {
                    CollectExternalReplicationTasks(databaseRecord.DatabaseName, databaseRecord.ExternalReplication, dbTopology,clusterTopology, store),
                    CollectEtlTasks(databaseRecord, dbTopology, clusterTopology, store),
                    CollectBackupTasks(databaseRecord, dbTopology, clusterTopology, store)
                })
                {
                    ongoingTasksResult.OngoingTasksList.AddRange(tasks);
                }

                if (store.DatabasesLandlord.DatabasesCache.TryGetValue(dbName, out var database) && database.Status == TaskStatus.RanToCompletion)
                {
                    ongoingTasksResult.SubscriptionsCount = (int)database.Result.SubscriptionStorage.GetAllSubscriptionsCount();
                }

                return ongoingTasksResult;
            }
        }

        private static IEnumerable<OngoingTask> CollectSubscriptionTasks(TransactionOperationContext context, DatabaseRecord databaseRecord, ClusterTopology clusterTopology, ServerStore store)
        {
            foreach (var keyValue in ClusterStateMachine.ReadValuesStartingWith(context, SubscriptionState.SubscriptionPrefix(databaseRecord.DatabaseName)))
            {
                var subscriptionState = JsonDeserializationClient.SubscriptionState(keyValue.Value);
                var tag = databaseRecord.Topology.WhoseTaskIsIt(subscriptionState, store.IsPassive());

                yield return new OngoingTaskSubscription
                {
                    // Supply only needed fields for List View  
                    ResponsibleNode = new NodeId
                    {
                        NodeTag = tag,
                        NodeUrl = clusterTopology.GetUrlFromTag(tag)
                    },
                    TaskName = subscriptionState.SubscriptionName,
                    TaskState = subscriptionState.Disabled ? OngoingTaskState.Disabled : OngoingTaskState.Enabled,
                    TaskId = subscriptionState.SubscriptionId,
                    Query = subscriptionState.Query
                };
            }
        }

        private static IEnumerable<OngoingTask> CollectExternalReplicationTasks(string name, List<ExternalReplication> watchers, DatabaseTopology dbTopology, ClusterTopology clusterTopology, ServerStore store)
        {
            if (dbTopology == null)
                yield break;

            foreach (var watcher in watchers)
            {
                var taskInfo = GetExternalReplicationInfo(name, dbTopology, clusterTopology, store, watcher);

                yield return taskInfo;
            }
        }

        private static OngoingTaskReplication GetExternalReplicationInfo(string name, DatabaseTopology dbTopology, ClusterTopology clusterTopology, ServerStore store,
            ExternalReplication watcher)
        {
            NodeId responsibale = null;

            var tag = dbTopology.WhoseTaskIsIt(watcher, store.IsPassive());
            if (tag != null)
            {
                responsibale = new NodeId
                {
                    NodeTag = tag,
                    NodeUrl = clusterTopology.GetUrlFromTag(tag)
                };
            }

            (string Url, OngoingTaskConnectionStatus Status) res = (null, OngoingTaskConnectionStatus.None);
            string error = null;
            if (tag == store.NodeTag)
            {
                error =  GetDatabase(name, store, out var db);
                if (db != null)
                {
                    res = db.ReplicationLoader.GetExternalReplicationDestination(watcher.TaskId);
                }
            }
            else
            {
                res.Status = OngoingTaskConnectionStatus.NotOnThisNode;
            }

            var taskInfo = new OngoingTaskReplication
            {
                TaskId = watcher.TaskId,
                TaskName = watcher.Name,
                ResponsibleNode = responsibale,
                DestinationDatabase = watcher.Database,
                TaskState = watcher.Disabled ? OngoingTaskState.Disabled : OngoingTaskState.Enabled,
                DestinationUrl = res.Url,
                TaskConnectionStatus = res.Status,
                Error = error
            };
            
            return taskInfo;
        }
        
        private static IEnumerable<OngoingTask> CollectBackupTasks(
            DatabaseRecord databaseRecord,
            DatabaseTopology dbTopology,
            ClusterTopology clusterTopology,
            ServerStore store)
        {
            if (dbTopology == null)
                yield break;

            if (databaseRecord.PeriodicBackups == null)
                yield break;

            if (databaseRecord.PeriodicBackups.Count == 0)
                yield break;

            var database = store.DatabasesLandlord.TryGetOrCreateResourceStore(databaseRecord.DatabaseName).Result;

            foreach (var backupConfiguration in databaseRecord.PeriodicBackups)
            {
                var tag = dbTopology.WhoseTaskIsIt(backupConfiguration, store.IsPassive());

                var backupDestinations = GetBackupDestinations(backupConfiguration);

                var backupStatus = database.PeriodicBackupRunner.GetBackupStatus(backupConfiguration.TaskId);
                var nextBackup = database.PeriodicBackupRunner.GetNextBackupDetails(databaseRecord, backupConfiguration, backupStatus);

                yield return new OngoingTaskBackup
                {
                    TaskId = backupConfiguration.TaskId,
                    BackupType = backupConfiguration.BackupType,
                    TaskName = backupConfiguration.Name,
                    TaskState = backupConfiguration.Disabled ? OngoingTaskState.Disabled : OngoingTaskState.Enabled,
                    LastFullBackup = backupStatus.LastFullBackup,
                    LastIncrementalBackup = backupStatus.LastIncrementalBackup,
                    NextBackup = nextBackup,
                    ResponsibleNode = new NodeId
                    {
                        NodeTag = tag,
                        NodeUrl = clusterTopology.GetUrlFromTag(tag)
                    },
                    BackupDestinations = backupDestinations
                };
            }
        }

        private static List<string> GetBackupDestinations(PeriodicBackupConfiguration backupConfiguration)
        {
            var backupDestinations = new List<string>();

            if (backupConfiguration.LocalSettings != null && backupConfiguration.LocalSettings.Disabled == false)
                backupDestinations.Add("Local");
            if (backupConfiguration.AzureSettings != null && backupConfiguration.AzureSettings.Disabled == false)
                backupDestinations.Add("Azure");
            if (backupConfiguration.S3Settings != null && backupConfiguration.S3Settings.Disabled == false)
                backupDestinations.Add("S3");
            if (backupConfiguration.GlacierSettings != null && backupConfiguration.GlacierSettings.Disabled == false)
                backupDestinations.Add("Glacier");
            if (backupConfiguration.FtpSettings != null && backupConfiguration.FtpSettings.Disabled == false)
                backupDestinations.Add("FTP");

            return backupDestinations;
        }

        private static IEnumerable<OngoingTask> CollectEtlTasks(DatabaseRecord databaseRecord, DatabaseTopology dbTopology, ClusterTopology clusterTopology, ServerStore store)
        {
            if (dbTopology == null)
                yield break;

            if (databaseRecord.RavenEtls != null)
            {
                foreach (var ravenEtl in databaseRecord.RavenEtls)
                {
                    var tag = dbTopology.WhoseTaskIsIt(ravenEtl, store.IsPassive());

                    var taskState = GetEtlTaskState(ravenEtl);

                    if (databaseRecord.RavenConnectionStrings.TryGetValue(ravenEtl.ConnectionStringName, out var connection) == false)
                        throw new InvalidOperationException(
                            $"Could not find connection string named '{ravenEtl.ConnectionStringName}' in the database record for '{ravenEtl.Name}' ETL");


                    (string Url, OngoingTaskConnectionStatus Status) res = (null, OngoingTaskConnectionStatus.None);
                    string error = null;
                    if (tag == store.NodeTag)
                    {
                        error = GetDatabase(databaseRecord.DatabaseName, store, out var db);
                        if (db != null)
                        {
                            foreach (var process in db.EtlLoader.Processes)
                            {
                                if (process is RavenEtl etlProcess)
                                {
                                    if (etlProcess.Name == ravenEtl.Name)
                                    {
                                        res.Url = etlProcess.Url;
                                        res.Status = OngoingTaskConnectionStatus.Active;
                                        break;
                                    }
                                }
                            }
                            if (res.Status == OngoingTaskConnectionStatus.None)
                            {
                                error = $"The raven etl process'{ravenEtl.Name}' was not found.";
                            }
                        }
                    }
                    else
                    {
                        res.Status = OngoingTaskConnectionStatus.NotOnThisNode;
                    }

                    yield return new OngoingTaskRavenEtlListView()
                    {
                        TaskId = ravenEtl.TaskId,
                        TaskName = ravenEtl.Name,
                        // TODO arek TaskConnectionStatus = 
                        TaskState = taskState,
                        ResponsibleNode = new NodeId
                        {
                            NodeTag = tag,
                            NodeUrl = clusterTopology.GetUrlFromTag(tag)
                        },
                        DestinationUrl = res.Url,
                        TaskConnectionStatus = res.Status,
                        DestinationDatabase = connection.Database,
                        ConnectionStringName = ravenEtl.ConnectionStringName,
                        Error = error
                    };
                }
            }

            if (databaseRecord.SqlEtls != null)
            {
                foreach (var sqlEtl in databaseRecord.SqlEtls)
                {
                    var tag = dbTopology.WhoseTaskIsIt(sqlEtl, store.IsPassive());

                    var taskState = GetEtlTaskState(sqlEtl);

                    if (databaseRecord.SqlConnectionStrings.TryGetValue(sqlEtl.ConnectionStringName, out var sqlConnection) == false)
                        throw new InvalidOperationException(
                            $"Could not find connection string named '{sqlEtl.ConnectionStringName}' in the database record for '{sqlEtl.Name}' ETL");

                    var (database, server) =
                        SqlConnectionStringParser.GetDatabaseAndServerFromConnectionString(sqlEtl.FactoryName, sqlConnection.ConnectionString);

                    yield return new OngoingTaskSqlEtlListView()
                    {
                        TaskId = sqlEtl.TaskId,
                        TaskName = sqlEtl.Name,
                        // TODO arek TaskConnectionStatus = 
                        TaskState = taskState,
                        ResponsibleNode = new NodeId
                        {
                            NodeTag = tag,
                            NodeUrl = clusterTopology.GetUrlFromTag(tag)
                        },
                        DestinationServer = server,
                        DestinationDatabase = database,
                        ConnectionStringName = sqlEtl.ConnectionStringName
                    };
                }
            }
        }

        private static string GetDatabase(string name, ServerStore store, out DocumentDatabase db)
        {
            string error = null;
            db = null;
            try
            {
                if (store.DatabasesLandlord.DatabasesCache.TryGetValue(name, out var task))
                {
                    if (task == null)
                    {
                        throw new DatabaseLoadFailureException("Database task is 'null'.");
                    }
                    if (task.IsCanceled)
                    {
                        throw new TaskCanceledException("Database task was canceled.");
                    }
                    if (task.IsFaulted)
                    {
                        throw new DatabaseLoadFailureException("Database task is faulted.", task.Exception);
                    }
                    db = task.Result;
                }
                DatabaseDoesNotExistException.Throw(name);
            }
            catch (Exception e)
            {
                error = $"Failed to load database '{name}' due to: \n {e}";
            }
            return error;
        }

        // Get Info about a specific task - For Edit View in studio - Each task should return its own specific object
        [RavenAction("/databases/*/task", "GET", AuthorizationStatus.ValidUser)]
        public Task GetOngoingTaskInfo()
        {
            if (ResourceNameValidator.IsValidResourceName(Database.Name, ServerStore.Configuration.Core.DataDirectory.FullPath, out string errorMessage) == false)
                throw new BadRequestException(errorMessage);

            var key = GetLongQueryString("key");
            var typeStr = GetQueryStringValueAndAssertIfSingleAndNotEmpty("type");

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                using (context.OpenReadTransaction())
                {
                    var clusterTopology = ServerStore.GetClusterTopology(context);
                    var record = ServerStore.Cluster.ReadDatabase(context, Database.Name);
                    var dbTopology = record?.Topology;

                    if (Enum.TryParse<OngoingTaskType>(typeStr, true, out var type) == false)
                        throw new ArgumentException($"Unknown task type: {type}", "type");

                    string tag;

                    switch (type)
                    {
                        case OngoingTaskType.Replication:

                            var watcher = record?.ExternalReplication.Find(x => x.TaskId == key);
                            if (watcher == null)
                            {
                                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                                break;
                            }
                            var taskInfo = GetExternalReplicationInfo(Database.Name, dbTopology, clusterTopology, ServerStore, watcher);

                            WriteResult(context, taskInfo);

                            break;

                        case OngoingTaskType.Backup:

                            var backupConfiguration = record?.PeriodicBackups?.Find(x => x.TaskId == key);
                            if (backupConfiguration == null)
                            {
                                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                                break;
                            }

                            tag = dbTopology?.WhoseTaskIsIt(backupConfiguration, ServerStore.IsPassive());
                            var backupDestinations = GetBackupDestinations(backupConfiguration);
                            var backupStatus = Database.PeriodicBackupRunner.GetBackupStatus(key);
                            var nextBackup = Database.PeriodicBackupRunner.GetNextBackupDetails(record, backupConfiguration, backupStatus);

                            var backupTaskInfo = new OngoingTaskBackup
                            {
                                TaskId = backupConfiguration.TaskId,
                                BackupType = backupConfiguration.BackupType,
                                TaskName = backupConfiguration.Name,
                                TaskState = backupConfiguration.Disabled ? OngoingTaskState.Disabled : OngoingTaskState.Enabled,
                                ResponsibleNode = new NodeId
                                {
                                    NodeTag = tag,
                                    NodeUrl = clusterTopology.GetUrlFromTag(tag)
                                },
                                BackupDestinations = backupDestinations,
                                LastFullBackup = backupStatus.LastFullBackup,
                                LastIncrementalBackup = backupStatus.LastIncrementalBackup,
                                NextBackup = nextBackup
                            };

                            WriteResult(context, backupTaskInfo);
                            break;

                        case OngoingTaskType.SqlEtl:

                            var sqlEtl = record?.SqlEtls?.Find(x => x.TaskId == key);
                            if (sqlEtl == null)
                            {
                                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                                break;
                            }

                            WriteResult(context, new OngoingTaskSqlEtlDetails()
                            {
                                TaskId = sqlEtl.TaskId,
                                TaskName = sqlEtl.Name,
                                Configuration = sqlEtl,
                                TaskState = GetEtlTaskState(sqlEtl)
                            });
                            break;

                        case OngoingTaskType.RavenEtl:

                            var ravenEtl = record?.RavenEtls?.Find(x => x.TaskId == key);
                            if (ravenEtl == null)
                            {
                                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                                break;
                            }

                            WriteResult(context, new OngoingTaskRavenEtlDetails()
                            {
                                TaskId = ravenEtl.TaskId,
                                TaskName = ravenEtl.Name,
                                Configuration = ravenEtl,
                                TaskState = GetEtlTaskState(ravenEtl)
                            });
                            break;

                        case OngoingTaskType.Subscription:

                            var nameKey = GetQueryStringValueAndAssertIfSingleAndNotEmpty("taskName");
                            var itemKey = SubscriptionState.GenerateSubscriptionItemKeyName(record.DatabaseName, nameKey);
                            var doc = ServerStore.Cluster.Read(context, itemKey);
                            if (doc == null)
                            {
                                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                                break;
                            }

                            var subscriptionState = JsonDeserializationClient.SubscriptionState(doc);
                            tag = dbTopology?.WhoseTaskIsIt(subscriptionState, ServerStore.IsPassive());

                            var subscriptionStateInfo = new SubscriptionStateWithNodeDetails
                            {
                                Query = subscriptionState.Query,
                                ChangeVectorForNextBatchStartingPoint = subscriptionState.ChangeVectorForNextBatchStartingPoint,
                                SubscriptionId = subscriptionState.SubscriptionId,
                                SubscriptionName = subscriptionState.SubscriptionName,
                                LastTimeServerMadeProgressWithDocuments = subscriptionState.LastTimeServerMadeProgressWithDocuments,
                                Disabled = subscriptionState.Disabled,
                                LastClientConnectionTime = subscriptionState.LastClientConnectionTime,
                                MentorNode = subscriptionState.MentorNode,
                                ResponsibleNode = new NodeId
                                {
                                    NodeTag = tag,
                                    NodeUrl = clusterTopology.GetUrlFromTag(tag)
                                }
                            };

                            // Todo: here we'll need to talk with the running node? TaskConnectionStatus = subscriptionState.Disabled ? OngoingTaskConnectionStatus.NotActive : OngoingTaskConnectionStatus.Active,

                            WriteResult(context, subscriptionStateInfo.ToJson());
                            break;

                        default:
                            HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                            break;
                    }
                }
            }

            return Task.CompletedTask;
        }

        private void WriteResult(JsonOperationContext context, OngoingTask taskInfo)
        {
            HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;

            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                context.Write(writer, taskInfo.ToJson());
                writer.Flush();
            }
        }

        private void WriteResult(JsonOperationContext context, DynamicJsonValue dynamicJsonValue)
        {
            HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;

            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                context.Write(writer, dynamicJsonValue);
                writer.Flush();
            }
        }

        [RavenAction("/databases/*/subscription-tasks/state", "POST", AuthorizationStatus.ValidUser)]
        public async Task ToggleSubscriptionTaskState()
        {
            // Note: Only Subscription task needs User authentication, All other tasks need Admin authentication
            var typeStr = GetQueryStringValueAndAssertIfSingleAndNotEmpty("type");
            if (Enum.TryParse<OngoingTaskType>(typeStr, true, out var type) == false)
                throw new ArgumentException($"Unknown task type: {type}", nameof(type));

            if (type != OngoingTaskType.Subscription)
                throw new ArgumentException("Only Subscription type can call this method");

            await ToggleTaskState();
        }

        [RavenAction("/databases/*/admin/tasks/state", "POST", AuthorizationStatus.Operator)]
        public async Task ToggleTaskState()
        {
            if (ResourceNameValidator.IsValidResourceName(Database.Name, ServerStore.Configuration.Core.DataDirectory.FullPath, out string errorMessage) == false)
                throw new BadRequestException(errorMessage);

            var key = GetLongQueryString("key");
            var typeStr = GetQueryStringValueAndAssertIfSingleAndNotEmpty("type");
            var disable = GetBoolValueQueryString("disable") ?? true;
            var taskName = GetStringQueryString("taskName", required: false);

            if (Enum.TryParse<OngoingTaskType>(typeStr, true, out var type) == false)
                throw new ArgumentException($"Unknown task type: {type}", nameof(type));

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                var (index, _) = await ServerStore.ToggleTaskState(key, taskName, type, disable, Database.Name);
                await Database.RachisLogIndexNotifications.WaitForIndexNotification(index);

                HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;

                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    context.Write(writer, new DynamicJsonValue
                    {
                        [nameof(ModifyOngoingTaskResult.TaskId)] = key,
                        [nameof(ModifyOngoingTaskResult.RaftCommandIndex)] = index
                    });
                    writer.Flush();
                }
            }
        }

        [RavenAction("/databases/*/admin/tasks/external-replication", "POST", AuthorizationStatus.Operator)]
        public async Task UpdateExternalReplication()
        {
            if (ResourceNameValidator.IsValidResourceName(Database.Name, ServerStore.Configuration.Core.DataDirectory.FullPath, out string errorMessage) == false)
                throw new BadRequestException(errorMessage);

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                var updateJson = await context.ReadForMemoryAsync(RequestBodyStream(), "read-update-replication");
                if (updateJson.TryGet(nameof(UpdateExternalReplicationCommand.Watcher), out BlittableJsonReaderObject watcherBlittable) == false)
                {
                    throw new InvalidDataException($"{nameof(UpdateExternalReplicationCommand.Watcher)} was not found.");
                }

                var watcher = JsonDeserializationClient.ExternalReplication(watcherBlittable);
                if (ServerStore.LicenseManager.CanAddExternalReplication(out var licenseLimit) == false)
                {
                    SetLicenseLimitResponse(licenseLimit);
                    return;
                }

                var (index, _) = await ServerStore.UpdateExternalReplication(Database.Name, watcher);
                await Database.RachisLogIndexNotifications.WaitForIndexNotification(index);
                string responsibleNode;
                using (context.OpenReadTransaction())
                {
                    var record = ServerStore.Cluster.ReadDatabase(context, Database.Name);
                    responsibleNode = record.Topology.WhoseTaskIsIt(watcher, ServerStore.IsPassive());
                }

                HttpContext.Response.StatusCode = (int)HttpStatusCode.Created;

                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    context.Write(writer, new DynamicJsonValue
                    {
                        [nameof(ModifyOngoingTaskResult.TaskId)] = watcher.TaskId == 0 ? index : watcher.TaskId,
                        [nameof(ModifyOngoingTaskResult.RaftCommandIndex)] = index,
                        [nameof(OngoingTask.ResponsibleNode)] = responsibleNode
                    });
                    writer.Flush();
                }
            }
        }


        [RavenAction("/databases/*/subscription-tasks", "DELETE", AuthorizationStatus.ValidUser)]
        public async Task DeleteSubscriptionTask()
        {
            // Note: Only Subscription task needs User authentication, All other tasks need Admin authentication
            var typeStr = GetQueryStringValueAndAssertIfSingleAndNotEmpty("type");
            if (Enum.TryParse<OngoingTaskType>(typeStr, true, out var type) == false)
                throw new ArgumentException($"Unknown task type: {type}", nameof(type));

            if (type != OngoingTaskType.Subscription)
                throw new ArgumentException("Only Subscription type can call this method");

            await DeleteOngoingTask();
        }

        [RavenAction("/databases/*/admin/tasks", "DELETE", AuthorizationStatus.Operator)]
        public async Task DeleteOngoingTask()
        {
            if (ResourceNameValidator.IsValidResourceName(Database.Name, ServerStore.Configuration.Core.DataDirectory.FullPath, out string errorMessage) == false)
                throw new BadRequestException(errorMessage);

            var id = GetLongQueryString("id");
            var typeStr = GetQueryStringValueAndAssertIfSingleAndNotEmpty("type");
            var taskName = GetStringQueryString("taskName", required: false);

            if (Enum.TryParse<OngoingTaskType>(typeStr, true, out var type) == false)
                throw new ArgumentException($"Unknown task type: {type}", "type");

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                var (index, _) = await ServerStore.DeleteOngoingTask(id, taskName, type, Database.Name);
                await Database.RachisLogIndexNotifications.WaitForIndexNotification(index);

                HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;

                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    context.Write(writer, new DynamicJsonValue
                    {
                        [nameof(ModifyOngoingTaskResult.TaskId)] = id,
                        [nameof(ModifyOngoingTaskResult.RaftCommandIndex)] = index
                    });
                    writer.Flush();
                }
            }
        }

        private static OngoingTaskState GetEtlTaskState<T>(EtlConfiguration<T> config) where T : ConnectionString
        {
            var taskState = OngoingTaskState.Enabled;

            if (config.Disabled || config.Transforms.All(x => x.Disabled))
                taskState = OngoingTaskState.Disabled;
            else if (config.Transforms.Any(x => x.Disabled))
                taskState = OngoingTaskState.PartiallyEnabled;

            return taskState;
        }
    }

    public class OngoingTasksResult : IDynamicJson
    {
        public List<OngoingTask> OngoingTasksList { get; set; }
        public int SubscriptionsCount { get; set; }

        public OngoingTasksResult()
        {
            OngoingTasksList = new List<OngoingTask>();
        }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(OngoingTasksList)] = new DynamicJsonArray(OngoingTasksList.Select(x => x.ToJson())),
                [nameof(SubscriptionsCount)] = SubscriptionsCount
            };
        }
    }
}
