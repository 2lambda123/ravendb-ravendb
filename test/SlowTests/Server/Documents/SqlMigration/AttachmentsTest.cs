﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Raven.Client.Documents.Operations;
using Raven.Server.ServerWide.Context;
using Raven.Server.SqlMigration;
using Raven.Server.SqlMigration.Model;
using Sparrow.Json.Parsing;
using Xunit;

namespace SlowTests.Server.Documents.SqlMigration
{
    public class AttachmentsTest : SqlAwareTestBase
    {
        [Theory]
        [InlineData(MigrationProvider.MsSQL)]
        [InlineData(MigrationProvider.MySQL)]
        public async Task Attachments(MigrationProvider provider)
        {
            using (WithSqlDatabase(provider, out var connectionString, out string schemaName, "basic"))
            {
                var driver = DatabaseDriverDispatcher.CreateDriver(provider, connectionString);
                using (var store = GetDocumentStore())
                {
                    var db = await GetDocumentDatabaseInstanceFor(store);

                    var settings = new MigrationSettings
                    {
                        BinaryToAttachment = true,
                        Collections = new List<RootCollection>
                        {
                            new RootCollection(schemaName, "actor", "Actors")
                            {
                                NestedCollections = new List<EmbeddedCollection>
                                {
                                    new EmbeddedCollection(schemaName, "actor_movie", RelationType.OneToMany, new List<string> { "a_id"}, "Movies")
                                    {
                                        NestedCollections = new List<EmbeddedCollection>
                                        {
                                            new EmbeddedCollection(schemaName, "movie", RelationType.ManyToOne, new List<string> { "m_id" }, "Movie")
                                        }
                                    }
                                }
                            }
                        }
                    };

                    using (db.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                    {
                        var schema = driver.FindSchema();
                        ApplyDefaultColumnNamesMapping(schema, settings);
                        await driver.Migrate(settings, schema, db, context);
                    }
                    
                    using (var session  = store.OpenSession())
                    {
                        var actor32 = session.Load<JObject>("Actors/32");
                        var attachments = session.Advanced.Attachments.GetNames(actor32)
                            .Select(x => x.Name)
                            .OrderBy(x => x)
                            .ToArray();
                        
                        Assert.Equal(new string[] { "Movies_0_Movie_file", "Movies_1_Movie_file", "photo" }, attachments);
                        
                        var actor34 = session.Load<JObject>("Actors/34");
                        Assert.Equal(0, session.Advanced.Attachments.GetNames(actor34).Length);
                    }
                }
            }
        }
        
        [Theory]
        [InlineData(MigrationProvider.MsSQL)]
        [InlineData(MigrationProvider.MySQL)]
        public async Task BinaryAsNoAttachment(MigrationProvider provider)
        {
            using (WithSqlDatabase(provider, out var connectionString, out string schemaName, "basic"))
            {
                var driver = DatabaseDriverDispatcher.CreateDriver(provider, connectionString);
                using (var store = GetDocumentStore())
                {
                    var db = await GetDocumentDatabaseInstanceFor(store);

                    var settings = new MigrationSettings
                    {
                        BinaryToAttachment = false,
                        Collections = new List<RootCollection>
                        {
                            new RootCollection(schemaName, "actor", "Actors")
                            {
                                NestedCollections = new List<EmbeddedCollection>
                                {
                                    new EmbeddedCollection(schemaName, "actor_movie", RelationType.OneToMany, new List<string> { "a_id" }, "Movies")
                                    {
                                        NestedCollections = new List<EmbeddedCollection>
                                        {
                                            new EmbeddedCollection(schemaName, "movie", RelationType.ManyToOne, new List<string> { "m_id" }, "Movie")
                                        }
                                    }
                                }
                            }
                        }
                    };

                    using (db.DocumentsStorage.ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
                    {
                        var schema = driver.FindSchema();
                        ApplyDefaultColumnNamesMapping(schema, settings);
                        await driver.Migrate(settings, schema, db, context);
                    }
                    
                    using (var session  = store.OpenSession())
                    {
                        var actor32 = session.Load<JObject>("Actors/32");
                        Assert.Equal(0, session.Advanced.Attachments.GetNames(actor32).Length);
                        Assert.Equal("MzI=", actor32["Photo"]);
                        Assert.Equal("MjE=", actor32["Movies"][0]["Movie"]["File"]);
                        Assert.Equal("MjM=", actor32["Movies"][1]["Movie"]["File"]);
                        Assert.Equal(JTokenType.Null, actor32["Movies"][2]["Movie"]["File"].Type);
                        
                        var actor34 = session.Load<JObject>("Actors/34");
                        Assert.Equal(0, session.Advanced.Attachments.GetNames(actor34).Length);
                        Assert.Equal(JTokenType.Null, actor34["Photo"].Type);
                    }
                }
            }
        }
    }
}
