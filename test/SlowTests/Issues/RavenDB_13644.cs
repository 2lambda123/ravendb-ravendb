﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using FastTests;
using Orders;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Client.Documents.Session;
using Tests.Infrastructure.Operations;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB_13644 : RavenTestBase
    {
        public RavenDB_13644(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void CanLoadCompareExchangeInIndexes()
        {
            using (var store = GetDocumentStore())
            {
                var index = new Index_With_CompareExchange();
                var indexName = index.IndexName;
                index.Execute(store);

                WaitForIndexing(store);

                RavenTestHelper.AssertNoIndexErrors(store);

                store.Maintenance.Send(new StopIndexingOperation());

                var staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.False(staleness.IsStale);

                var terms = store.Maintenance.Send(new GetTermsOperation(indexName, "City", null));
                Assert.Equal(0, terms.Length);

                // add doc
                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Store(new Company { Name = "CF", ExternalId = "companies/cf" });

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.Contains("There are still some documents to process from collection", staleness.StalenessReasons[0]);

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                RavenTestHelper.AssertNoIndexErrors(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.False(staleness.IsStale);

                terms = store.Maintenance.Send(new GetTermsOperation(indexName, "City", null));
                Assert.Equal(0, terms.Length);

                store.Maintenance.Send(new StopIndexingOperation());

                // add compare
                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Advanced.ClusterTransaction.CreateCompareExchangeValue("companies/cf", new Address { City = "Torun" });

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.Contains("There are still some compare exchange references to process for collection", staleness.StalenessReasons[0]);

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                RavenTestHelper.AssertNoIndexErrors(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.False(staleness.IsStale);

                terms = store.Maintenance.Send(new GetTermsOperation(indexName, "City", null));
                Assert.Equal(1, terms.Length);
                Assert.Contains("torun", terms);

                store.Maintenance.Send(new StopIndexingOperation());

                // add doc and compare
                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    session.Store(new Company { Name = "HR", ExternalId = "companies/hr" });
                    session.Advanced.ClusterTransaction.CreateCompareExchangeValue("companies/hr", new Address { City = "Cesarea" });

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.True(staleness.IsStale);
                Assert.Equal(2, staleness.StalenessReasons.Count);
                Assert.Contains("There are still some documents to process from collection", staleness.StalenessReasons[0]);
                Assert.Contains("There are still some compare exchange references to process for collection", staleness.StalenessReasons[1]);

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                RavenTestHelper.AssertNoIndexErrors(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.False(staleness.IsStale);

                terms = store.Maintenance.Send(new GetTermsOperation(indexName, "City", null));
                Assert.Equal(2, terms.Length);
                Assert.Contains("torun", terms);
                Assert.Contains("cesarea", terms);

                store.Maintenance.Send(new StopIndexingOperation());

                // update
                using (var session = store.OpenSession(new SessionOptions { TransactionMode = TransactionMode.ClusterWide }))
                {
                    var value = session.Advanced.ClusterTransaction.GetCompareExchangeValue<Address>("companies/hr");
                    value.Value.City = "Hadera";

                    session.Advanced.ClusterTransaction.UpdateCompareExchangeValue(value);

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.Contains("There are still some compare exchange references to process for collection", staleness.StalenessReasons[0]);

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                RavenTestHelper.AssertNoIndexErrors(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.False(staleness.IsStale);

                terms = store.Maintenance.Send(new GetTermsOperation(indexName, "City", null));
                Assert.Equal(2, terms.Length);
                Assert.Contains("torun", terms);
                Assert.Contains("hadera", terms);
            }
        }

        private class Index_With_CompareExchange : AbstractIndexCreationTask<Company>
        {
            public Index_With_CompareExchange()
            {
                Map = companies => from c in companies
                                   let address = LoadCompareExchangeValue<Address>(c.ExternalId)
                                   select new
                                   {
                                       address.City
                                   };
            }
        }
    }
}
