﻿using System.Linq;
using FastTests;
using Raven.NewClient.Client;
using Raven.NewClient.Client.Indexes;
using Xunit;

namespace SlowTests.Issues
{
    public class QueryCommaTest : RavenNewTestBase
    {
        private class Employee
        {
            public string Id { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        private class Employees_ByFirstName : AbstractIndexCreationTask<Employee>
        {
            public Employees_ByFirstName()
            {
                Map = employees => from employee in employees
                                   select new
                                   {
                                       employee.Id,
                                       employee.FirstName,
                                   };
            }
        }

        [Fact]
        public void CommaInQueryTest()
        {
            using (var store = GetDocumentStore())
            {
                new Employees_ByFirstName().Execute(store);

                using (var session = store.OpenSession())
                {
                    session
                        .Query<Employee, Employees_ByFirstName>()
                        .Search(x => x.FirstName, "foo , bar")
                        .ToList();
                    // Lucene.Net.QueryParsers.ParseException: Could not parse:
                    // ' FirstName:(foo , bar)' ---> Lucene.Net.QueryParsers.ParseException: Syntax error, unexpected COMMA
                }
            }
        }
    }
}