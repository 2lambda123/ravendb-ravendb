﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.ETL;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide.Operations.ConnectionStrings;
using Raven.Client.ServerWide.Operations.ETL;
using Raven.Tests.Core.Utils.Entities;
using Tests.Infrastructure;
using Xunit;

namespace RachisTests.DatabaseCluster
{
    public class EtlFailover : ReplicationTests
    {
        [NightlyBuildFact]
        public async Task ReplicateFromSingleSource()
        {
            var srcDb = "ReplicateFromSingleSourceSrc";
            var dstDb = "ReplicateFromSingleSourceDst";
            var srcRaft = await CreateRaftClusterAndGetLeader(3);
            var dstRaft = await CreateRaftClusterAndGetLeader(1);
            var srcNodes = await CreateDatabaseInCluster(srcDb, 3, srcRaft.WebUrl);
            var destNode = await CreateDatabaseInCluster(dstDb, 1, dstRaft.WebUrl);

            using (var src = new DocumentStore
            {
                Urls = srcNodes.Servers.Select(s=>s.WebUrl).ToArray(),
                Database = srcDb,
            }.Initialize())
            using (var dest = new DocumentStore
            {
                Urls = new []{destNode.Servers[0].WebUrl},
                Database = dstDb,
            }.Initialize())
            {
                var connectionStringName = "EtlFailover";
                var urls = new[] { destNode.Servers[0].WebUrl };
                var config = new RavenEtlConfiguration()
                {
                    Name = connectionStringName,
                    ConnectionStringName = connectionStringName,
                    Transforms =
                    {
                        new Transformation
                        {
                            Name = $"ETL : {connectionStringName}",
                            Collections = new List<string>(new[] {"Users"}),
                            Script = null,
                            ApplyToAllDocuments = false,
                            Disabled = false
                        }
                    },
                    LoadRequestTimeoutInSec = 10,
                    MentorNode = "B"
                };
                var connectionString = new RavenConnectionString
                {
                    Name = connectionStringName,
                    Database = dest.Database,
                    TopologyDiscoveryUrls = urls,
                };

                src.Maintenance.Server.Send(new PutConnectionStringOperation<RavenConnectionString>(connectionString, src.Database));
                src.Maintenance.Server.Send(new AddEtlOperation<RavenConnectionString>(config, src.Database));
                var originalTaskNode = srcNodes.Servers.Single(s => s.ServerStore.NodeTag == "B");
                
                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    },"users/1");

