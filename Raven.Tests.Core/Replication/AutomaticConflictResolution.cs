// -----------------------------------------------------------------------
//  <copyright file="AutomaticConflictResolution.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.IO;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Abstractions.Extensions;
using Raven.Abstractions.Replication;
using Raven.Json.Linq;
using Raven.Tests.Core.Utils.Entities;
using Xunit;

namespace Raven.Tests.Core.Replication
{
    public class AutomaticConflictResolution : RavenReplicationCoreTest
    {
        [Fact]
        public void ShouldResolveDocumentConflictInFavorOfLocalVersion()
        {
            DocumentConflictResolveTest(StraightforwardConflictResolution.ResolveToLocal);
        }

        [Fact]
        public void ShouldResolveDocumentConflictInFavorOfRemoteVersion()
        {
            DocumentConflictResolveTest(StraightforwardConflictResolution.ResolveToRemote);
        }

        [Fact]
        public void ShouldResolveDocumentConflictInFavorOfLatestVersion()
        {
            using (var master = GetDocumentStore())
            using (var slave = GetDocumentStore())
            {
                SetupReplication(master, destinations: slave);

                using (var session = slave.OpenSession())
                {
                    session.Store(new ReplicationConfig()
                    {
                        DocumentConflictResolution = StraightforwardConflictResolution.ResolveToLatest
                    }, Constants.RavenReplicationConfig);

                    session.Store(new User()
                    {
                        Name = "1st"
                    }, "users/1");

                    session.SaveChanges();
                }

                using (var session = master.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "1st"
                    }, "users/2");

                    session.SaveChanges();
                }

                Thread.Sleep(2000);

                using (var session = slave.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "2nd"
                    }, "users/2");

                    session.SaveChanges();
                }

                using (var session = master.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "2nd"
                    }, "users/1");

                    session.Store(new
                    {
                        Foo = "marker"
                    }, "marker");

                    session.SaveChanges();
                }

                var marker = WaitForDocument(slave, "marker");

                Assert.NotNull(marker);

                using (var session = slave.OpenSession())
                {
                    User user1 = null;
                    User user2 = null;

                    Assert.DoesNotThrow(() => { user1 = session.Load<User>("users/1"); });
                    Assert.DoesNotThrow(() => { user2 = session.Load<User>("users/2"); });

                    Assert.Equal("2nd", user1.Name);
                    Assert.Equal("2nd", user2.Name);
                }
            }
        }

        private void DocumentConflictResolveTest(StraightforwardConflictResolution docConflictResolution)
        {
            using (var master = GetDocumentStore())
            using (var slave = GetDocumentStore())
            {
                SetupReplication(master, destinations: slave);

                using (var session = slave.OpenSession())
                {
                    session.Store(new ReplicationConfig()
                    {
                        DocumentConflictResolution = docConflictResolution
                    }, Constants.RavenReplicationConfig);

                    session.Store(new User()
                    {
                        Name = "local"
                    }, "users/1");

                    session.SaveChanges();
                }

                using (var session = master.OpenSession())
                {
                    session.Store(new User()
                    {
                        Name = "remote"
                    }, "users/1");

                    session.Store(new
                    {
                        Foo = "marker"
                    }, "marker");

                    session.SaveChanges();
                }

                var marker = WaitForDocument(slave, "marker");

                Assert.NotNull(marker);

                using (var session = slave.OpenSession())
                {
                    User item = null;

                    Assert.DoesNotThrow(() => { item = session.Load<User>("users/1"); });

                    switch (docConflictResolution)
                    {
                        case StraightforwardConflictResolution.ResolveToLocal:
                            Assert.Equal("local", item.Name);
                            break;
                        case StraightforwardConflictResolution.ResolveToRemote:
                            Assert.Equal("remote", item.Name);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException("docConflictResolution");
                    }
                }
            }
        }
    }
}
