// -----------------------------------------------------------------------
//  <copyright file="RavenDB_334.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Linq;
using FastTests;
using Raven.NewClient.Abstractions.Indexing;
using Raven.NewClient.Client.Indexes;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_334 : RavenNewTestBase
    {
        private class Foo
        {
            public string Id { get; set; }
            public DateTime DateTime { get; set; }
        }

        private class FooIndex : AbstractIndexCreationTask<Foo>
        {
            public class IndexedFoo
            {
                public string Id { get; set; }
                public DateTime DateTime { get; set; }
            }

            public FooIndex()
            {
                Map = foos => from f in foos select new { f.Id };

                Store(x => x.DateTime, FieldStorage.Yes);
            }
        }

        private class FooTransformer : AbstractTransformerCreationTask<Foo>
        {
            public FooTransformer()
            {

                TransformResults = foos =>
                                    from f in foos select new { f.DateTime };
            }
        }

        [Fact]
        public void CanGetUtcFromDate()
        {
            using (var documentStore = GetDocumentStore())
            {
                new FooIndex().Execute(documentStore);
                new FooTransformer().Execute(documentStore);

                using (var session = documentStore.OpenSession())
                {
                    var foo = new Foo { Id = "foos/1", DateTime = DateTime.UtcNow };

                    session.Store(foo);

                    session.SaveChanges();
                }

                using (var session = documentStore.OpenSession())
                {
                    var foo = session.Load<Foo>("foos/1");

                    var indexedFoo = session.Query<Foo, FooIndex>()
                        .Customize(c => c.WaitForNonStaleResults())
                        .TransformWith<FooTransformer, FooIndex.IndexedFoo>()
                        .Single(f => f.Id == "foos/1");
                    Assert.Equal(foo.DateTime.Kind, indexedFoo.DateTime.Kind);
                    Assert.Equal(foo.DateTime, indexedFoo.DateTime);
                }
            }
        }
    }
}
