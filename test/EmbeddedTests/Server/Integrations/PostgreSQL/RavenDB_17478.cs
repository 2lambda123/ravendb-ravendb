﻿#if NET8_0
using System;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using Xunit;

namespace EmbeddedTests.Server.Integrations.PostgreSQL
{
    public class RavenDB_17478 : PostgreSqlIntegrationTestBase
    {
        [Fact]
        public async Task ShouldThrowException_WithFirstPartOfQuery_SplittedBySemicolons_WhenGivenQueryContainsSemicolons()
        {
            const string query =
                @"declare function name(e) {
                    if (!e)
                        return null;
                    return e.FirstName + "" "" + e.LastName;
                }
                from Employees as e
                where id() in ('employees/2-A', 'employees/1-A'  )
                load e.ReportsTo as boss
                select { Name: name(e), Manager: name(boss) }";

            var firstQueryPart = query.Split(";").First();

            var expectedErrorMessage =
                "54001: Unhandled query (Are you using ; in your query? " +
                $"That is likely causing the postgres client to split the query and results in partial queries): {Environment.NewLine}" +
                $"{firstQueryPart}";

            using (var store = GetDocumentStore())
            {
                var pgException = await Assert.ThrowsAsync<PostgresException>(async () => await Act(store, query));

                Assert.Equal(expectedErrorMessage, pgException.Message);
            }
        }
    }
}
#endif
