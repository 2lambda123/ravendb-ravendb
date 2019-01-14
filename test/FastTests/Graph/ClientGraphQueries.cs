﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace FastTests.Graph
{
    public class ClientGraphQueries : RavenTestBase
    {
        [Fact]
        public void CanGraphQuery()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    var bar = new Bar {Name = "Barvazon"};
                    var barId = "Bars/1";
                    session.Store(bar, barId);
                    session.Store(new Foo
                    {
                        Name = "Foozy",
                        Bars = new List<string> { barId }
                    });
                    session.SaveChanges();
                    FooBar res = session.Advanced.GraphQuery<FooBar>("match (Foo)-[Bars as _]->(Bars as Bar)").With("Foo",session.Query<Foo>()).Single();
                    Assert.Equal(res.Foo.Name, "Foozy");
                    Assert.Equal(res.Bar.Name, "Barvazon");
                }
            }
        }
        private class FooBar
        {
            public Foo Foo { get; set; }
            public Bar Bar { get; set; }
        }
        private class Foo
        {
            public string Name { get; set; }
            public List<string> Bars { get; set; }
        }

        private class Bar
        {
            public string Name { get; set; }
        }
    }
}
