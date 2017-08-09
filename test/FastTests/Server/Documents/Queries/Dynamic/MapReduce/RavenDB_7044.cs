﻿using System;
using System.Linq;
using Raven.Client.Documents.Operations.Indexes;
using Xunit;

namespace FastTests.Server.Documents.Queries.Dynamic.MapReduce
{
    public class RavenDB_7044 : RavenTestBase
    {
        [Fact]
        public void Where_field_needs_to_be_incorporated_into_group_by_if_it_is_not_aggregate_operation()
        {
            using (var store = GetDocumentStore())
            {
                var today = DateTime.UtcNow;
                var tomorrow = today.AddDays(1);

                using (var session = store.OpenSession())
                {
                    session.Store(new ToDoTask()
                    {
                        DueDate = today,
                        Completed = false
                    });

                    session.Store(new ToDoTask
                    {
                        DueDate = today,
                        Completed = true
                    });

                    session.Store(new ToDoTask
                    {
                        DueDate = tomorrow,
                        Completed = false
                    });

                    session.Store(new ToDoTask
                    {
                        DueDate = tomorrow,
                        Completed = false
                    });

                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var tasksPerDay =
                        (from t in session.Query<ToDoTask>()
                        where t.Completed == false // we need to group by this field since it isn't aggregation
                        group t by t.DueDate
                        into g
                        select new
                        {
                            DueDate = g.Key,
                            TasksPerDate = g.Count()
                        }).ToList();

                    Assert.Equal(2, tasksPerDay.Count);

                    tasksPerDay = tasksPerDay.OrderBy(x => x.TasksPerDate).ToList();

                    Assert.Equal(today, tasksPerDay[0].DueDate);
                    Assert.Equal(1, tasksPerDay[0].TasksPerDate);

                    Assert.Equal(tomorrow, tasksPerDay[1].DueDate);
                    Assert.Equal(2, tasksPerDay[1].TasksPerDate);
                }

                using (var session = store.OpenSession())
                {
                    var tasksPerDay =
                        (from t in session.Query<ToDoTask>()
                        where t.Completed == false
                        group t by new { t.DueDate, t.Completed }
                        into g
                        select new
                        {
                            g.Key.DueDate,
                            TasksPerDate = g.Count()
                        }).ToList();

                    Assert.Equal(2, tasksPerDay.Count);

                    tasksPerDay = tasksPerDay.OrderBy(x => x.TasksPerDate).ToList();

                    Assert.Equal(today, tasksPerDay[0].DueDate);
                    Assert.Equal(1, tasksPerDay[0].TasksPerDate);

                    Assert.Equal(tomorrow, tasksPerDay[1].DueDate);
                    Assert.Equal(2, tasksPerDay[1].TasksPerDate);
                }

                var indexDefinitions = store.Admin.Send(new GetIndexesOperation(0, 10));

                Assert.Equal(1, indexDefinitions.Length); // all of the above queries should be handled by the same auto index
                Assert.Equal("Auto/ToDoTasks/ByCountReducedByCompletedAndDueDate", indexDefinitions[0].Name);
            }
        }

        private class ToDoTask
        {
            public DateTime DueDate { get; set; }

            public bool Completed { get; set; }
        }
    }
}
