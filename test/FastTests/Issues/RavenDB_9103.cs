﻿using System;
using System.Linq;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Xunit;

namespace FastTests.Issues
{
    public class RavenDB_9103 : RavenTestBase
    {
        [Fact]
        public void ProjectingFromIndexFieldWithGroupPropertyShouldWork()
        {
            using (var store = GetDocumentStore())
            {
                var index = new Model_Info();
                store.ExecuteIndex(index);
                using (var session = store.OpenSession())
                {
                    session.Store(new Model
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = nameof(Model.Name),
                        Group = nameof(Model.Group),
                    });
                    session.SaveChanges();
                    WaitForIndexing(store);
                    var infos = session.Query<ModelInfo, Model_Info>()
                        .ProjectFromIndexFieldsInto<ModelInfo>()
                        .ToList();
                }
            }
        }
        
        public class Model
        {
            public string Id { get; set; }

            public string Name { get; set; }
            public string Group { get; set; }
        }

        public class ModelInfo
        {
            public string Name { get; set; }
            public string Group { get; set; }
        }

        public class Model_Info : AbstractIndexCreationTask<Model>
        {
            public Model_Info()
            {
                Map = list => list
                    .Select(x => new
                    {
                        x.Name,
                        x.Group
                    });

                StoreAllFields(FieldStorage.Yes);
            }
        }
    }
}
