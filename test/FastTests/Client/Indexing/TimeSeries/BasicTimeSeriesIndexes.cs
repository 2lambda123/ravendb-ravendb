﻿using System;
using System.Linq;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Indexes.TimeSeries;
using Raven.Client.Documents.Operations.Indexes;
using Raven.Tests.Core.Utils.Entities;
using Sparrow.Extensions;
using Tests.Infrastructure.Operations;
using Xunit;
using Xunit.Abstractions;

namespace FastTests.Client.Indexing.TimeSeries
{
    public class BasicTimeSeriesIndexes : RavenTestBase
    {
        public BasicTimeSeriesIndexes(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void BasicMapIndex()
        {
            using (var store = GetDocumentStore())
            {
                var now1 = DateTime.Today; // DateTime.Now; - RavenDB-14242
                var now2 = now1.AddSeconds(1);

                using (var session = store.OpenSession())
                {
                    var company = new Company();
                    session.Store(company, "companies/1");
                    session.TimeSeriesFor(company).Append("HeartRate", now1, "tag", new double[] { 7 });

                    session.SaveChanges();
                }

                store.Maintenance.Send(new StopIndexingOperation());

                var result = store.Maintenance.Send(new PutIndexesOperation(new TimeSeriesIndexDefinition
                {
                    Name = "MyTsIndex",
                    Maps = {
                    "from ts in timeSeries.Companies.HeartRate " +
                    "from entry in ts.Entries " +
                    "select new { " +
                    "   HeartBeat = entry.Values[0], " +
                    "   Date = entry.TimeStamp.Date, " +
                    "   User = ts.DocumentId " +
                    "}" }
                }));

                var staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.False(staleness.IsStale);

                store.Maintenance.Send(new StopIndexingOperation());

                using (var session = store.OpenSession())
                {
                    var company = session.Load<Company>("companies/1");
                    session.TimeSeriesFor(company).Append("HeartRate", now2, "tag", new double[] { 3 });

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.False(staleness.IsStale);

                store.Maintenance.Send(new StopIndexingOperation());

                Assert.Equal(2, WaitForValue(() => store.Maintenance.Send(new GetIndexStatisticsOperation("MyTsIndex")).EntriesCount, 2));

                var terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "HeartBeat", null));
                Assert.Equal(2, terms.Length);
                Assert.Contains("7", terms);
                Assert.Contains("3", terms);

                terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "Date", null));
                Assert.Equal(1, terms.Length);
                Assert.Contains(now1.Date.GetDefaultRavenFormat(), terms);

                terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "User", null));
                Assert.Equal(1, terms.Length);
                Assert.Contains("companies/1", terms);

                // delete time series

                using (var session = store.OpenSession())
                {
                    var company = session.Load<Company>("companies/1");
                    session.TimeSeriesFor(company).Remove("HeartRate", now2);

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.False(staleness.IsStale);

                terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "HeartBeat", null));
                Assert.Equal(1, terms.Length);
                Assert.Contains("7", terms);

                // delete document

                store.Maintenance.Send(new StopIndexingOperation());

                using (var session = store.OpenSession())
                {
                    session.Delete("companies/1");
                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.False(staleness.IsStale);

                terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "HeartBeat", null));
                Assert.Equal(0, terms.Length);
            }
        }

        [Fact]
        public void BasicIndexWithLoad()
        {
            using (var store = GetDocumentStore())
            {
                var now1 = DateTime.Now;
                var now2 = now1.AddSeconds(1);

                using (var session = store.OpenSession())
                {
                    var employee = new Employee
                    {
                        FirstName = "John"
                    };
                    session.Store(employee, "employees/1");

                    var company = new Company();
                    session.Store(company, "companies/1");

                    session.TimeSeriesFor(company).Append("HeartRate", now1, employee.Id, new double[] { 7 });

                    session.SaveChanges();
                }

                store.Maintenance.Send(new StopIndexingOperation());

                var result = store.Maintenance.Send(new PutIndexesOperation(new TimeSeriesIndexDefinition
                {
                    Name = "MyTsIndex",
                    Maps = {
                    "from ts in timeSeries.Companies.HeartRate " +
                    "from entry in ts.Entries " +
                    "let employee = LoadDocument(entry.Tag, \"Employees\")" +
                    "select new { " +
                    "   HeartBeat = entry.Value, " +
                    "   Date = entry.TimeStamp.Date, " +
                    "   User = ts.DocumentId, " +
                    "   Employee = employee.FirstName" +
                    "}" }
                }));

                var staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.False(staleness.IsStale);

                store.Maintenance.Send(new StopIndexingOperation());

                Assert.Equal(1, WaitForValue(() => store.Maintenance.Send(new GetIndexStatisticsOperation("MyTsIndex")).EntriesCount, 1));

                var terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "Employee", null));
                Assert.Equal(1, terms.Length);
                Assert.Contains("john", terms);

                using (var session = store.OpenSession())
                {
                    var employee = session.Load<Employee>("employees/1");
                    employee.FirstName = "Bob";

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.False(staleness.IsStale);

                store.Maintenance.Send(new StopIndexingOperation());

                Assert.Equal(1, WaitForValue(() => store.Maintenance.Send(new GetIndexStatisticsOperation("MyTsIndex")).EntriesCount, 1));

                terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "Employee", null));
                Assert.Equal(1, terms.Length);
                Assert.Contains("bob", terms);

                using (var session = store.OpenSession())
                {
                    session.Delete("employees/1");

                    session.SaveChanges();
                }

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation("MyTsIndex"));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForIndexing(store);

                Assert.Equal(1, WaitForValue(() => store.Maintenance.Send(new GetIndexStatisticsOperation("MyTsIndex")).EntriesCount, 1));

                terms = store.Maintenance.Send(new GetTermsOperation("MyTsIndex", "Employee", null));
                Assert.Equal(0, terms.Length);
            }
        }

        [Fact( Skip =  "TODO arek")]
        public void BasicMapReduceIndex()
        {
            using (var store = GetDocumentStore())
            {
                var now1 = DateTime.Today; 

                using (var session = store.OpenSession())
                {
                    var user = new User();
                    session.Store(user, "users/1");

                    for (int i = 0; i < 10; i++)
                    {
                        session.TimeSeriesFor(user).Append("HeartRate", now1.AddHours(i), "abc", new double[] {180 + i});
                    }

                    session.SaveChanges();
                }

                store.Maintenance.Send(new StopIndexingOperation());

                string indexName = "AverageHeartRateDaily/ByDateAndUser";

                var result = store.Maintenance.Send(new PutIndexesOperation(new TimeSeriesIndexDefinition
                {
                    Name = indexName,
                    Maps = {
                    "from ts in timeSeries.Users.HeartRate " +
                    "from entry in ts.Entries " +
                    "select new { " +
                    "   HeartBeat = entry.Value, " +
                    "   Date = new DateTime(entry.TimeStamp.Date.Year, entry.TimeStamp.Date.Month, entry.TimeStamp.Date.Day), " +
                    "   User = ts.DocumentId, " +
                    "   Count = 1" +
                    "}" },
                    Reduce = "from r in results " +
                             "group r by new { r.Date, r.User } into g " +
                             "let sumHeartBeat = g.Sum(x => x.HeartBeat) " +
                             "let sumCount = g.Sum(x => x.Count) " +
                             "select new {" +
                             "  HeartBeat = sumHeartBeat / sumCount, " +
                             "  Date = g.Key.Date," +
                             "  User = g.Key.User, " +
                             "  Count = sumCount" +
                             "}"
                }));

                var staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.True(staleness.IsStale);
                Assert.Equal(1, staleness.StalenessReasons.Count);
                Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                store.Maintenance.Send(new StartIndexingOperation());

                WaitForUserToContinueTheTest(store);

                WaitForIndexing(store);

                staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                Assert.False(staleness.IsStale);

                store.Maintenance.Send(new StopIndexingOperation());

                

                //using (var session = store.OpenSession())
                //{
                //    var user = session.Load<User>("users/1");
                //    session.TimeSeriesFor(user).Append("HeartRate", now2, "tag", new double[] { 3 });

                //    session.SaveChanges();
                //}

                //staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                //Assert.True(staleness.IsStale);
                //Assert.Equal(1, staleness.StalenessReasons.Count);
                //Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                //store.Maintenance.Send(new StartIndexingOperation());

                //WaitForIndexing(store);

                //staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                //Assert.False(staleness.IsStale);

                //store.Maintenance.Send(new StopIndexingOperation());

                //Assert.Equal(2, WaitForValue(() => store.Maintenance.Send(new GetIndexStatisticsOperation(indexName)).EntriesCount, 2));

                //var terms = store.Maintenance.Send(new GetTermsOperation(indexName, "HeartBeat", null));
                //Assert.Equal(2, terms.Length);
                //Assert.Contains("7", terms);
                //Assert.Contains("3", terms);

                //terms = store.Maintenance.Send(new GetTermsOperation(indexName, "Date", null));
                //Assert.Equal(1, terms.Length);
                //Assert.Contains(now1.Date.GetDefaultRavenFormat(), terms);

                //terms = store.Maintenance.Send(new GetTermsOperation(indexName, "User", null));
                //Assert.Equal(1, terms.Length);
                //Assert.Contains("users/1", terms);

                //// delete time series

                //using (var session = store.OpenSession())
                //{
                //    var user = session.Load<User>("users/1");
                //    session.TimeSeriesFor(user).Remove("HeartRate", now2);

                //    session.SaveChanges();
                //}

                //staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                //Assert.True(staleness.IsStale);
                //Assert.Equal(1, staleness.StalenessReasons.Count);
                //Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                //store.Maintenance.Send(new StartIndexingOperation());

                //WaitForIndexing(store);

                //staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                //Assert.False(staleness.IsStale);

                //terms = store.Maintenance.Send(new GetTermsOperation(indexName, "HeartBeat", null));
                //Assert.Equal(1, terms.Length);
                //Assert.Contains("7", terms);

                //// delete document

                //store.Maintenance.Send(new StopIndexingOperation());

                //using (var session = store.OpenSession())
                //{
                //    session.Delete("users/1");
                //    session.SaveChanges();
                //}

                //staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                //Assert.True(staleness.IsStale);
                //Assert.Equal(1, staleness.StalenessReasons.Count);
                //Assert.True(staleness.StalenessReasons.Any(x => x.Contains("There are still")));

                //store.Maintenance.Send(new StartIndexingOperation());

                //WaitForIndexing(store);

                //staleness = store.Maintenance.Send(new GetIndexStalenessOperation(indexName));
                //Assert.False(staleness.IsStale);

                //terms = store.Maintenance.Send(new GetTermsOperation(indexName, "HeartBeat", null));
                //Assert.Equal(0, terms.Length);
            }
        }
    }
}
