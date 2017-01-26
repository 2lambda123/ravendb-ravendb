﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using FastTests.Server.Basic.Entities;
using FastTests.Server.Documents.Notifications;
using Raven.Abstractions.Data;
using Raven.Json.Linq;
using Xunit;

namespace SlowTests.Core.Subscriptions
{
    public class RavenDB_3193 : RavenTestBase
    {
        [Fact]
        public async Task ShouldRespectCollectionCriteria()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession())
                {
                    for (int i = 0; i < 100; i++)
                    {
                        await session.StoreAsync(new Company());
                        await session.StoreAsync(new User());
                        await session.StoreAsync(new Address());
                    }

                    await session.SaveChangesAsync();
                }

                var id = await store.AsyncSubscriptions.CreateAsync(new SubscriptionCriteria
                {
                    Collection = "Users"                    
                });

                using (var subscription = store.AsyncSubscriptions.Open(new SubscriptionConnectionOptions
                {
                    MaxDocsPerBatch = 31,
                    SubscriptionId = id
                }))
                {
                    var docs = new List<RavenJObject>();

                    subscription.Subscribe(docs.Add);

                    await subscription.StartAsync();

                    SpinWait.SpinUntil(() => docs.Count >= 100, TimeSpan.FromSeconds(60));
                    Assert.Equal(100, docs.Count);


                    foreach (var jsonDocument in docs)
                    {
                        var collection =
                            jsonDocument[Constants.Metadata.Key].Value<string>(Constants.Metadata.Collection);
                        Assert.True(collection == "Users");
                    }
                }
            }
        }
    }
}
