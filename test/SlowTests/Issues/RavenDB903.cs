﻿using System;
using System.Diagnostics;
using System.Linq;
using FastTests;
using Newtonsoft.Json;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Session;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RavenDB903 : RavenTestBase
    {
        public RavenDB903(ITestOutputHelper output) : base(output)
        {
        }

        private class Product
        {
            public string Name { get; set; }
            public string Description { get; set; }
        }

        [Theory]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void Test1(Options options)
        {
            DoTest(session => session.Query<Product, TestIndex>()
                                     .Search(x => x.Description, "Hello")
                                     .Intersect()
                                     .Where(x => x.Name == "Bar")
                                     .As<Product>(), options);
        }

        [Theory]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene)]
        public void Test2(Options options)
        {
            DoTest(session => session.Query<Product, TestIndex>()
                                     .Where(x => x.Name == "Bar")
                                     .Intersect()
                                     .Search(x => x.Description, "Hello")
                                     .As<Product>(), options);
        }

        private void DoTest(Func<IDocumentSession, IQueryable<Product>> queryFunc, Options options)
        {
            using (var documentStore = GetDocumentStore(options))
            {
                documentStore.ExecuteIndex(new TestIndex());

                using (var session = documentStore.OpenSession())
                {
                    session.Store(new Product { Name = "Foo", Description = "Hello World" });
                    session.Store(new Product { Name = "Bar", Description = "Hello World" });
                    session.Store(new Product { Name = "Bar", Description = "Goodbye World" });

                    session.SaveChanges();
                }

                Indexes.WaitForIndexing(documentStore);

                using (var session = documentStore.OpenSession())
                {
                    var query = queryFunc(session);

                    Debug.WriteLine(query);
                    Debug.WriteLine("");

                    var products = query.ToList();
                    foreach (var product in products)
                    {
                        Debug.WriteLine(JsonConvert.SerializeObject(product, Formatting.Indented));
                    }

                    Assert.Equal(1, products.Count);
                }
            }
        }

        private class TestIndex : AbstractIndexCreationTask<Product>
        {
            public TestIndex()
            {
                Map = products => from product in products
                                  select new
                                  {
                                      product.Name,
                                      product.Description
                                  };

                Index(x => x.Description, FieldIndexing.Search);
            }
        }
    }
}
