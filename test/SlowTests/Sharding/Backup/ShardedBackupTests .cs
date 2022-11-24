﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Documents.Operations.Backups.Sharding;
using Raven.Client.Documents.Smuggler;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.Operations.Configuration;
using Raven.Server.Documents;
using Raven.Server.Documents.Sharding;
using Raven.Server.ServerWide.Context;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Tests.Infrastructure.Entities;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Sharding.Backup
{
    public class ShardedBackupTests : ClusterTestBase
    {
        public ShardedBackupTests(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public async Task CanBackupSharded(Options options)
        {
            var backupPath = NewDataPath(suffix: $"{options.DatabaseMode}_BackupFolder");

            using (var store1 = Sharding.GetDocumentStore())
            using (var store2 = GetDocumentStore(options))
            {
                await Sharding.Backup.InsertData(store1);

                // assert that index was created on all shards
                await foreach (var shard in Sharding.GetShardsDocumentDatabaseInstancesFor(store1))
                {
                    Assert.Equal(1, shard.IndexStore.Count);
                }

                var waitHandles = await Sharding.Backup.WaitForBackupToComplete(store1);

                var config = Backup.CreateBackupConfiguration(backupPath);
                await Sharding.Backup.UpdateConfigurationAndRunBackupAsync(Server, store1, config);

                Assert.True(WaitHandle.WaitAll(waitHandles, TimeSpan.FromMinutes(1)));

                // import

                var dirs = Directory.GetDirectories(backupPath);

                Assert.Equal(3, dirs.Length);
                var importOptions = new DatabaseSmugglerImportOptions();

                foreach (var dir in dirs)
                {
                    await store2.Smuggler.ImportIncrementalAsync(importOptions, dir);
                    importOptions.OperateOnTypes &= ~DatabaseSmugglerOptions.OperateOnFirstShardOnly;
                }

                await Sharding.Backup.CheckData(store2, options.DatabaseMode, expectedRevisionsCount: 21);
            }
        }

        [RavenFact(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        public async Task CanBackupShardedIncremental()
        {
            var backupPath = NewDataPath(suffix: "_BackupFolder");

            using (var store1 = Sharding.GetDocumentStore())
            using (var store2 = Sharding.GetDocumentStore())
            {
                var shardNumToDocIds = new Dictionary<int, List<string>>();
                var dbRecord = await store1.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(store1.Database));
                var shardedCtx = new ShardedDatabaseContext(Server.ServerStore, dbRecord);

                // generate data on store1, keep track of doc-ids per shard
                using (var session = store1.OpenAsyncSession())
                using (Server.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                {
                    for (int i = 0; i < 100; i++)
                    {
                        var user = new User { Name = i.ToString() };
                        var id = $"users/{i}";

                        var shardNumber = shardedCtx.GetShardNumber(context, id);
                        if (shardNumToDocIds.TryGetValue(shardNumber, out var ids) == false)
                        {
                            shardNumToDocIds[shardNumber] = ids = new List<string>();
                        }
                        ids.Add(id);

                        await session.StoreAsync(user, id);
                    }

                    Assert.Equal(3, shardNumToDocIds.Count);

                    await session.SaveChangesAsync();
                }

                var waitHandles = await Sharding.Backup.WaitForBackupToComplete(store1);

                var config = Backup.CreateBackupConfiguration(backupPath, incrementalBackupFrequency: "* * * * *");
                await Sharding.Backup.UpdateConfigurationAndRunBackupAsync(Server, store1, config);

                Assert.True(WaitHandle.WaitAll(waitHandles, TimeSpan.FromMinutes(1)));

                // import
                var dirs = Directory.GetDirectories(backupPath);
                Assert.Equal(3, dirs.Length);

                foreach (var dir in dirs)
                {
                    await store2.Smuggler.ImportIncrementalAsync(new DatabaseSmugglerImportOptions(), dir);
                }

                // assert
                await AssertDocsInShardedDb(store2, shardNumToDocIds);

                // add more data to store1
                using (var session = store1.OpenAsyncSession())
                using (Server.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                {
                    for (int i = 100; i < 200; i++)
                    {
                        var user = new User { Name = i.ToString() };
                        var id = $"users/{i}";

                        var shardNumber = shardedCtx.GetShardNumber(context, id);
                        shardNumToDocIds[shardNumber].Add(id);

                        await session.StoreAsync(user, id);
                    }

                    await session.SaveChangesAsync();
                }

                waitHandles = await Sharding.Backup.WaitForBackupToComplete(store1);

                await Sharding.Backup.UpdateConfigurationAndRunBackupAsync(Server, store1, config);

                Assert.True(WaitHandle.WaitAll(waitHandles, TimeSpan.FromMinutes(1)));

                // import
                var newDirs = Directory.GetDirectories(backupPath).Except(dirs).ToList();
                Assert.Equal(3, newDirs.Count);

                foreach (var dir in newDirs)
                {
                    await store2.Smuggler.ImportIncrementalAsync(new DatabaseSmugglerImportOptions(), dir);
                }

                // assert
                await AssertDocsInShardedDb(store2, shardNumToDocIds);
            }
        }

        [RavenTheory(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public async Task CanAddServerWideBackup(Options options)
        {
            DoNotReuseServer();

            using (var store1 = GetDocumentStore(options))
            using (var store2 = Sharding.GetDocumentStore())
            using (var store3 = GetDocumentStore())
            {
                await store1.Maintenance.Server.SendAsync(new PutServerWideBackupConfigurationOperation(new ServerWideBackupConfiguration
                {
                    FullBackupFrequency = "* * * * *",
                    Disabled = true,
                    Name = "test"
                }));

                foreach (var store in new[] { store1, store2, store3 })
                {
                    var databaseRecord = await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(store.Database));
                    Assert.Equal(1, databaseRecord.PeriodicBackups.Count);
                    Assert.Equal("Server Wide Backup, test", databaseRecord.PeriodicBackups[0].Name);

                }
            }
        }

        [RavenTheory(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public async Task CanBackupShardedServerWide(Options options)
        {
            DoNotReuseServer();

            const string usersPrefix = "Users";
            const string ordersPrefix = "Orders";

            var backupPath = NewDataPath(suffix: "_BackupFolder");

            using (var store1 = Sharding.GetDocumentStore())
            using (var store2 = GetDocumentStore())
            {
                // generate data on store1 and store2
                using (var session1 = store1.OpenAsyncSession())
                using (var session2 = store2.OpenAsyncSession())
                {
                    for (int i = 0; i < 100; i++)
                    {
                        await session1.StoreAsync(new User(), $"{usersPrefix}/{i}");
                        await session2.StoreAsync(new Order(), $"{ordersPrefix}/{i}");
                    }

                    await session1.SaveChangesAsync();
                    await session2.SaveChangesAsync();
                }

                // define server wide backup
                await store1.Maintenance.Server.SendAsync(new PutServerWideBackupConfigurationOperation(new ServerWideBackupConfiguration
                {
                    FullBackupFrequency = "* * * * *",
                    Disabled = false,
                    LocalSettings = new LocalSettings
                    {
                        FolderPath = backupPath
                    }
                }));

                // wait for backups to complete
                var backupsDone = await Sharding.Backup.WaitForBackupToComplete(store1);
                var backupsDone2 = await Backup.WaitForBackupToComplete(store2);

                Assert.True(WaitHandle.WaitAll(backupsDone, TimeSpan.FromMinutes(2)));
                Assert.True(WaitHandle.WaitAll(backupsDone2, TimeSpan.FromMinutes(2)));

                var dirs = Directory.GetDirectories(backupPath);
                Assert.Equal(2, dirs.Length);

                var store1Backups = Directory.GetDirectories(Path.Combine(backupPath, store1.Database));
                var store2Backup = Directory.GetDirectories(Path.Combine(backupPath, store2.Database));

                Assert.Equal(3, store1Backups.Length); // one per shard
                Assert.Single(store2Backup);

                // import data to new stores and assert
                using (var store3 = GetDocumentStore(options))
                using (var store4 = GetDocumentStore(options))
                {
                    foreach (var dir in store1Backups)
                    {
                        await store3.Smuggler.ImportIncrementalAsync(new DatabaseSmugglerImportOptions(), dir);
                    }

                    await store4.Smuggler.ImportIncrementalAsync(new DatabaseSmugglerImportOptions(), store2Backup[0]);

                    await AssertDocs(store3, idPrefix: usersPrefix, dbMode: options.DatabaseMode);
                    await AssertDocs(store4, idPrefix: ordersPrefix, dbMode: options.DatabaseMode);
                }
            }
        }

        [RavenTheory(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public async Task OneTimeBackupSharded(Options options)
        {
            var backupPath = NewDataPath(suffix: "_BackupFolder");
            const string idPrefix = "users";

            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    for (int i = 0; i < 100; i++)
                    {
                        await session.StoreAsync(new User(), $"{idPrefix}/{i}");
                    }
                    await session.SaveChangesAsync();
                }

                var config = new BackupConfiguration
                {
                    BackupType = BackupType.Backup,
                    LocalSettings = new LocalSettings
                    {
                        FolderPath = backupPath
                    }
                };

                var operation = await store.Maintenance.SendAsync(new BackupOperation(config));

                var result = await operation.WaitForCompletionAsync(TimeSpan.FromSeconds(60));
                var backupResult = (ShardedBackupResult)result;
                Assert.NotNull(backupResult);
                Assert.Equal(100, backupResult.Results.Sum(x => x.Result.Documents.ReadCount));

                var dirs = Directory.GetDirectories(backupPath);
                Assert.Equal(3, dirs.Length);
                using (var store2 = GetDocumentStore(options))
                {
                    foreach (var dir in dirs)
                    {
                        await store2.Smuggler.ImportIncrementalAsync(new DatabaseSmugglerImportOptions(), dir);
                    }

                    await AssertDocs(store2, idPrefix, options.DatabaseMode);
                }
            }
        }

        [RavenTheory(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        [RavenData(DatabaseMode = RavenDatabaseMode.All)]
        public async Task BackupNowSharded(Options options)
        {
            var backupPath = NewDataPath(suffix: "_BackupFolder");
            const string idPrefix = "users";

            using (var store = GetDocumentStore(options))
            {
                var config = Backup.CreateBackupConfiguration(backupPath, fullBackupFrequency: "0 2 * * 0", incrementalBackupFrequency: "0 2 * * 1");

                var result = await store.Maintenance.SendAsync(new UpdatePeriodicBackupOperation(config));
                var backupTaskId = result.TaskId;

                using (var session = store.OpenAsyncSession())
                {
                    for (int i = 0; i < 100; i++)
                    {
                        await session.StoreAsync(new User(), $"{idPrefix}/{i}");
                    }
                    await session.SaveChangesAsync();
                }

                var op = await store.Maintenance.SendAsync(new StartBackupOperation(isFullBackup: true, backupTaskId));
                var operationResult = await op.WaitForCompletionAsync();

                string[] dirs = null;
                if (options.DatabaseMode == RavenDatabaseMode.Single)
                {
                    var backupResult = operationResult as BackupResult;
                    Assert.Equal(100, backupResult.Documents.ReadCount);
                    dirs = Directory.GetDirectories(backupPath);
                    Assert.Equal(1, dirs.Length);
                }
                else if (options.DatabaseMode == RavenDatabaseMode.Sharded)
                {
                    var sbr = operationResult as ShardedBackupResult;
                    Assert.Equal(3, sbr.Results.Count);
                    Assert.Equal(100, sbr.Results.Sum(x => x.Result.Documents.ReadCount));
                    dirs = Directory.GetDirectories(backupPath);
                    Assert.Equal(3, dirs.Length);
                }

                using (var store2 = GetDocumentStore(options))
                {
                    foreach (var dir in dirs)
                    {
                        await store2.Smuggler.ImportIncrementalAsync(new DatabaseSmugglerImportOptions(), dir);
                    }

                    await AssertDocs(store2, idPrefix, options.DatabaseMode);
                }
            }
        }

        [RavenFact(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        public async Task CanGetShardedBackupStatus()
        {
            var backupPath = NewDataPath(suffix: "BackupFolder");
            var cluster = await CreateRaftCluster(3, watcherCluster: true);

            var options = Sharding.GetOptionsForCluster(cluster.Leader, shards: 3, shardReplicationFactor: 1, orchestratorReplicationFactor: 3);
            using (var store = Sharding.GetDocumentStore(options))
            {
                using (var session = store.OpenSession())
                {
                    for (int i = 0; i < 10; i++)
                    {
                        session.Store(new User(), $"users/{i}");
                    }

                    session.SaveChanges();
                }

                var waitHandles = await Sharding.Backup.WaitForBackupsToComplete(cluster.Nodes, store.Database);

                var config = Backup.CreateBackupConfiguration(backupPath);
                var backupTaskId = await Sharding.Backup.UpdateConfigurationAndRunBackupAsync(cluster.Nodes, store, config);

                Assert.True(WaitHandle.WaitAll(waitHandles, TimeSpan.FromMinutes(1)));

                var dirs = Directory.GetDirectories(backupPath).ToList();
                Assert.Equal(cluster.Nodes.Count, dirs.Count);

                var status = store.Maintenance.Send(new GetShardedPeriodicBackupStatusOperation(backupTaskId));

                Assert.Equal(3, status.Statuses.Length);
                foreach (var shardBackupStatus in status.Statuses)
                {
                    Assert.NotNull(shardBackupStatus);
                }
            }
        }

        [RavenFact(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        public async Task ShouldThrowOnAttemptToGetNonShardedBackupStatusFromShardedDb()
        {
            var backupPath = NewDataPath(suffix: "BackupFolder");
            var cluster = await CreateRaftCluster(3, watcherCluster: true);

            var options = Sharding.GetOptionsForCluster(cluster.Leader, shards: 3, shardReplicationFactor: 1, orchestratorReplicationFactor: 3);
            using (var store = Sharding.GetDocumentStore(options))
            {
                using (var session = store.OpenSession())
                {
                    for (int i = 0; i < 10; i++)
                    {
                        session.Store(new User(), $"users/{i}");
                    }

                    session.SaveChanges();
                }

                var waitHandles = await Sharding.Backup.WaitForBackupsToComplete(cluster.Nodes, store.Database);

                var config = Backup.CreateBackupConfiguration(backupPath);
                var backupTaskId = await Sharding.Backup.UpdateConfigurationAndRunBackupAsync(cluster.Nodes, store, config);

                Assert.True(WaitHandle.WaitAll(waitHandles, TimeSpan.FromMinutes(1)));

                var dirs = Directory.GetDirectories(backupPath).ToList();
                Assert.Equal(cluster.Nodes.Count, dirs.Count);

                var op = store.Maintenance.SendAsync(new GetPeriodicBackupStatusOperation(backupTaskId));

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await op);

                Assert.Contains($"Database is sharded, can't use {nameof(GetPeriodicBackupStatusOperation)}, " +
                                $"use {nameof(GetShardedPeriodicBackupStatusOperation)} instead", ex.Message);
            }
        }

        [RavenFact(RavenTestCategory.BackupExportImport | RavenTestCategory.Sharding)]
        public async Task ShouldThrowOnAttemptToGetShardedBackupStatusFromNonShardedDb()
        {
            var backupPath = NewDataPath(suffix: "BackupFolder");
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    for (int i = 0; i < 10; i++)
                    {
                        session.Store(new User(), $"users/{i}");
                    }

                    session.SaveChanges();
                }

                var waitHandles = await Backup.WaitForBackupToComplete(store);

                var config = Backup.CreateBackupConfiguration(backupPath);
                var backupTaskId = await Backup.UpdateConfigAndRunBackupAsync(Server, config, store);

                Assert.True(WaitHandle.WaitAll(waitHandles, TimeSpan.FromMinutes(1)));

                var dirs = Directory.GetDirectories(backupPath).ToList();
                Assert.Equal(1, dirs.Count);

                var op = store.Maintenance.SendAsync(new GetShardedPeriodicBackupStatusOperation(backupTaskId));

                var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await op);

                Assert.Contains($"Database is not sharded, can't use {nameof(GetShardedPeriodicBackupStatusOperation)}, " +
                                $"use {nameof(GetPeriodicBackupStatusOperation)} instead", ex.Message);
            }
        }

        private Task AssertDocs(IDocumentStore store, string idPrefix, RavenDatabaseMode dbMode, int count = 100)
        {
            return dbMode switch
            {
                RavenDatabaseMode.Single => AssertDocs(store, idPrefix, count),
                RavenDatabaseMode.Sharded => AssertDocsInShardedDb(store, idPrefix, count),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private async Task AssertDocs(IDocumentStore store, string idPrefix, int count = 100)
        {
            var ids = Enumerable.Range(0, count).Select(x => $"{idPrefix}/{x}").ToList();
            var db = await GetDocumentDatabaseInstanceFor(store);
            AssertDocs(db, ids);
        }

        private static void AssertDocs(DocumentDatabase database, IReadOnlyCollection<string> ids)
        {
            using (database.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            using (context.OpenReadTransaction())
            {
                var docs = database.DocumentsStorage.GetDocumentsFrom(context, 0).ToList();
                Assert.NotEmpty(docs);
                Assert.Equal(ids.Count, docs.Count);

                foreach (var doc in docs)
                {
                    Assert.Contains(doc.Id, ids);
                }
            }
        }

        private async Task AssertDocsInShardedDb(IDocumentStore store, string idPrefix, int count = 100)
        {
            var dbRecord = await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(store.Database));
            if (dbRecord.IsSharded == false)
                throw new InvalidOperationException($"database {store.Database} is not sharded");

            var shardedCtx = new ShardedDatabaseContext(Server.ServerStore, dbRecord);
            var shardNumToDocIds = new Dictionary<int, List<string>>();

            using (Server.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                for (int i = 0; i < count; i++)
                {
                    var id = $"{idPrefix}/{i}";
                    var shardNumber = shardedCtx.GetShardNumber(context, id);
                    if (shardNumToDocIds.TryGetValue(shardNumber, out var ids) == false)
                    {
                        shardNumToDocIds[shardNumber] = ids = new List<string>();
                    }

                    ids.Add(id);

                }

                Assert.Equal(dbRecord.Sharding.Shards.Length, shardNumToDocIds.Count);
            }

            await AssertDocsInShardedDb(store, shardNumToDocIds);
        }

        private async Task AssertDocsInShardedDb(IDocumentStore store, Dictionary<int, List<string>> shardNumToDocIds)
        {
            foreach ((int shardNumber, List<string> ids) in shardNumToDocIds)
            {
                var shard = await GetDocumentDatabaseInstanceFor(store, $"{store.Database}${shardNumber}");
                AssertDocs(shard, ids);
            }
        }

    }
}
