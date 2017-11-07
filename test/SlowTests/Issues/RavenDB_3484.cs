﻿using System;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Documents.Subscriptions;
using SlowTests.Core.Utils.Entities;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_3484 : RavenTestBase
    {
        private readonly TimeSpan waitForDocTimeout = TimeSpan.FromSeconds(20);

        [Fact]
        public void AllClientsWith_WaitForFree_StrategyShouldGetAccessToSubscription()
        {
            using (var store = GetDocumentStore())
            {
                var id = store.Subscriptions.Create<User>();

                const int numberOfClients = 4;

                var subscriptions = new SubscriptionWorker<User>[numberOfClients];
                var processed = new ManualResetEvent[numberOfClients];
                var done = new bool[numberOfClients];
                for (int i = 0; i < numberOfClients; i++)
                {
                    processed[i] = new ManualResetEvent(false);
                }
                var userShouldnotexist = "user/shouldNotExist";
                using (var session = store.OpenSession())
                {
                    session.Store(new User(),userShouldnotexist);
                    session.SaveChanges();
                    //session.Delete(userShouldnotexist);
                }

                for (int i = 0; i < numberOfClients; i++)
                {
                    var clientNumber = i;

                    subscriptions[clientNumber] = store.Subscriptions.GetSubscriptionWorker<User>(new SubscriptionWorkerOptions(id)
                    {
                        Strategy = SubscriptionOpeningStrategy.WaitForFree
                    });

                    subscriptions[clientNumber].AfterAcknowledgment += x =>
                    {
                        processed[clientNumber].Set();
                        return Task.CompletedTask;
                    };

                    subscriptions[clientNumber].Run(x =>
                    {
                    });

                    Thread.Sleep(200);
                }

                for (int i = 0; i < numberOfClients; i++)
                {
                    using (var s = store.OpenSession())
                    {
                        s.Store(new User());
                        s.SaveChanges();
                    }

                    var index = WaitHandle.WaitAny(processed, waitForDocTimeout);
                    
                    Assert.NotEqual(WaitHandle.WaitTimeout, index);

                    subscriptions[index].Dispose();

                    done[index] = true;

                    processed[index].Reset();
                }

                for (int i = 0; i < numberOfClients; i++)
                {
                    Assert.True(done[i]);
                }
            }
        }
    }
}
