// -----------------------------------------------------------------------
//  <copyright file="Tamir.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.NewClient.Client;
using Raven.NewClient.Client.Indexing;
using Raven.NewClient.Client.Linq;
using Raven.NewClient.Operations.Databases.Indexes;
using Xunit;

namespace SlowTests.MailingList
{
    public class Tamir : RavenNewTestBase
    {
        private class Developer
        {
            public string Name { get; set; }
            public IDE PreferredIDE { get; set; }
        }

        private class IDE
        {
            public string Name { get; set; }
        }

        [Fact]
        public void InOnObjects()
        {
            using (var store = GetDocumentStore())
            {
                store.Admin.Send(new PutIndexOperation("DevByIDE", new IndexDefinition
                {
                    Maps = { @"from dev in docs.Developers select new { dev.PreferredIDE, dev.PreferredIDE.Name }" }
                }));

                using (var session = store.OpenSession())
                {

                    IEnumerable<Developer> developers = from name in new[] { "VisualStudio", "Vim", "Eclipse", "PyCharm" }
                                                        select new Developer
                                                        {
                                                            Name = string.Format("Fan of {0}", name),
                                                            PreferredIDE = new IDE
                                                            {
                                                                Name = name
                                                            }
                                                        };

                    foreach (var developer in developers)
                    {
                        session.Store(developer);
                    }

                    session.SaveChanges();

                }

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var bestIDEsEver = new[] { new IDE { Name = "VisualStudio" }, new IDE { Name = "IntelliJ" } };

                    RavenQueryStatistics stats;
                    // this query returns with results
                    var querySpecificIDE = session.Query<Developer>().Statistics(out stats)
                                                  .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(5)))
                                                  .Where(d => d.PreferredIDE.Name == "VisualStudio")
                                                  .ToList();

                    Assert.NotEmpty(querySpecificIDE);

                    // this query returns empty
                    var queryUsingWhereIn = session.Query<Developer>("DevByIDE").Statistics(out stats)
                                                   .Customize(x => x.WaitForNonStaleResults(TimeSpan.FromSeconds(5)))
                                                   .Where(d => d.PreferredIDE.In(bestIDEsEver))
                                                   .ToList();
                    Assert.NotEmpty(queryUsingWhereIn);
                }
            }
        }
    }
}
