﻿using System.Linq;
using FastTests;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Tests.Core.Utils.Entities;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_8847 : RavenTestBase
    {
        [Fact]
        public void Should_set_first_batch_timeout_of_newly_created_auto_index()
        {
            using (var store = GetDocumentStore())
            {
                var database = GetDatabase(store.Database).Result;

                database.IndexStore.StopIndexing();

                Raven.Server.Documents.Indexes.Index index;

                using (var session = store.OpenSession())
                {
                    session.Query<User>().Statistics(out var stats).Where(x => x.Name != "joe").ToList();

                    index = database.IndexStore.GetIndex(stats.IndexName);

                    Assert.True(index._firstBatchTimeout.HasValue);
                }

                database.IndexStore.StartIndexing();

                using (var session = store.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "B"
                    });

                    session.SaveChanges();

                    session.Query<User>().Customize(x => x.WaitForNonStaleResults()).Where(x => x.Name != "ema").ToList();

                    Assert.False(index._firstBatchTimeout.HasValue);
                }
            }
        }

        [Fact]
        public void Should_set_first_batch_timeout_of_newly_created_static_index()
        {
            using (var store = GetDocumentStore())
            {
                var database = GetDatabase(store.Database).Result;

                var usersByname = "users/byname";

                store.Admin.Send(new PutIndexesOperation(new IndexDefinition()
                {
                    Name = usersByname,
                    Maps =
                    {
                        "from user in docs.Users select new { user.Name }"
                    }
                }));

                database.IndexStore.StopIndexing();

                var index = database.IndexStore.GetIndex(usersByname);

                using (var session = store.OpenSession())
                {
                    session.Query<User>(usersByname).Customize(x => x.WaitForNonStaleResults()).ToList();

                    Assert.True(index._firstBatchTimeout.HasValue);
                }

                database.IndexStore.StartIndexing();

                using (var session = store.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "B"
                    });

                    session.SaveChanges();

                    session.Query<User>(usersByname).Customize(x => x.WaitForNonStaleResults()).Where(x => x.Name != "ema").ToList();

                    Assert.False(index._firstBatchTimeout.HasValue);
                }
            }
        }
    }
}
