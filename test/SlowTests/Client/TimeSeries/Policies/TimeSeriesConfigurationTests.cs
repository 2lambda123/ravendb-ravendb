﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FastTests;
using FastTests.Server.Replication;
using Raven.Client.Documents;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.TimeSeries;
using Raven.Client.Documents.Operations.TransactionsRecording;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions;
using Raven.Client.ServerWide.Operations;
using Raven.Tests.Core.Utils.Entities;
using Sparrow;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Client.TimeSeries.Policies
{
    public class TimeSeriesConfigurationTests : ReplicationTestBase
    {
        public TimeSeriesConfigurationTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task CanConfigureTimeSeries()
        {
            using (var store = GetDocumentStore())
            {
                var config = new TimeSeriesConfiguration();
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                config.Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>();
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                config.Collections["Users"] = new TimeSeriesCollectionConfiguration();
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                config.Collections["Users"].Policies = new List<TimeSeriesPolicy>
                {
                    new TimeSeriesPolicy("ByHourFor12Hours",TimeValue.FromHours(1), TimeValue.FromHours(48)),
                    new TimeSeriesPolicy("ByMinuteFor3Hours",TimeValue.FromMinutes(1), TimeValue.FromMinutes(180)),
                    new TimeSeriesPolicy("BySecondFor1Minute",TimeValue.FromSeconds(1), TimeValue.FromSeconds(60)),
                    new TimeSeriesPolicy("ByMonthFor1Year",TimeValue.FromMonths(1), TimeValue.FromYears(1)),
                    new TimeSeriesPolicy("ByYearFor3Years",TimeValue.FromYears(1), TimeValue.FromYears(3)),
                    new TimeSeriesPolicy("ByDayFor1Month",TimeValue.FromDays(1), TimeValue.FromMonths(1)),
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));
                
                config.Collections["Users"].RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromHours(96));
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));


                var updated = (await store.Maintenance.Server.SendAsync(new GetDatabaseRecordOperation(store.Database))).TimeSeries;
                var collection = updated.Collections["Users"];

                var policies = collection.Policies;
                Assert.Equal(6, policies.Count);

                Assert.Equal(TimeValue.FromSeconds(60), policies[0].RetentionTime);
                Assert.Equal(TimeValue.FromSeconds(1), policies[0].AggregationTime);

                Assert.Equal(TimeValue.FromMinutes(180), policies[1].RetentionTime);
                Assert.Equal(TimeValue.FromMinutes(1), policies[1].AggregationTime);

                Assert.Equal(TimeValue.FromHours(48), policies[2].RetentionTime);
                Assert.Equal(TimeValue.FromHours(1), policies[2].AggregationTime);

                Assert.Equal(TimeValue.FromMonths(1), policies[3].RetentionTime);
                Assert.Equal(TimeValue.FromDays(1), policies[3].AggregationTime);

                Assert.Equal(TimeValue.FromYears(1), policies[4].RetentionTime);
                Assert.Equal(TimeValue.FromMonths(1), policies[4].AggregationTime);

                Assert.Equal(TimeValue.FromYears(3), policies[5].RetentionTime);
                Assert.Equal(TimeValue.FromYears(1), policies[5].AggregationTime);
            }
        }

        [Fact]
        public async Task NotValidConfigureShouldThrow()
        {
            using (var store = GetDocumentStore())
            {
                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromMonths(1)),
                            Policies = new List<TimeSeriesPolicy>
                            {
                                new TimeSeriesPolicy("By30DaysFor5Years", TimeValue.FromDays(30), TimeValue.FromYears(5)),
                            }
                        }
                    }
                };

                var ex = await Assert.ThrowsAsync<RavenException>(() => store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config)));
                Assert.Contains("month might have different number of days", ex.Message);


                config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromMonths(12)),
                            Policies = new List<TimeSeriesPolicy>
                            {
                                new TimeSeriesPolicy("By365DaysFor5Years", TimeValue.FromSeconds(365 * 24 * 3600), TimeValue.FromYears(5)),
                            }
                        }
                    }
                };

                ex = await Assert.ThrowsAsync<RavenException>(() => store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config)));
                Assert.Contains("month might have different number of days", ex.Message);


                config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromMonths(1)),
                            Policies = new List<TimeSeriesPolicy>
                            {
                                new TimeSeriesPolicy("By27DaysFor1Year", TimeValue.FromDays(27), TimeValue.FromYears(1)),
                                new TimeSeriesPolicy("By364DaysFor5Years", TimeValue.FromDays(364), TimeValue.FromYears(5)),
                            }
                        }
                    }
                };

                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));
            }
        }

        [Fact]
        public async Task CanExecuteRollupInTheCluster()
        {
            var cluster = await CreateRaftCluster(3);
            using (var store = GetDocumentStore(new Options
            {
                Server = cluster.Leader,
                ReplicationFactor = 3
            }))
            {
                var p1 = new TimeSeriesPolicy("BySecond",TimeValue.FromSeconds(1));
                var p2 = new TimeSeriesPolicy("By10Seconds",TimeValue.FromSeconds(10));
                var p3 = new TimeSeriesPolicy("ByMinute",TimeValue.FromMinutes(1));
                var p4 = new TimeSeriesPolicy("By5Minutes",TimeValue.FromMinutes(5));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1,p2,p3,p4
                            },
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromHours(96))
                        },
                    },
                    PolicyCheckFrequency = TimeSpan.FromSeconds(1)
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var baseline = DateTime.Today.AddDays(-1);

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddSeconds(0.5 * i), new[] {29d * i}, "watches/fitbit");
                    }

                    session.SaveChanges();
                }

                await Task.Delay(config.PolicyCheckFrequency * 3);

                using (var session = store.OpenSession())
                {
                    session.Advanced.WaitForReplicationAfterSaveChanges(replicas: 2);
                    session.Store(new User {Name = "Karmel"}, "foo/bar");
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(100, ts.Count);

                    var ts1 = session.TimeSeriesFor("users/karmel", p1.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(50, ts1.Count);

                    var ts2 = session.TimeSeriesFor("users/karmel", p2.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(5, ts2.Count);

                    var ts3 = session.TimeSeriesFor("users/karmel", p3.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(1, ts3.Count);

                    var ts4 = session.TimeSeriesFor("users/karmel", p4.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(1, ts4.Count);
                }
            }
        }


        [Fact]
        public async Task CanExecuteSimpleRollup()
        {
            using (var store = GetDocumentStore())
            {
                var p1 = new TimeSeriesPolicy("BySecond",TimeValue.FromSeconds(1));
                var p2 = new TimeSeriesPolicy("By2Seconds",TimeValue.FromSeconds(2));
                var p3 = new TimeSeriesPolicy("By3Seconds",TimeValue.FromSeconds(3));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1,p2,p3
                            }
                        },
                    }
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var baseline = DateTime.Today.AddDays(-1);

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddSeconds(0.3 * i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.RunRollups();

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    var tsSeconds = (int)(ts.Last().Timestamp - ts.First().Timestamp).TotalSeconds;

                    var ts1 = session.TimeSeriesFor("users/karmel", p1.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    var ts1Seconds = (int)(ts1.Last().Timestamp - ts1.First().Timestamp).TotalSeconds;
                    Assert.Equal(ts1Seconds, tsSeconds);

                    var ts2 = session.TimeSeriesFor("users/karmel", p2.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(ts1.Count / 2, ts2.Count);

                    var ts3 = session.TimeSeriesFor("users/karmel", p3.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(ts1.Count / 3, ts3.Count);
                }
            }
        }

        [Fact]
        public async Task CanExecuteRawRetention()
        {
            using (var store = GetDocumentStore())
            {
                var retention = TimeValue.FromHours(96);
                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromHours(96))
                        },
                    }
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var baseline = DateTime.UtcNow.Add(-retention * 2);

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddHours(i), 29 * i, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.DoRetention();

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(3, ts.Count);
                }
            }
        }

        [Fact]
        public async Task CanReExecuteRollupWhenOldValuesChanged()
        {
            using (var store = GetDocumentStore())
            {
                var p1 = new TimeSeriesPolicy("BySecond",TimeValue.FromSeconds(1));
                var p2 = new TimeSeriesPolicy("By2Seconds",TimeValue.FromSeconds(2));
                var p3 = new TimeSeriesPolicy("By3Seconds",TimeValue.FromSeconds(3));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1,p2,p3
                            },
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromHours(96))
                        },
                    }
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var baseline = DateTime.Today.AddDays(-1);

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddSeconds(0.2 * i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.RunRollups();

                using (var session = store.OpenSession())
                {
                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddSeconds(0.2 * i + 0.1), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                await database.TimeSeriesPolicyRunner.RunRollups();

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(200, ts.Count);

                    var ts1 = session.TimeSeriesFor("users/karmel", p1.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(20, ts1.Count);

                    var ts2 = session.TimeSeriesFor("users/karmel", p2.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(10, ts2.Count);

                    var ts3 = session.TimeSeriesFor("users/karmel", p3.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(7, ts3.Count);
                }
            }
        }

        [Fact]
        public async Task RemoveConfigurationWillKeepData()
        {
            using (var store = GetDocumentStore())
            {
                var baseline = DateTime.Today.AddDays(-1);

                var p1 = new TimeSeriesPolicy("BySecond",TimeValue.FromSeconds(1));
                var p2 = new TimeSeriesPolicy("By2Seconds",TimeValue.FromSeconds(2));
                var p3 = new TimeSeriesPolicy("By3Seconds",TimeValue.FromSeconds(3));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1, p2 ,p3
                            },
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromHours(96))
                        },
                    }
                };
                
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddSeconds(0.2 * i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.HandleChanges();
                await database.TimeSeriesPolicyRunner.RunRollups();

                config.Collections["Users"].Policies.Remove(p3);
                config.Collections["Users"].Policies.Remove(p2);
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                await database.TimeSeriesPolicyRunner.HandleChanges();
                await database.TimeSeriesPolicyRunner.RunRollups();

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(100, ts.Count);

                    var ts1 = session.TimeSeriesFor("users/karmel", p1.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(20, ts1.Count);

                    var ts2 = session.TimeSeriesFor("users/karmel", p2.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(10, ts2.Count);

                    var ts3 = session.TimeSeriesFor("users/karmel", p3.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(7, ts3.Count);
                }

                using (var session = store.OpenSession())
                {
                    for (int i = 100; i < 200; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append( baseline.AddSeconds(0.2 * i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                await database.TimeSeriesPolicyRunner.RunRollups();

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(200, ts.Count);

                    var ts1 = session.TimeSeriesFor("users/karmel", p1.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(40, ts1.Count);

                    var ts2 = session.TimeSeriesFor("users/karmel", p2.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(10, ts2.Count);

                    var ts3 = session.TimeSeriesFor("users/karmel", p3.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(7, ts3.Count);
                }

            }
        }

        [Fact]
        public async Task CanRemoveConfigurationEntirely()
        {
            using (var store = GetDocumentStore())
            {
                var baseline = DateTime.Today.AddDays(-1);

                var p1 = new TimeSeriesPolicy("BySecond",TimeValue.FromSeconds(1));
                var p2 = new TimeSeriesPolicy("By2Seconds",TimeValue.FromSeconds(2));
                var p3 = new TimeSeriesPolicy("By3Seconds",TimeValue.FromSeconds(3));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1, p2 ,p3
                            },
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromHours(96))
                        },
                    }
                };
                
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddSeconds(0.2 * i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.HandleChanges();
                await database.TimeSeriesPolicyRunner.RunRollups();

                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(null));

                Assert.True(await WaitForValueAsync(() => database.TimeSeriesPolicyRunner == null, true));


                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(100, ts.Count);

                    var ts1 = session.TimeSeriesFor("users/karmel", p1.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(20, ts1.Count);

                    var ts2 = session.TimeSeriesFor("users/karmel", p2.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(10, ts2.Count);

                    var ts3 = session.TimeSeriesFor("users/karmel", p3.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(7, ts3.Count);
                }
            }
        }

        [Fact]
        public async Task CanAddConfiguration()
        {
            using (var store = GetDocumentStore())
            {
                var baseline = DateTime.Today.AddDays(-1);

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i < 100; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddSeconds(0.2 * i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var p1 = new TimeSeriesPolicy("BySecond",TimeValue.FromSeconds(1));
                var p2 = new TimeSeriesPolicy("By2Seconds",TimeValue.FromSeconds(2));
                var p3 = new TimeSeriesPolicy("By3Seconds",TimeValue.FromSeconds(3));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1, p2 ,p3
                            },
                            RawPolicy = new RawTimeSeriesPolicy(TimeValue.FromHours(96))
                        },
                    }
                };
                
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));
                var database = await GetDocumentDatabaseInstanceFor(store);

                await database.TimeSeriesPolicyRunner.HandleChanges();
                await database.TimeSeriesPolicyRunner.RunRollups();

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(100, ts.Count);

                    var ts1 = session.TimeSeriesFor("users/karmel", p1.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(20, ts1.Count);

                    var ts2 = session.TimeSeriesFor("users/karmel", p2.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(10, ts2.Count);

                    var ts3 = session.TimeSeriesFor("users/karmel", p3.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(7, ts3.Count);
                }
            }
        }

        [Fact]
        public async Task CanRetainAndRollup()
        {
            using (var store = GetDocumentStore())
            {
                var now = DateTime.UtcNow;
                var baseline = now.AddMinutes(-120);

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");
                    for (int i = 0; i <= 120; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddMinutes(i), new[] {29d * i, 30 * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var raw = new RawTimeSeriesPolicy(TimeValue.FromMinutes(30));
                var p = new TimeSeriesPolicy("By10Minutes",TimeValue.FromMinutes(10), TimeValue.FromHours(1));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = raw,
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p
                            }
                        },
                    }
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));
                
                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.HandleChanges();
                await database.TimeSeriesPolicyRunner.RunRollups();
                await database.TimeSeriesPolicyRunner.DoRetention();
                
                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(30, ts.Count);
                    var ts2 = session.TimeSeriesFor("users/karmel", p.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(((TimeSpan)p.RetentionTime).TotalMinutes / ((TimeSpan)p.AggregationTime).TotalMinutes, ts2.Count);
                }
            }
        }

        [Fact]
        public async Task CanRetainAndRollupForMonths()
        {
            using (var store = GetDocumentStore())
            {
                var now = DateTime.UtcNow;
                var baseline = now.AddMonths(-48);

                var totalDays = 365 * 4 + 1; // true for this century

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");
                    for (int i = 0; i <= 24 * totalDays; i+=3) // appr. 10,000 items
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddHours(i), new[] {29d * i, 30 * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                var raw = new RawTimeSeriesPolicy(TimeValue.FromDays(120));
                var p = new TimeSeriesPolicy("ByQuarterFor3Years",TimeValue.FromMonths(3), TimeValue.FromYears(3));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = raw,
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p
                            }
                        },
                    }
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));
                
                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.HandleChanges();
                await database.TimeSeriesPolicyRunner.RunRollups();
                await database.TimeSeriesPolicyRunner.DoRetention();

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(960, ts.Count);
                    var ts2 = session.TimeSeriesFor("users/karmel", p.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(12, ts2.Count);
                }
            }
        }

        [Fact]
        public async Task CanRecordAndReplay()
        {
            var recordFilePath = NewDataPath();

            var raw = new RawTimeSeriesPolicy(TimeValue.FromMinutes(30));
            var p = new TimeSeriesPolicy("By10Minutes",TimeValue.FromMinutes(10), TimeValue.FromHours(1));
            var config = new TimeSeriesConfiguration
            {
                Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                {
                    ["Users"] = new TimeSeriesCollectionConfiguration {RawPolicy = raw, Policies = new List<TimeSeriesPolicy> {p}},
                }
            };

            int count1, count2;
            using (var store = GetDocumentStore())
            {
                var now = DateTime.UtcNow;
                var baseline = now.AddHours(-2);

                store.Maintenance.Send(new StartTransactionsRecordingOperation(recordFilePath));

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");
                    for (int i = 0; i < 120; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddMinutes(i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }
               
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.HandleChanges();
                await database.TimeSeriesPolicyRunner.RunRollups();
                await database.TimeSeriesPolicyRunner.DoRetention();

                store.Maintenance.Send(new StopTransactionsRecordingOperation());


                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    count1 = ts.Count;

                    ts = session.TimeSeriesFor("users/karmel", p.GetTimeSeriesName("Heartrate")).Get( DateTime.MinValue, DateTime.MaxValue).ToList();
                    count2 = ts.Count;
                }
            }

            using (var store = GetDocumentStore())
            using (var replayStream = new FileStream(recordFilePath, FileMode.Open))
            {
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var command = new GetNextOperationIdCommand();
                store.Commands().Execute(command);
                store.Maintenance.Send(new ReplayTransactionsRecordingOperation(replayStream, command.Result));

                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(count1, ts.Count);
                    ts = session.TimeSeriesFor("users/karmel", p.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(count2, ts.Count);

                }
            }
        }

        [Fact]
        public async Task FullRetentionAndRollup()
        {
            using (var store = GetDocumentStore())
            {
                var raw = new RawTimeSeriesPolicy(TimeValue.FromHours(24));

                var p1 = new TimeSeriesPolicy("By6Hours",TimeValue.FromHours(6), raw.RetentionTime * 4);
                var p2 = new TimeSeriesPolicy("By1Day",TimeValue.FromDays(1), raw.RetentionTime * 5);
                var p3 = new TimeSeriesPolicy("By30Minutes",TimeValue.FromMinutes(30), raw.RetentionTime * 2);
                var p4 = new TimeSeriesPolicy("By1Hour",TimeValue.FromMinutes(60), raw.RetentionTime * 3);

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = raw,
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1,p2,p3,p4
                            }
                        },
                    },
                    PolicyCheckFrequency = TimeSpan.FromSeconds(1)
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var now = DateTime.UtcNow;
                var baseline = now.AddDays(-12);
                var total = ((TimeSpan)TimeValue.FromDays(12)).TotalMinutes;

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");
                    for (int i = 0; i <= total; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddMinutes(i), new[] {29d * i, i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }
                
                WaitForUserToContinueTheTest(store);

                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.RunRollups();
                await database.TimeSeriesPolicyRunner.DoRetention();

                await VerifyFullPolicyExecution(store, config.Collections["Users"]);
            }
        }

        [NightlyBuildFact]
        public async Task RapidRetentionAndRollupInACluster()
        {
            var cluster = await CreateRaftCluster(3);
            using (var store = GetDocumentStore(new Options
            {
                Server = cluster.Leader,
                ReplicationFactor = 3
            }))
            {
                var raw = new RawTimeSeriesPolicy(TimeValue.FromSeconds(15));

                var p1 = new TimeSeriesPolicy("By1",TimeValue.FromSeconds(1), raw.RetentionTime * 2);
                var p2 = new TimeSeriesPolicy("By2",TimeValue.FromSeconds(2), raw.RetentionTime * 3);
                var p3 = new TimeSeriesPolicy("By3",TimeValue.FromSeconds(3), raw.RetentionTime * 4);
                var p4 = new TimeSeriesPolicy("By4",TimeValue.FromSeconds(4), raw.RetentionTime * 5);

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = raw,
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1,p2,p3,p4
                            }
                        },
                    },
                    PolicyCheckFrequency = TimeSpan.FromSeconds(1)
                };

                var now = DateTime.UtcNow;
                var baseline = now.AddSeconds(-15 * 3);
                var total = ((TimeSpan)TimeValue.FromSeconds(15 * 3)).TotalMilliseconds;

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i <= total; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddMilliseconds(i), new[] {29d * i, i}, "watches/fitbit");
                    }
                    session.SaveChanges();

                    session.Store(new User {Name = "Karmel"}, "marker");
                    session.SaveChanges();

                    await WaitForDocumentInClusterAsync<User>((DocumentSession)session, "marker", null, TimeSpan.FromSeconds(15));
                }

                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));
                await Task.Delay((TimeSpan)(p4.RetentionTime + TimeValue.FromSeconds(10)));
                // nothing should be left

                WaitForUserToContinueTheTest(store);

                foreach (var node in cluster.Nodes)
                {
                    using (var nodeStore = GetDocumentStore(new Options
                    {
                        Server = node,
                        CreateDatabase =  false,
                        DeleteDatabaseOnDispose = false,
                        ModifyDocumentStore = s => s.Conventions = new DocumentConventions
                        {
                            DisableTopologyUpdates = true
                        },
                        ModifyDatabaseName = _ => store.Database
                    }))
                    {
                        using (var session = nodeStore.OpenSession())
                        {
                            var user = session.Load<User>("users/karmel");
                            Assert.Equal(0,session.Advanced.GetTimeSeriesFor(user)?.Count ?? 0);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task RapidRetentionAndRollup()
        {
            using (var store = GetDocumentStore())
            {
                var raw = new RawTimeSeriesPolicy(TimeValue.FromSeconds(15));

                var p1 = new TimeSeriesPolicy("By1",TimeValue.FromSeconds(1), raw.RetentionTime * 2);
                var p2 = new TimeSeriesPolicy("By2",TimeValue.FromSeconds(2), raw.RetentionTime * 3);
                var p3 = new TimeSeriesPolicy("By3",TimeValue.FromSeconds(3), raw.RetentionTime * 4);
                var p4 = new TimeSeriesPolicy("By4",TimeValue.FromSeconds(4), raw.RetentionTime * 5);

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = raw,
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1,p2,p3,p4
                            }
                        },
                    },
                    PolicyCheckFrequency = TimeSpan.FromSeconds(1)
                };

                var now = DateTime.UtcNow;
                var baseline = now.AddSeconds(-15 * 3);
                var total = ((TimeSpan)TimeValue.FromSeconds(15 * 3)).TotalMilliseconds;

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i <= total; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddMilliseconds(i), new[] {29d * i, i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }

                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));
                WaitForUserToContinueTheTest(store);

                await Task.Delay((TimeSpan)(p4.RetentionTime + TimeValue.FromSeconds(10)));
                // nothing should be left

                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>("users/karmel");
                    Assert.Equal(0,session.Advanced.GetTimeSeriesFor(user)?.Count ?? 0);
                }
            }
        }

        [Fact]
        public async Task FullRetentionAndRollupInACluster()
        {
            var cluster = await CreateRaftCluster(3, watcherCluster: true);
            using (var store = GetDocumentStore(new Options
            {
                Server = cluster.Leader,
                ReplicationFactor = 3
            }))
            {
                var raw = new RawTimeSeriesPolicy(TimeValue.FromHours(24));

                var p1 = new TimeSeriesPolicy("By6Hours",TimeValue.FromHours(6), raw.RetentionTime * 4);
                var p2 = new TimeSeriesPolicy("By1Day",TimeValue.FromDays(1), raw.RetentionTime * 5);
                var p3 = new TimeSeriesPolicy("By30Minutes",TimeValue.FromMinutes(30), raw.RetentionTime * 2);
                var p4 = new TimeSeriesPolicy("By1Hour",TimeValue.FromMinutes(60), raw.RetentionTime * 3);

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            RawPolicy = raw,
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p1
                                ,p2,p3,p4
                            }
                        },
                    },
                    PolicyCheckFrequency = TimeSpan.FromSeconds(5)
                };

                var now = DateTime.UtcNow;
                var baseline = now.AddDays(-12);
                var total = ((TimeSpan)TimeValue.FromDays(12)).TotalMinutes;

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");

                    for (int i = 0; i <= total; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddMinutes(i), new[] {29d * i, i}, "watches/fitbit");
                    }
                    session.SaveChanges();

                    session.Store(new User {Name = "Karmel"}, "marker");
                    session.SaveChanges();

                    await WaitForDocumentInClusterAsync<User>((DocumentSession)session, "marker", null, TimeSpan.FromSeconds(15));
                }

                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                await Task.Delay(config.PolicyCheckFrequency * 3);
                WaitForUserToContinueTheTest(store);

                foreach (var node in cluster.Nodes)
                {
                    using (var nodeStore = GetDocumentStore(new Options
                    {
                        Server = node,
                        CreateDatabase =  false,
                        DeleteDatabaseOnDispose = false,
                        ModifyDocumentStore = s => s.Conventions = new DocumentConventions
                        {
                            DisableTopologyUpdates = true
                        },
                        ModifyDatabaseName = _ => store.Database
                    }))
                    {
                       await VerifyFullPolicyExecution(nodeStore, config.Collections["Users"]); 
                    }
                }
            }
        }

        private async Task VerifyFullPolicyExecution(DocumentStore store, TimeSeriesCollectionConfiguration configuration)
        {
            var raw = configuration.RawPolicy;
            configuration.ValidateAndInitialize();

            await WaitForValueAsync(() =>
            {
                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel","Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(((TimeSpan)raw.RetentionTime).TotalMinutes, ts.Count);

                    foreach (var policy in configuration.Policies)
                    {
                        ts = session.TimeSeriesFor("users/karmel",policy.GetTimeSeriesName("Heartrate")).Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                        Assert.Equal(((TimeSpan)policy.RetentionTime).TotalMinutes / ((TimeSpan)policy.AggregationTime).TotalMinutes, ts.Count);
                    }
                }
                return true;
            }, true);
        }

        [Fact]
        public async Task RollupLargeTime()
        {
            using (var store = GetDocumentStore())
            {

                var p = new TimeSeriesPolicy("ByDay", TimeValue.FromDays(1));

                var config = new TimeSeriesConfiguration
                {
                    Collections = new Dictionary<string, TimeSeriesCollectionConfiguration>
                    {
                        ["Users"] = new TimeSeriesCollectionConfiguration
                        {
                            Policies = new List<TimeSeriesPolicy>
                            {
                                p
                            }
                        },
                    }
                };
                await store.Maintenance.SendAsync(new ConfigureTimeSeriesOperation(config));

                var baseline = DateTime.UtcNow.AddDays(-12);
                var total = ((TimeSpan)TimeValue.FromDays(12)).TotalHours;

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Karmel"}, "users/karmel");
                    for (int i = 0; i < total; i++)
                    {
                        session.TimeSeriesFor("users/karmel", "Heartrate")
                            .Append(baseline.AddHours(i), new[] {29d * i}, "watches/fitbit");
                    }
                    session.SaveChanges();
                }
                
                var database = await GetDocumentDatabaseInstanceFor(store);
                await database.TimeSeriesPolicyRunner.RunRollups();
                await database.TimeSeriesPolicyRunner.DoRetention();
                
                using (var session = store.OpenSession())
                {
                    var ts = session.TimeSeriesFor("users/karmel", "Heartrate").Get(DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(288, ts.Count);

                    ts = session.TimeSeriesFor("users/karmel", p.GetTimeSeriesName("Heartrate")).Get( DateTime.MinValue, DateTime.MaxValue).ToList();
                    Assert.Equal(12, ts.Count);
                }
            }
        }
    }
}
