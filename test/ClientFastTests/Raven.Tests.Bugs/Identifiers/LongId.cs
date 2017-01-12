﻿using FastTests;
using Xunit;

namespace NewClientTests.NewClient.Raven.Tests.Bugs.Identifiers
{
    public class LongId : RavenNewTestBase
    {
        public class Entity
        {
            public long Id { get; set; }
        }

        [Fact]
        public void Can_load_entity()
        {
            using (var store = GetDocumentStore())
            {
                object id;
                using (var session = store.OpenSession())
                {
                    var entity = new Entity();
                    session.Store(entity);
                    id = entity.Id;
                    session.SaveChanges();
                }

                using (var session = store.OpenSession())
                {
                    var entity1 = session.Load<Entity>("entities/" + id);
                    Assert.NotNull(entity1);
                }
            }
        }
    }
}
