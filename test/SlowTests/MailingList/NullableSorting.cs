// -----------------------------------------------------------------------
//  <copyright file="NullableSorting.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using FastTests;
using Raven.NewClient.Abstractions.Indexing;
using Raven.NewClient.Client.Indexes;
using Xunit;

namespace SlowTests.MailingList
{
    public class NullableSorting : RavenNewTestBase
    {
        private class Blog
        {
            public string Id { get; set; }
            public decimal? Price { get; set; }
        }

        private class Blog_Search : AbstractIndexCreationTask<Blog, Blog>
        {
            public Blog_Search()
            {
                Map = blogs => from b in blogs
                               select new Blog
                               {
                                   Id = b.Id,
                                   Price = b.Price
                               };
                Index(b => b.Price, FieldIndexing.Default);
                Sort(b => b.Price, SortOptions.NumericDouble);
            }
        }

        [Fact]
        public void SortByNullableDecimal()
        {
            using (var store = GetDocumentStore())
            {
                using (var session = store.OpenSession())
                {
                    session.Store(new Blog { Price = 10.20m });
                    session.Store(new Blog { Price = 1.50m });
                    session.Store(new Blog { });  //with price set it works
                    session.Store(new Blog { Price = 4.20m });
                    session.SaveChanges();
                }

                new Blog_Search().Execute(store);

                using (var session = store.OpenSession())
                {
                    var result = session
                     .Advanced
                     .DocumentQuery<Blog, Blog_Search>()
                     .WaitForNonStaleResults()
                     .OrderBy(x => x.Price)
                     .ToArray();

                    var ids = result.Select(b => b.Id).ToArray();

                    Assert.Equal(new[] { "blogs/3", "blogs/2", "blogs/4", "blogs/1" }, ids);
                }
            }
        }
    }
}
