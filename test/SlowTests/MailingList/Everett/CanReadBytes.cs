﻿// -----------------------------------------------------------------------
//  <copyright file="CanReadBytes.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FastTests;
using Newtonsoft.Json;
using Raven.Client.Documents.Conventions;
using Raven.Client.Json.Serialization.NewtonsoftJson;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.MailingList.Everett
{
    public class CanReadBytes : RavenTestBase
    {
        public CanReadBytes(ITestOutputHelper output) : base(output)
        {
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void query_for_object_with_byte_array_with_TypeNameHandling_All(Options options)
        {
            options.ModifyDocumentStore = s =>
            {
                s.Conventions.Serialization = new NewtonsoftJsonSerializationConventions
                {
                    CustomizeJsonSerializer = serializer =>
                    {
                        serializer.TypeNameHandling = TypeNameHandling.All;
                    }
                };
            };

            using (var store = GetDocumentStore(options))
            {
                var json = GetResourceText("DocumentWithBytes.txt");
                var jsonSerializer = (JsonSerializer)DocumentConventions.Default.Serialization.CreateSerializer();
                var item = jsonSerializer.Deserialize<DesignResources>(new JsonTextReader(new StringReader(json)));

                using (var session = store.OpenSession())
                {
                    item.Id = "resources/123";
                    item.DesignId = "designs/123";
                    session.Store(item);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    session
                        .Query<DesignResources>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .Where(x => x.DesignId == "designs/123")
                        .ToList();
                }
            }
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void query_for_object_with_byte_array_with_default_TypeNameHandling(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var json = GetResourceText("DocumentWithBytes.txt");
                var jsonSerializer = (JsonSerializer)DocumentConventions.Default.Serialization.CreateSerializer();
                var item = jsonSerializer.Deserialize<DesignResources>(new JsonTextReader(new StringReader(json)));

                using (var session = store.OpenSession())
                {
                    item.Id = "resources/123";
                    item.DesignId = "designs/123";
                    session.Store(item);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    session
                        .Query<DesignResources>()
                        .Customize(x => x.WaitForNonStaleResults())
                        .Where(x => x.DesignId == "designs/123")
                        .ToList();
                }
            }
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void load_object_with_byte_array_with_TypeNameHandling_All(Options options)
        {
            options.ModifyDocumentStore = s =>
            {
                s.Conventions.Serialization = new NewtonsoftJsonSerializationConventions
                {
                    CustomizeJsonSerializer = serializer =>
                    {
                        serializer.TypeNameHandling = TypeNameHandling.All;
                    }
                };
            };

            using (var store = GetDocumentStore(options))
            {
                var json = GetResourceText("DocumentWithBytes.txt");
                var jsonSerializer = (JsonSerializer)DocumentConventions.Default.Serialization.CreateSerializer();
                var item = jsonSerializer.Deserialize<DesignResources>(new JsonTextReader(new StringReader(json)));

                using (var session = store.OpenSession())
                {
                    item.Id = "resources/123";
                    item.DesignId = "designs/123";
                    session.Store(item);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                    session.Load<DesignResources>("resources/123");
            }
        }

        [RavenTheory(RavenTestCategory.Querying)]
        [RavenData(SearchEngineMode = RavenSearchEngineMode.All, DatabaseMode = RavenDatabaseMode.All)]
        public void load_object_with_byte_array_with_default_TypeNameHandling(Options options)
        {
            using (var store = GetDocumentStore(options))
            {
                var json = GetResourceText("DocumentWithBytes.txt");
                var jsonSerializer = (JsonSerializer)DocumentConventions.Default.Serialization.CreateSerializer();
                var item = jsonSerializer.Deserialize<DesignResources>(new JsonTextReader(new StringReader(json)));

                using (var session = store.OpenSession())
                {
                    item.Id = "resources/123";
                    item.DesignId = "designs/123";
                    session.Store(item);
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    session.Load<DesignResources>("resources/123");
                }
            }
        }

        [Fact]
        public void FromText()
        {
            var json = GetResourceText("DocumentWithBytes.txt");
            var jsonSerializer = (JsonSerializer)DocumentConventions.Default.Serialization.CreateSerializer();
            var item = jsonSerializer.Deserialize<DesignResources>(new JsonTextReader(new StringReader(json)));
        }

        private static string GetResourceText(string name)
        {
            name = typeof(CanReadBytes).Namespace + "." + name;
            using (var stream = typeof(CanReadBytes).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                    throw new InvalidOperationException("Could not find the following resource: " + name);
                return new StreamReader(stream).ReadToEnd();
            }
        }

        private class DesignResources
        {
            private List<Resource> _entries = new List<Resource>();

            public string DesignId { get; set; }

            public virtual string Id { get; set; }

            public DateTime LastSavedDate { get; set; }

            public string LastSavedUser { get; set; }

            public Guid SourceId { get; set; }

            public List<Resource> Entries
            {
                get { return _entries; }
                set
                {
                    if (value == null)
                        return;

                    _entries = value;
                }
            }
        }

        private class Resource
        {
            public Guid Id { get; set; }
            public byte[] Data { get; set; }
        }
    }
}
