﻿using System.Linq;
using FastTests;
using FastTests.Issues;
using Raven.Client;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_6427 : RavenTestBase
    {
        private class Stuff
        {
            public int Key { get; set; }

        }

        [Fact]
        public void CanPatchExactlyOneTime()
        {
            using (var store = GetDocumentStore())
            {
                using (var bulkInsert = store.BulkInsert())
                {
                    new StuffIndex().Execute(store);

                    // use value over batchSize = 1024
                    for (var i = 0; i < 1030; i++)
                    {
                        bulkInsert.Store(new Stuff
                        {
                            Key = 0
                        });
                    }
                }

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    store.Operations.Send(new PatchCollectionOperation("stuffs", new PatchRequest
                    {
                        Script = "this.Key = this.Key + 1;"
                    })).WaitForCompletion();

                    using (var reader = session.Advanced.Stream<Stuff>(startsWith: "stuffs/"))
                    {
                        while (reader.MoveNext())
                        {
                            var doc = reader.Current.Document;
                            Assert.Equal(1, doc.Key);
                        }
                    }
                }
            }
        }

        private class StuffIndex : AbstractIndexCreationTask<Stuff>
        {
            public StuffIndex()
            {
                Map = entities => from entity in entities
                    select new
                    {
                        entity.Key,
                    };
            }
        }
    }
}