﻿using System;
using System.Threading.Tasks;
using FastTests.Server.Replication;
using Raven.Server.ServerWide.Context;
using Tests.Infrastructure;
using Xunit;

namespace SlowTests.Server.Replication
{
    public class ReplicationWriteAssurance : ClusterTestBase 
    {
        [Fact]
        public async Task ServerSideWriteAssurance()
        {
            
            var leader = await CreateRaftClusterAndGetLeader(3);
            using (var store = GetDocumentStore(new Options
            {
                Server = leader,
                ReplicationFactor = 3,
                ModifyDatabaseRecord = record =>
                {
                    record.Topology.DynamicNodesDistribution = false;
                }
            }))
            {
                using (var s1 = store.OpenSession())
                {
                    s1.Advanced.WaitForReplicationAfterSaveChanges(replicas: 2, timeout: TimeSpan.FromSeconds(30));

                    s1.Store(new
                    {
                        Name = "Idan"
                    }, "users/1");

                    s1.SaveChanges();
                }

                foreach (var server in Servers)
                {
                    var db = await server.ServerStore.DatabasesLandlord.TryGetOrCreateResourceStore(store.Database);
                    using(db.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                    {
                        context.OpenReadTransaction();
                        Assert.NotNull(db.DocumentsStorage.Get(context, "users/1"));
                    }
                }
            }
        }
    }
}
