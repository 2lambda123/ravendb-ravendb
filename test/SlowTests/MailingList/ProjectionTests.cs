// -----------------------------------------------------------------------
//  <copyright file="ProjectionTests.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client;
using Raven.Client.Document;
using Xunit;

namespace SlowTests.MailingList
{
    public class ProjectionTests : RavenNewTestBase
    {
        private readonly DocumentStore _store;
        private readonly IDocumentSession _session;

        public ProjectionTests()
        {
            _store = GetDocumentStore();
            _session = _store.OpenSession();

            Setup();
        }

        private void Setup()
        {
            var list = new List<Foo>
            {
                new Foo {Data = 1},
                new Foo {Data = 2},
                new Foo {Data = 3},
                new Foo {Data = 4},
            };

            list.ForEach(foo => _session.Store(foo));
            _session.SaveChanges();
        }

        //This works as expected
        [Fact]
        public void ActuallyGetData()
        {
            var foos = _session.Query<Foo>()
                .Customize(x => x.WaitForNonStaleResults())
                .Where(foo => foo.Data > 1)
                .Select(foo => new FooWithId
                {
                    Id = foo.Id,
                    Data = foo.Data
                })
                .ToList();

            Assert.True(foos.Count == 3);
        }

        //This works as expected
        [Fact]
        public void ShouldBeAbleToProjectIdOntoAnotherFieldCalledId()
        {
            var foos = _session.Query<Foo>()
                .Customize(x => x.WaitForNonStaleResults())
                .Where(foo => foo.Data > 1)
                .Select(foo => new FooWithId
                {
                    Id = foo.Id,
                    Data = foo.Data
                })
                .ToList();

            Assert.NotNull(foos[0].Id);
        }

        //Fails
        [Fact]
        public void ShouldBeAbleToProjectIdOntoAnotherName()
        {
            var foos = _session.Query<Foo>()
                              .Customize(x => x.WaitForNonStaleResults())
                              .Where(foo => foo.Data > 1)
                              .Select(foo => new FooWithFooId
                              {
                                  FooId = foo.Id,
                                  Data = foo.Data
                              })
                              .ToList();

            Assert.NotNull(foos[0].FooId);
        }

        [Fact]
        public void ShouldBeAbleToProjectIdOntoAnotherName_ButIdFieldWillBeFilledAnyway()
        {
            var foos = _session.Query<Foo>()
                              .Customize(x => x.WaitForNonStaleResults())
                              .Where(foo => foo.Data > 1)
                              .Select(foo => new FooWithFooIdAndId
                              {
                                  FooId = foo.Id,
                                  Data2 = foo.Data
                              })
                              .ToList();

            Assert.NotNull(foos[0].Id);
            Assert.NotNull(foos[0].FooId);
            Assert.Equal(foos[0].Id, foos[0].FooId);
        }

        private class Foo
        {
            public string Id { set; get; }
            public int Data { set; get; }
        }

        private class FooWithFooId
        {
            public string FooId { set; get; }
            public int Data { set; get; }
        }

        private class FooWithId
        {
            public string Id { set; get; }
            public int Data { set; get; }
        }

        private class FooWithFooIdAndId
        {
            public string FooId { set; get; }
            public string Id { set; get; }
            public int Data2 { set; get; }
        }
    }
}