                    session.SaveChanges();
                }
                Assert.True(WaitForDocument<User>(dest, "users/1", u => u.Name == "Joe Doe", 30_000));
                await srcRaft.ServerStore.RemoveFromClusterAsync("B");
                await originalTaskNode.ServerStore.WaitForState(RachisState.Passive);

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe2"
                    }, "users/2");

                    session.SaveChanges();
                }
                Assert.True(WaitForDocument<User>(dest, "users/2", u => u.Name == "Joe Doe2", 30_000));

                using (var originalSrc = new DocumentStore
                {
                    Urls = new[] {originalTaskNode.WebUrl},
                    Database = srcDb,
                }.Initialize())
                {
                    using (var session = originalSrc.OpenSession())
                    {
                        session.Store(new User()
                        {
                            Name = "Joe Doe3"
                        }, "users/3");

                        session.SaveChanges();
                    }

                    Assert.False(WaitForDocument<User>(dest, "users/3", u => u.Name == "Joe Doe3", 60_000));
                }
            }
        }

        [NightlyBuildFact]
        public async Task EtlDestinationFailoverBetweenNodesWithinSameCluster()
        {
            var srcDb = "EtlDestinationFailoverBetweenNodesWithinSameClusterSrc";
            var dstDb = "EtlDestinationFailoverBetweenNodesWithinSameClusterDst";
            var srcRaft = await CreateRaftClusterAndGetLeader(3);
            var dstRaft = await CreateRaftClusterAndGetLeader(3);
            var srcNodes = await CreateDatabaseInCluster(srcDb, 3, srcRaft.WebUrl);
            var destNode = await CreateDatabaseInCluster(dstDb, 3, dstRaft.WebUrl);

            using (var src = new DocumentStore
            {
                Urls = srcNodes.Servers.Select(s => s.WebUrl).ToArray(),
                Database = srcDb,
            }.Initialize())
            using (var dest = new DocumentStore
            {
                Urls = destNode.Servers.Select(s => s.WebUrl).ToArray(),
                Database = dstDb,
            }.Initialize())
            {
                var connectionStringName = "EtlFailover";
                var urls = new[] { "http://google.com", "http://localhost:1232", destNode.Servers[0].WebUrl };
                var conflig = new RavenEtlConfiguration()
                {
                    Name = connectionStringName,
                    ConnectionStringName = connectionStringName,
                    Transforms =
                    {
                        new Transformation
                        {
                            Name = $"ETL : {connectionStringName}",
                            Collections = new List<string>(new[] { "Users" }),
                            Script = null,
                            ApplyToAllDocuments = false,
                            Disabled = false
                        }
                    },
                    LoadRequestTimeoutInSec = 10,
                    MentorNode = "A"
                };
                var connectionString = new RavenConnectionString
                {
                    Name = connectionStringName,
                    Database = dest.Database,
                    TopologyDiscoveryUrls = urls,
                };

                src.Maintenance.Server.Send(new PutConnectionStringOperation<RavenConnectionString>(connectionString, src.Database));
                var etlResult = src.Maintenance.Server.Send(new AddEtlOperation<RavenConnectionString>(conflig, src.Database));
                var database = await srcNodes.Servers.Single(s => s.ServerStore.NodeTag == "A")
                    .ServerStore.DatabasesLandlord.TryGetOrCreateResourceStore(srcDb);

                var etlDone = new ManualResetEventSlim();
                database.EtlLoader.BatchCompleted += (n, s) =>
                {
                    if (s.LoadSuccesses > 0)
                        etlDone.Set();
                };

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    }, "users/1");
                    session.SaveChanges();
                }

                Assert.True(etlDone.Wait(TimeSpan.FromMinutes(1)));

                Assert.True(WaitForDocument<User>(dest, "users/1", u => u.Name == "Joe Doe", 30_000));
                var taskInfo = (OngoingTaskRavenEtlDetails)src.Maintenance.Server.Send(new GetOngoingTaskInfoOperation(src.Database, etlResult.TaskId, OngoingTaskType.RavenEtl));

                Assert.NotNull(taskInfo.DestinationUrl);
                etlDone.Reset();
                DisposeServerAndWaitForFinishOfDisposal(destNode.Servers.Single(s => s.WebUrl == taskInfo.DestinationUrl));

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe2"
                    }, "users/2");

                    session.SaveChanges();
                }

                Assert.True(etlDone.Wait(TimeSpan.FromMinutes(1)));
                Assert.True(WaitForDocument<User>(dest, "users/2", u => u.Name == "Joe Doe2", 60_000));
            }
        }

        [NightlyBuildFact(Skip = "RavenDB-9742")]
        public async Task EtlDestinationFailoverBetweenNodesInDifferentClusters()
        {
            var srcDb = "EtlFailoverBetweenNodesSrc";
            var dstDb = "EtlFailoverBetweenNodesDst";
            var srcRaft = await CreateRaftClusterAndGetLeader(3);
            var dstRaft1 = await CreateRaftClusterAndGetLeader(1);
            var dstRaft2 = await CreateRaftClusterAndGetLeader(1);
            var srcNodes = await CreateDatabaseInCluster(srcDb, 3, srcRaft.WebUrl);
            var destNode1 = await CreateDatabaseInCluster(dstDb, 1, dstRaft1.WebUrl);
            var destNode2 = await CreateDatabaseInCluster(dstDb, 1, dstRaft2.WebUrl);

            using (var src = new DocumentStore
            {
                Urls = srcNodes.Servers.Select(s => s.WebUrl).ToArray(),
                Database = srcDb,
            }.Initialize())
            using (var dest1 = new DocumentStore
            {
                Urls = destNode1.Servers.Select(s => s.WebUrl).ToArray(),
                Database = dstDb,
            }.Initialize())
            using (var dest2 = new DocumentStore
            {
                Urls = destNode2.Servers.Select(s => s.WebUrl).ToArray(),
                Database = dstDb,
            }.Initialize())
            {
                var connectionStringName = "EtlFailover";
                var urls = new[] { destNode1.Servers[0].WebUrl, destNode2.Servers[0].WebUrl };
                var conflig = new RavenEtlConfiguration()
                {
                    Name = connectionStringName,
                    ConnectionStringName = connectionStringName,
                    Transforms =
                    {
                        new Transformation
                        {
                            Name = $"ETL : {connectionStringName}",
                            Collections = new List<string>(new[] { "Users" }),
                            Script = null,
                            ApplyToAllDocuments = false,
                            Disabled = false
                        }
                    },
                    LoadRequestTimeoutInSec = 10,
                    MentorNode = "A"
                };
                var connectionString = new RavenConnectionString
                {
                    Name = connectionStringName,
                    Database = dest1.Database,
                    TopologyDiscoveryUrls = urls,
                };

                src.Maintenance.Server.Send(new PutConnectionStringOperation<RavenConnectionString>(connectionString, src.Database));
                var etlResult = src.Maintenance.Server.Send(new AddEtlOperation<RavenConnectionString>(conflig, src.Database));
                var database = await srcNodes.Servers.Single(s => s.ServerStore.NodeTag == "A")
                    .ServerStore.DatabasesLandlord.TryGetOrCreateResourceStore(srcDb);

                var etlDone = new ManualResetEventSlim();
                database.EtlLoader.BatchCompleted += (n, s) =>
                {
                    if (s.LoadSuccesses > 0)
                        etlDone.Set();
                };

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe"
                    },"users/1");

                    session.SaveChanges();
                }

                Assert.True(etlDone.Wait(TimeSpan.FromMinutes(1)));
                Assert.True(WaitForDocument<User>(dest1, "users/1", u => u.Name == "Joe Doe", 30_000));
                var taskInfo = (OngoingTaskRavenEtlDetails)src.Maintenance.Server.Send(new GetOngoingTaskInfoOperation(src.Database, etlResult.TaskId, OngoingTaskType.RavenEtl));

                Assert.NotNull(taskInfo.DestinationUrl);
                etlDone.Reset();
                DisposeServerAndWaitForFinishOfDisposal(destNode1.Servers.Single(s => s.WebUrl == taskInfo.DestinationUrl));

                using (var session = src.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "Joe Doe2"
                    }, "users/2");

                    session.SaveChanges();
                }

                Assert.True(etlDone.Wait(TimeSpan.FromMinutes(1)));
                Assert.True(WaitForDocument<User>(dest2, "users/2", u => u.Name == "Joe Doe2", 60_000));
            }
        }

    }
}
