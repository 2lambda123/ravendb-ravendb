﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using FastTests.Client;
using Orders;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;
using Xunit;
using Xunit.Abstractions;


namespace SlowTests.Issues
{
    public class RavenDB_17624 : RavenTestBase
    {
        public RavenDB_17624(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task ForbidOpeningMoreThenOneSessionPerSubscriptionBatch()
        {
            using var store = GetDocumentStore();
            using (var session = store.OpenAsyncSession())
            {
                await session.StoreAsync(new Command { Value = 1 });
                await session.StoreAsync(new Command { Value = 2 });
                await session.SaveChangesAsync();
            }

            try
            {
                await store.Subscriptions
                    .GetSubscriptionStateAsync("BackgroundSubscriptionWorker");
            }
            catch (SubscriptionDoesNotExistException)
            {
                await store.Subscriptions
                    .CreateAsync(new SubscriptionCreationOptions<Command>
                    {
                        Name = "BackgroundSubscriptionWorker",
                        Filter = x => x.ProcessedOn == null
                    });
            }

            var workerOptions = new SubscriptionWorkerOptions("BackgroundSubscriptionWorker");

            using var worker = store.Subscriptions
               .GetSubscriptionWorker<Command>(workerOptions);

            worker.OnSubscriptionConnectionRetry += exception => Console.WriteLine(exception);
            worker.OnUnexpectedSubscriptionError += exception => Console.WriteLine(exception);

            var mre = new ManualResetEvent(false);
            var cts = new CancellationTokenSource();
            var last = DateTime.UtcNow;
            InvalidOperationException exception = null;
            var t = worker.Run(async batch =>
            {
                using (var session = batch.OpenAsyncSession())
                {
                }

                exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                {
                    using var session = batch.OpenAsyncSession();
                });

                mre.Set();
            }, cts.Token);

            Assert.True(mre.WaitOne(TimeSpan.FromSeconds(15)));
            Assert.NotNull(exception);
            Assert.Equal("Session can only be opened once per each Subscription batch", exception.Message);
        }

        private class Command
        {
            public string Id { get; set; }

            public DateTime? ProcessedOn { get; set; }

            public int Value { get; set; }
        }
    }
}
