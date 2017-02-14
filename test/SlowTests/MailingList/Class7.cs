using System;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using Raven.Client.Documents.Indexes;
using Raven.Client.Exceptions;
using Xunit;

namespace SlowTests.MailingList
{
    public class Class7 : RavenNewTestBase
    {
        [Fact]
        public void ThrowsOnUnindexedSorts()
        {
            using (var store = GetDocumentStore())
            {
                new PersonIndex().Execute(store);

                Person personA;
                Person personB;
                using (var session = store.OpenSession())
                {
                    personA = new Person();
                    personA.Name = "A";
                    personA.Surname = "A";
                    session.Store(personA);

                    personB = new Person();
                    personB.Name = "B";
                    personB.Surname = "B";
                    session.Store(personB);

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var e = Assert.Throws<RavenException>(() =>
                    {
                        var results = session.Query<Person, PersonIndex>()
                            .Customize(customization => customization.WaitForNonStaleResultsAsOfNow())
                            .OrderByDescending(x => x.Surname)
                            .ToList();
                    });

                    Assert.Contains("The field 'Surname' is not indexed, cannot sort on fields that are not indexed", e.InnerException.Message);
                }

                using (var session = store.OpenSession())
                {
                    var e = Assert.Throws<RavenException>(() =>
                    {
                        var results = session.Advanced.DocumentQuery<Person, PersonIndex>()
                           .WaitForNonStaleResultsAsOfNow()
                           .OrderByDescending(x => x.Surname)
                           .ToList();
                    });

                    Assert.Contains("The field 'Surname' is not indexed, cannot sort on fields that are not indexed", e.InnerException.Message);
                }
            }
        }

        private class Person
        {
            public string Name { get; set; }
            public string Surname { get; set; }
        }

        private class PersonIndex : AbstractIndexCreationTask<Person>
        {
            public PersonIndex()
            {
                Map = persons => from person in persons
                                 select new
                                 {
                                     person.Name,
                                 };
            }
        }
    }

}
