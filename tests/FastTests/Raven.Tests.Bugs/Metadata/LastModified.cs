﻿//-----------------------------------------------------------------------
// <copyright file="LastModifiedLocal.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using Raven.NewClient.Abstractions;
using Raven.Tests.Core.Utils.Entities;
using Xunit;

namespace NewClientTests.NewClient.Raven.Tests.Bugs.Metadata
{
    public class LastModified : RavenTestBase
    {
        [Fact(Skip = "GetNextRange Not Implemented")]
        public void CanAccessLastModifiedAsMetadata()
        {
            using (var store = GetDocumentStore())
            {
                DateTime before;
                DateTime after;

                using (var session = store.OpenSession())
                {
                    session.Store(new User());

                    before = SystemTime.UtcNow;
                    session.SaveChanges();
                    after = SystemTime.UtcNow;
                }

                using (var session = store.OpenSession())
                {
                    var user = session.Load<User>("users/1");
                    var lastModified = DateTimeOffset.Parse(session.Advanced.GetMetadataFor(user)["Raven-Last-Modified"]).UtcDateTime;
                    Assert.NotNull(lastModified);
                    Assert.InRange(lastModified, before, after);
                    //TODO
                    //Assert.Equal(DateTimeKind.Utc, lastModified.Kind);
                }

                WaitForUserToContinueTheTest(store);
            }
        }
    }
}
