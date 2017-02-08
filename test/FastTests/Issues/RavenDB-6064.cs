﻿using Xunit;
using System.Linq;
using Raven.NewClient.Client.Indexes;
using Raven.NewClient.Operations.Databases.Indexes;
using Tests.Infrastructure;

namespace FastTests.Issues
{
    public class RavenDB_6064_2 : RavenNewTestBase
    {
        private class User
        {
            public string A, B, C;
            public string D;
        }

        private class User_Index : AbstractIndexCreationTask<User, User>
        {
            public User_Index()
            {
                Map = users =>
                    from user in users
                    select new
                    {
                        user.A,
                        user.B,
                        user.C,
                        user.D
                    };
                Reduce = results =>
                    from result in results
                    group result by result.D
                    into g
                    select new
                    {
                        D = g.Key,
                        g.First().A,
                        g.First().B,
                        g.First().C
                    };
            }
        }

        [Fact]
        public void CanIndexWithThreeCompressedProperties()
        {
            using (var store = GetDocumentStore())
            {
                using (var s = store.OpenSession())
                {
                    s.Store(new User
                    {
                        A = new string('a', 129),
                        B = new string('b', 257),
                        C = new string('c', 513),
                        D = "u"
                    });
                    s.SaveChanges();
                }
                new User_Index().Execute(store);

                WaitForIndexing(store);

                using (var s = store.OpenSession())
                {
                    var errors = store.Admin.Send(new GetIndexErrorsOperation())[0];
                    Assert.Empty(errors.Errors);
                    var collection = s.Query<User, User_Index>().ToList();
                    Assert.NotEmpty(collection);
                }
            }
        }
    }
}