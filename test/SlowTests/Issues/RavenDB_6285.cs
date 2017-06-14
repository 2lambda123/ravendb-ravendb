﻿using System;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using Raven.Tests.Core.Utils.Entities;
using SlowTests.Server.Documents.Notifications;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_6285 : RavenTestBase
    {
        [Fact]
        public async Task CanGetAllNotificationAboutDocument_ALotOfDocuments()
        {
            using (var x = new ChangesTests())
            {
                await x.CanGetAllNotificationAboutDocument_ALotOfDocuments();
            }
        }

        [Fact]
        public async Task BasicChangesApi()
        {
            using (var store = GetDocumentStore())
            {
                var mre = new ManualResetEventSlim();

                var changes = await store.Changes().EnsureConnectedNow();
                var observable = changes.ForAllDocuments();
                observable.Subscribe(x => mre.Set());
                await observable.EnsureSubscribedNow();

                using (var session = store.OpenSession())
                {
                    session.Store(new Person
                    {
                        Name = "John"
                    });

                    session.SaveChanges();
                }

                Assert.True(mre.Wait(TimeSpan.FromSeconds(45)));
            }
        }
    }
}