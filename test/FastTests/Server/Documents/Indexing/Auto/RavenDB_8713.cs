﻿using System.Linq;
using System.Threading;
using Raven.Client.Documents.Operations;
using Raven.Server.Config;
using Xunit;

namespace FastTests.Server.Documents.Indexing.Auto
{
    public class RavenDB_8713 : RavenTestBase
    {
        [Fact]
        public void CanQueryOnCaseSensitiveFields()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });

                    session.Store(new Item
                    {
                        Name = "ja",
                        name = "da"
                    });

                    session.SaveChanges();

                    var count = session.Query<Item>().Statistics(out var stats).Count(x => x.Name == "joe" || x.name == "da");

                    Assert.Equal(2, count);
                    Assert.Equal("Auto/Items/BynameAndName", stats.IndexName);
                }
            }
        }

        [Fact]
        public void ShouldExtendMappingDueToCaseSensitiveFields()
        {
            using (var store = GetDocumentStore(new Options
            {
                ModifyDatabaseRecord = record => record.Settings[RavenConfiguration.GetKey(x => x.Indexing.TimeBeforeDeletionOfSupersededAutoIndex)] = "0"
            }))
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });

                    session.Store(new Item
                    {
                        Name = "ja",
                        name = "da"
                    });

                    session.SaveChanges();

                    var count = session.Query<Item>().Statistics(out var stats).Count(x => x.Name == "joe");

                    Assert.Equal(1, count);
                    Assert.Equal("Auto/Items/ByName", stats.IndexName);

                    // should extend mapping and remove prev index

                    var results = session.Query<Item>().Statistics(out stats).Count(x => x.Name == "joe" || x.name == "da");

                    Assert.Equal(2, results);
                    Assert.Equal("Auto/Items/BynameAndName", stats.IndexName);
                }

                IndexInformation[] indexes = null;

                Assert.True(SpinWait.SpinUntil(() => (indexes = store.Admin.Send(new GetStatisticsOperation()).Indexes).Length == 1, 1000));

                Assert.Equal(1, indexes.Length);
                Assert.Equal("Auto/Items/BynameAndName", indexes[0].Name);
            }
        }

        [Fact]
        public void CanQueryOnCaseSensitiveGroupByFields()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });

                    session.Store(new Item
                    {
                        Name = "joe",
                        name = "doe"
                    });


                    session.Store(new Item
                    {
                        Name = "ja",
                        name = "da"
                    });

                    session.SaveChanges();

                    var results = session.Query<Item>().Statistics(out var stats).GroupBy(x => new {x.name, x.Name}).Select(g => new
                    {
                        g.Key.name,
                        g.Key.Name,
                        Count = g.Count()
                    }).OrderBy(x => x.Count).ToList();

                    Assert.Equal(2, results.Count);

                    Assert.Equal(1, results[0].Count);
                    Assert.Equal("ja", results[0].Name);
                    Assert.Equal("da", results[0].name);

                    Assert.Equal(2, results[1].Count);
                    Assert.Equal("joe", results[1].Name);
                    Assert.Equal("doe", results[1].name);
                }
            }
        }

        private class Item
        {
            public string Name { get; set; }

            public string name { get; set; }

        }
    }
}
