﻿using System.Threading.Tasks;
using FastTests.Server.Replication;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Sparrow.Json.Parsing;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_17128 : ReplicationTestBase
    {
        public RavenDB_17128(ITestOutputHelper output) : base(output)
        {
        }


        [Fact]
        public async Task ConcurrencyExceptionShouldIncludeConflictsInfo_CompareExchangeClusterWide()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Advanced.ClusterTransaction
                        .CreateCompareExchangeValue("usernames/ravendb", new object());

                    session.Advanced.ClusterTransaction
                        .CreateCompareExchangeValue("emails/info@ravendb.net", new object());

                    await session.SaveChangesAsync();
                }

                using (var session = store.OpenAsyncSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Advanced.ClusterTransaction
                        .CreateCompareExchangeValue("usernames/ravendb", new object());

                    session.Advanced.ClusterTransaction
                        .CreateCompareExchangeValue("emails/info@ravendb.net", new object());

                    var ex = await Assert.ThrowsAsync<ConcurrencyException>(async () => await session.SaveChangesAsync());
                    
                    Assert.NotNull(ex.ClusterTransactionConflicts);
                    Assert.Equal(2, ex.ClusterTransactionConflicts.Length);

                    Assert.Equal(ConcurrencyException.ConflictType.CompareExchange, ex.ClusterTransactionConflicts[0].Type);
                    Assert.Equal("usernames/ravendb", ex.ClusterTransactionConflicts[0].Id);
                    Assert.NotNull(ex.ClusterTransactionConflicts[0].Expected);
                    Assert.NotNull(ex.ClusterTransactionConflicts[0].Actual);

                    Assert.Equal(ConcurrencyException.ConflictType.CompareExchange, ex.ClusterTransactionConflicts[1].Type);
                    Assert.Equal("emails/info@ravendb.net", ex.ClusterTransactionConflicts[1].Id);
                    Assert.NotNull(ex.ClusterTransactionConflicts[1].Expected);
                    Assert.NotNull(ex.ClusterTransactionConflicts[1].Actual);
                }
            }
        }

        [Fact]
        public void ConcurrencyExceptionShouldIncludeConflictsInfo_DocumentsClusterWide()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Store(new Document { Value = 1 }, "objects/1");
                    session.Store(new Document { Value = 2 }, "objects/2");

                    session.SaveChanges();
                }

                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                using (var session2 = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    var o1 = session.Load<Document>("objects/1");
                    var o2 = session.Load<Document>("objects/2");

                    var o12 = session2.Load<Document>("objects/1");
                    var o22 = session2.Load<Document>("objects/2");

                    o12.Value = 12;
                    o22.Value = 22;

                    session2.SaveChanges();

                    o1.Value = 13;
                    o2.Value = 23;

                    var ex = Assert.Throws<ConcurrencyException>(() => session.SaveChanges());
                    Assert.NotNull(ex.ClusterTransactionConflicts);
                    Assert.Equal(2, ex.ClusterTransactionConflicts.Length);

                    Assert.Equal(ConcurrencyException.ConflictType.Document, ex.ClusterTransactionConflicts[0].Type);
                    Assert.Equal("rvn-atomic/objects/1", ex.ClusterTransactionConflicts[0].Id);
                    Assert.NotNull(ex.ClusterTransactionConflicts[0].Expected);
                    Assert.NotNull(ex.ClusterTransactionConflicts[0].Actual);

                    Assert.Equal(ConcurrencyException.ConflictType.Document, ex.ClusterTransactionConflicts[1].Type);
                    Assert.Equal("rvn-atomic/objects/2", ex.ClusterTransactionConflicts[1].Id);
                    Assert.NotNull(ex.ClusterTransactionConflicts[1].Expected);
                    Assert.NotNull(ex.ClusterTransactionConflicts[1].Actual);
                }
            }
        }

        [Fact]
        public void ConcurrencyExceptionShouldIncludeConflictsInfo_DocumentsSingleNode()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Store(new Document { Value = 1 }, "objects/1");
                    session.Store(new Document { Value = 2 }, "objects/2");

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                using (var session2 = store.OpenSession())
                {
                    session.Advanced.UseOptimisticConcurrency = true;
                    session2.Advanced.UseOptimisticConcurrency = true;

                    var o1 = session.Load<Document>("objects/1");
                    var o2 = session.Load<Document>("objects/2");

                    var o12 = session2.Load<Document>("objects/1");
                    var o22 = session2.Load<Document>("objects/2");

                    o12.Value = 12;
                    o22.Value = 22;

                    session2.SaveChanges();

                    o1.Value = 13;
                    o2.Value = 23;

                    var ex = Assert.Throws<ConcurrencyException>(() => session.SaveChanges());

                    Assert.NotNull(ex.ExpectedChangeVector);
                    Assert.NotNull(ex.ActualChangeVector);
                }
            }
        }

        [Fact]
        public void ConcurrencyExceptionShouldIncludeConflictsInfo_DocumentPut()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Store(new Document { Value = 1 }, "objects/1");
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var doc = session.Load<Document>("objects/1");
                    var cv = session.Advanced.GetChangeVectorFor(doc);
                    var djv = new DynamicJsonValue
                    {
                        ["Name"] = "ayende"
                    };
                    var re = store.GetRequestExecutor();
                    using (re.ContextPool.AllocateOperationContext(out var ctx))
                    using (var blittable = ctx.ReadObject(djv, "new/1"))
                    {
                        var ex = Assert.Throws<ConcurrencyException>(() => re.Execute(new PutDocumentCommand("new/1", cv, blittable), ctx));
                        Assert.Equal(cv, ex.ExpectedChangeVector);
                    }
                }
            }
        }

        private class Document
        {
            public int Value;
        }
    }
}
