﻿using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Linq;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues
{
    public class RDBC_78 : RavenTestBase
    {
        public RDBC_78(ITestOutputHelper output) : base(output)
        {
        }

        private class Person
        {
            public string Name { get; set; }
            public List<Pet> Pets { get; set; }
        }
        
        private class Pet
        {
            public string Name { get; set; }
        }
        
        private class PersonIndex : AbstractIndexCreationTask<Person>
        {
            public PersonIndex()
            {
                Map = persons => from person in persons
                    select new { person.Name, person.Pets };
                Store(p=> p.Pets, FieldStorage.Yes);
                Index(p => p.Pets, FieldIndexing.No);
            }
        }
                
        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Lucene, DatabaseMode = RavenDatabaseMode.All)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.Corax, Skip = "Complex object")]
        public void CanQueryNestedClass(Options options)
        {             
            using (var store = GetDocumentStore(options))
            {
                new PersonIndex().Execute(store); 
                
                var fluffy = new Pet { Name = "Fluffy" };
                var john = new Person { Name = "John", Pets = new List<Pet> { fluffy } };
                
                using (var session = store.OpenSession())
                {
                    session.Store(john);
                    session.SaveChanges();
                }
                                
                Indexes.WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var allPets = new List<Pet> { fluffy };                    
                    var query = session.Query<Person>().Where(p => p.Pets.ContainsAny(allPets)).ToList();                    
                    Assert.Equal(1, query.Count);
                }                                
            }
        }
    }
}
