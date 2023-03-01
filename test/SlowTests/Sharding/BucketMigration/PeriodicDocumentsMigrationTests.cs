﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastTests.Server.Replication;
using Orders;
using Raven.Client.Documents;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.Sharding;
using Raven.Server.Config;
using Raven.Server.Utils;
using Raven.Tests.Core.Utils.Entities;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Sharding.BucketMigration
{
    public class PeriodicDocumentsMigrationTests : ReplicationTestBase
    {
        public PeriodicDocumentsMigrationTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_SimpleCase()
        {
            using (var store = Sharding.GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }

                var shard = await Sharding.GetShardNumberFor(store, "users/1");
                var wrongShard = (shard + 1) % 2;

                var db = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(store.Database, wrongShard));
                db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

                using (var session = store.OpenSession(ShardHelper.ToShardName(store.Database, wrongShard)))
                {
                    session.Store(new User(), "users/2$users/1");
                    session.Store(new User(), "users/3$users/1");
                    session.SaveChanges();
                }

                await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

                Assert.True(WaitForValue(() =>
                {
                    using (var session = store.OpenSession(ShardHelper.ToShardName(store.Database, wrongShard)))
                    {
                        var u1 = session.Load<User>("users/2$users/1");
                        if (u1 != null)
                            return false;

                        var u2 = session.Load<User>("users/3$users/1");
                        if (u2 != null)
                            return false;

                        return true;
                    }
                }, true, 30_000, 333));
            }
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_MultipleWrongBuckets()
        {
            Server.ServerStore.Sharding.BlockPrefixedSharding = false;
            using var store = Sharding.GetDocumentStore(new Options
            {
                ModifyDatabaseRecord = record =>
                {
                    record.Sharding ??= new ShardingConfiguration();
                    record.Sharding.Prefixed = new List<PrefixedShardingSetting>
                    {
                        new PrefixedShardingSetting
                        {
                            Prefix = "users/",
                            Shards = new List<int> { 0 }
                        },
                        new PrefixedShardingSetting
                        {
                            Prefix = "orders/",
                            Shards = new List<int> { 2 }
                        }
                    };
                    record.Settings[RavenConfiguration.GetKey(x => x.Sharding.PeriodicDocumentsMigrationInterval)] = 1.ToString();
                }
            });

            using (var session = store.OpenSession())
            {
                session.Store(new User(), "users/1");
                session.SaveChanges();
            }

            var shard = await Sharding.GetShardNumberFor(store, "users/1");
            Assert.Equal(0, shard);

            using (var session = store.OpenSession())
            {
                session.Store(new Order(), "orders/1");
                session.SaveChanges();
            }

            shard = await Sharding.GetShardNumberFor(store, "orders/1");
            Assert.Equal(2, shard);

            var wrongShard = 1;
            var db = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(store.Database, wrongShard));
            db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

            using (var session = store.OpenSession(ShardHelper.ToShardName(store.Database, wrongShard)))
            {
                session.Store(new User(), "users/2");
                session.Store(new Order(), "orders/2");
                session.SaveChanges();
            }

            await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

            Assert.True(WaitForValue(() =>
            {
                using (var session = store.OpenSession(ShardHelper.ToShardName(store.Database, wrongShard)))
                {
                    var u2 = session.Load<User>("users/2");
                    if (u2 != null)
                        return false;

                    var o2 = session.Load<Order>("orders/2");
                    if (o2 != null)
                        return false;

                    return true;
                }
            }, true, 30_000, 333));
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_ClusterCase()
        {
            var dbName = GetDatabaseName();
            var (nodes, leader) = await CreateRaftCluster(3);
            var topology = await ShardingCluster.CreateShardedDatabaseInCluster(dbName, 1, (nodes, leader), shards: 3);

            using (var store = new DocumentStore
            {
                Urls = topology.Servers.Select(s => s.WebUrl).ToArray(),
                Database = dbName,
            }.Initialize())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }

                var shard = await Sharding.GetShardNumberFor(store, "users/1");
                var wrongShard = (shard + 1) % 2;

                var record = await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(dbName));
                var dbTopology = record.Sharding.Shards[wrongShard];
                var serverTag = dbTopology.Members[0];
                var server = nodes.Single(n => n.ServerStore.NodeTag == serverTag);

                var db = await server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(store.Database, wrongShard));
                db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

                using (var session = store.OpenSession(ShardHelper.ToShardName(store.Database, wrongShard)))
                {
                    session.Store(new User(), "users/2$users/1");
                    session.Store(new User(), "users/3$users/1");
                    session.SaveChanges();
                }

                await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

                Assert.True(WaitForValue(() =>
                {
                    using (var session = store.OpenSession(ShardHelper.ToShardName(store.Database, wrongShard)))
                    {
                        var u1 = session.Load<User>("users/2$users/1");
                        if (u1 != null)
                            return false;

                        var u2 = session.Load<User>("users/3$users/1");
                        if (u2 != null)
                            return false;

                        return true;
                    }
                }, true, 30_000, 333));
            }
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_SubscriptionsCase()
        {
            using var store = Sharding.GetDocumentStore();
            using (var session = store.OpenSession())
            {
                session.Store(new User(), "users/1-A");
                session.SaveChanges();
            }

            var shard = await Sharding.GetShardNumberFor(store, "users/1");
            var wrongShard = (shard + 1) % 2;

            var db = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(store.Database, wrongShard));
            db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

            using (var session = store.OpenSession(ShardHelper.ToShardName(store.Database, wrongShard)))
            {
                session.Store(new User(), "users/2$users/1");
                session.SaveChanges();
            }

            await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

            var id = await store.Subscriptions.CreateAsync<User>();
            var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var subscription = store.Subscriptions.GetSubscriptionWorker<User>(new SubscriptionWorkerOptions(id)
            {
                MaxDocsPerBatch = 5,
                TimeToWaitBeforeConnectionRetry = TimeSpan.FromMilliseconds(250)
            }))
            {
                var t = subscription.Run(batch =>
                {
                    foreach (var item in batch.Items)
                    {
                        if (users.Add(item.Id) == false)
                        {
                            throw new SubscriberErrorException($"Got exact same {item.Id} twice");
                        }
                    }
                });


                try
                {
                    await t.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.True(false, "Worker completed without exception");
                }
                catch (TimeoutException)
                {
                    // expected, means the worker is still alive  
                }

                await Sharding.Subscriptions.AssertNoItemsInTheResendQueueAsync(store, id);
            }
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_ExternalReplicationShardedAndNonSharded()
        {
            using (var source = Sharding.GetDocumentStore())
            using (var dest = GetDocumentStore())
            {
                await SetupReplicationAsync(source, dest);
                await EnsureReplicatingAsync(source, dest);

                await SetupReplicationAsync(dest, source);
                await EnsureReplicatingAsync(dest, source);

                using (var session = source.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }

                var shard = await Sharding.GetShardNumberFor(source, "users/1");
                var wrongShard = (shard + 1) % 2;

                var db = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(source.Database, wrongShard));
                db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

                using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                {
                    session.Store(new User(), "users/2$users/1");
                    session.Store(new User(), "users/3$users/1");
                    session.SaveChanges();
                }

                await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

                Assert.True(WaitForValue(() =>
                {
                    using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                    {
                        var u1 = session.Load<User>("users/2$users/1");
                        if (u1 != null)
                            return false;

                        var u2 = session.Load<User>("users/3$users/1");
                        if (u2 != null)
                            return false;

                        return true;
                    }
                }, true, 30_000, 333));
            }
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_ExternalReplicationFromShardedToSharded()
        {
            using (var source = Sharding.GetDocumentStore())
            using (var dest = Sharding.GetDocumentStore())
            {
                await SetupReplicationAsync(source, dest);
                await EnsureReplicatingAsync(source, dest);

                await SetupReplicationAsync(dest, source);
                await EnsureReplicatingAsync(dest, source);

                using (var session = source.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }

                var shard = await Sharding.GetShardNumberFor(source, "users/1");
                var wrongShard = (shard + 1) % 2;

                var db = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(source.Database, wrongShard));
                db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

                using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                {
                    session.Store(new User(), "users/2$users/1");
                    session.Store(new User(), "users/3$users/1");
                    session.SaveChanges();
                }

                await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

                Assert.True(WaitForValue(() =>
                {
                    using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                    {
                        var u1 = session.Load<User>("users/2$users/1");
                        if (u1 != null)
                            return false;

                        var u2 = session.Load<User>("users/3$users/1");
                        if (u2 != null)
                            return false;

                        return true;
                    }
                }, true, 30_000, 333));
            }
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_ExternalReplicationFromShardedToSharded2()
        {
            using (var source = Sharding.GetDocumentStore())
            using (var dest = Sharding.GetDocumentStore())
            {
                using (var session = source.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }

                var shard = await Sharding.GetShardNumberFor(source, "users/1");
                var wrongShard = (shard + 1) % 2;

                var db = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(source.Database, wrongShard));
                db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

                using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                {
                    session.Store(new User(), "users/2$users/1");
                    session.Store(new User(), "users/3$users/1");
                    session.SaveChanges();
                }

                await SetupReplicationAsync(source, dest);
                await EnsureReplicatingAsync(source, dest);

                await SetupReplicationAsync(dest, source);
                await EnsureReplicatingAsync(dest, source);

                await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

                Assert.True(WaitForValue(() =>
                {
                    using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                    {
                        var u1 = session.Load<User>("users/2$users/1");
                        if (u1 != null)
                            return false;

                        var u2 = session.Load<User>("users/3$users/1");
                        if (u2 != null)
                            return false;

                        return true;
                    }
                }, true, 30_000, 333));
            }
        }

        [Fact]
        public async Task PeriodicDocumentsMigratorShouldWork_ExternalReplicationFromShardedToShardedWithConflict()
        {
            using (var source = Sharding.GetDocumentStore())
            using (var dest = Sharding.GetDocumentStore())
            {
                using (var session = source.OpenSession())
                {
                    session.Store(new User(), "users/1");
                    session.SaveChanges();
                }

                var shard = await Sharding.GetShardNumberFor(source, "users/1");
                var wrongShard = (shard + 1) % 2;

                var db = await Server.ServerStore.DatabasesLandlord.TryGetOrCreateShardedResourceStore(ShardHelper.ToShardName(source.Database, wrongShard));
                db.ForTestingPurposesOnly().EnableWritesToTheWrongShard = true;

                using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                {
                    session.Store(new User(), "users/2$users/1");
                    session.Store(new User(), "users/3$users/1");
                    session.SaveChanges();
                }

                await SetupReplicationAsync(source, dest);
                await EnsureReplicatingAsync(source, dest);

                await SetupReplicationAsync(dest, source);
                await EnsureReplicatingAsync(dest, source);

                var shard2 = await Sharding.GetShardNumberFor(source, "users/1");
                var b1 = await BreakReplication(Server.ServerStore, ShardHelper.ToShardName(source.Database, wrongShard));
                var b2 = await BreakReplication(Server.ServerStore, ShardHelper.ToShardName(dest.Database, shard2));

                using (var session = dest.OpenSession())
                {
                    var u1 = session.Load<User>("users/2$users/1");
                    u1.Age = 29;
                    session.SaveChanges();
                }

                using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                {
                    var u1 = session.Load<User>("users/2$users/1");
                    u1.LastName = "Smelly Cat";
                    session.SaveChanges();
                }

                b1.Mend();
                b2.Mend();

                await db.PeriodicDocumentsMigrator.ExecuteMoveDocuments();

                Assert.True(WaitForValue(() =>
                {
                    using (var session = source.OpenSession(ShardHelper.ToShardName(source.Database, wrongShard)))
                    {
                        var u1 = session.Load<User>("users/2$users/1");
                        if (u1 != null)
                            return false;

                        var u2 = session.Load<User>("users/3$users/1");
                        if (u2 != null)
                            return false;

                        return true;
                    }
                }, true, 30_000, 333));
            }
        }
    }
}
