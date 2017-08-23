using System;
using System.Collections.Generic;
using System.Linq;
using FastTests;
using Newtonsoft.Json.Linq;
using Raven.Client.Documents;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Transformers;
using Xunit;

namespace SlowTests.MailingList
{
    public class SpatialQueryWithTransformTests : RavenTestBase
    {
        [Fact]
        public void CanQuery()
        {
            using (var store = GetDocumentStore())
            {
                new VacanciesIndex().Execute(store);
                new ViewItemTransformer().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new Employer
                    {
                        Id = "employers/ivanov",
                        Locations = new[]
                        {
                            new Location
                            {
                                Key = "Kiev",
                                Lng = 30.52340,
                                Ltd = 50.45010
                            }
                        }
                    });
                    session.Store(new Vacancy
                    {
                        Id = "employers/ivanov/vacancies/xy",
                        Name = "Test",
                        CompanyId = "employers/ivanov",
                        Owner = "employers/ivanov",
                        LocationId = "Kiev"
                    });
                    session.SaveChanges();
                }
                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var query = session.Query<VacanciesIndex.Result, VacanciesIndex>()
                        .TransformWith<ViewItemTransformer, ViewItemTransformer.View>()
                        .AddTransformerParameter("USERID", JToken.FromObject("fake"))
                        .Spatial("Coordinates", x => x.WithinRadius(10, 50.45010, 30.52340));
                    var result = query.ToList();
                    Assert.Equal(1, result.Count);
                }
            }
        }

        [Fact]
        public void CanQueryRemote()
        {
            using (var store = GetDocumentStore())
            {
                new VacanciesIndex().Execute(store);
                new ViewItemTransformer().Execute(store);

                using (var session = store.OpenSession())
                {
                    session.Store(new Employer
                    {
                        Id = "employers/ivanov",
                        Locations = new[]
                        {
                            new Location
                            {
                                Key = "Kiev",
                                Lng = 30.52340,
                                Ltd = 50.45010
                            }
                        }
                    });
                    session.Store(new Vacancy
                    {
                        Id = "employers/ivanov/vacancies/xy",
                        Name = "Test",
                        CompanyId = "employers/ivanov",
                        Owner = "employers/ivanov",
                        LocationId = "Kiev"
                    });
                    session.SaveChanges();
                }
                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    var query = session.Query<VacanciesIndex.Result, VacanciesIndex>()
                        .TransformWith<ViewItemTransformer, ViewItemTransformer.View>()
                        .AddTransformerParameter("USERID", JToken.FromObject("fake"))
                        .Spatial("Coordinates", x => x.WithinRadius(10, 50.45010, 30.52340));
                    var result = query.ToList();
                    Assert.Equal(1, result.Count);
                }
            }
        }

        private class Vacancy
        {
            public string Id { get; set; }
            public string Owner { get; set; }
            public string Name { get; set; }
            public string LocationId { get; set; }
            public string CompanyId { get; set; }
            public IEnumerable<string> Jobs { get; set; }
            public DateTimeOffset? CreatedAt { get; set; }
        }

        private class Employer
        {
            public string Id { get; set; }
            public string CompanyName { get; set; }
            public IList<Location> Locations { get; set; }
        }

        private class Location
        {
            public double Ltd { get; set; }
            public double Lng { get; set; }
            public string Key { get; set; }
        }

        private class ViewItemTransformer : AbstractTransformerCreationTask<Vacancy>
        {
            public const string USERID = "USERID";

            public class View
            {
                public string CompanyId { get; set; }
                public string Title { get; set; }
                public string Id { get; set; }
                public string Description { get; set; }
                public Location Location { get; set; }
            }

            public ViewItemTransformer()
            {
                TransformResults = results =>
                    from result in results
                    let employer = LoadDocument<Employer>(result.Owner)
                    let uid = Parameter(USERID)
                    let user = LoadDocument<Employer>(uid.ToString())
                    select new View
                    {
                        Id = result.Id,
                        Title = result.Name,
                        Location = employer.Locations.FirstOrDefault(x => x.Key == result.LocationId)
                    };
            }
        }

        private class VacanciesIndex
            : AbstractIndexCreationTask<Vacancy, VacanciesIndex.Result>
        {
            public class Result
            {
                public string CompanyId { get; set; }
                public DateTimeOffset? CreatedAt { get; set; }
                public IEnumerable<object> Filter { get; set; }
                public string DocId { get; set; }
                public string Name { get; set; }
            }

            public VacanciesIndex()
            {
                Map = vacancies => from vacancy in vacancies
                    let owner = LoadDocument<Employer>(vacancy.CompanyId)
                    let location = owner.Locations.FirstOrDefault(x => x.Key == vacancy.LocationId)
                    select new
                    {
                        DocId = vacancy.Id,
                        CompanyId = vacancy.Owner,
                        vacancy.Name,
                        vacancy.CreatedAt,
                        Filter = new object[]
                        {
                            vacancy.CompanyId,
                            vacancy.Jobs
                        },
                        Coordinates = CreateSpatialField(location.Ltd, location.Lng)
                    };
            }
        }
    }
}
