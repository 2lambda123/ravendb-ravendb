// -----------------------------------------------------------------------
//  <copyright file="Garrett.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.NewClient.Client.Linq;
using Xunit;

namespace SlowTests.MailingList
{
    public class Garrett : RavenNewTestBase
    {
        private class StrategyIndividual
        {
            public string OtherProp { get; set; }
            public Dictionary<int, double> Statistcs { get; set; }
        }

        [Fact]
        public void CanOrderByDictionaryValue()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new StrategyIndividual
                    {
                        OtherProp = "Test",
                        Statistcs = new Dictionary<int, double>
                        {
                            { 4, 5.0 }
                        }
                    });

                    session.SaveChanges();
                }

                using (var s = store.OpenSession())
                {
                    s.Query<StrategyIndividual>()
                     .Where(x => x.Statistcs[4] == 0)
                     .OrderBy(x => x.Statistcs[4])
                     .ToList();
                }
            }
        }
    }
}
