﻿using System.Collections.Generic;
using FastTests;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues;

public class RavenDB21342 : RavenTestBase
{
    public RavenDB21342(ITestOutputHelper output) : base(output)
    {
    }

    private class Item
    {
        public string Name;
        public int? Color;
    }

    [RavenTheory(RavenTestCategory.Querying)]
    [RavenData(SearchEngineMode = RavenSearchEngineMode.All)]
    public void CanHandleNumericGreaterThanOnNullValue(Options options)
    {
        using var store = GetDocumentStore(options);

        using (var s = store.OpenSession())
        {
            s.Store(new Item
            {
                Name = "Oren",
                Color = null
            }, "items/1");
            s.SaveChanges();
        }

        using (var s = store.OpenSession())
        {
            List<Item> collection = s.Advanced.RawQuery<Item>("from Items where Name = 'Oren' and Color > 10")
                .ToList();
            Assert.Empty(collection);
        }
    }
}
