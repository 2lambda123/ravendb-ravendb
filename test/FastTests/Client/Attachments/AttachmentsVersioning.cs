﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FastTests.Server.Documents.Versioning;
using Raven.Client.Documents.Operations;
using Xunit;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Server.Documents;
using Raven.Server.ServerWide.Context;

namespace FastTests.Client.Attachments
{
    public class AttachmentsVersioning : RavenTestBase
    {
        [Fact]
        public async Task PutAttachments()
        {
            using (var store = GetDocumentStore())
            {
                await VersioningHelper.SetupVersioning(store, false, 4);

                using (var session = store.OpenSession())
                {
                    session.Store(new User {Name = "Fitzchak"}, "users/1");
                    session.SaveChanges();
                }

                var names = new[]
                {
                    "profile.png",
                    "background-photo.jpg",
                    "fileNAME_#$1^%_בעברית.txt"
                };
                using (var profileStream = new MemoryStream(new byte[] {1, 2, 3}))
                {
                    var result = store.Operations.Send(new PutAttachmentOperation("users/1", names[0], profileStream, "image/png"));
                    Assert.Equal(4, result.Etag);
                    Assert.Equal(names[0], result.Name);
                    Assert.Equal("users/1", result.DocumentId);
                    Assert.Equal("image/png", result.ContentType);
                    Assert.Equal("JCS/B3EIIB2gNVjsXTCD1aXlTgzuEz50", result.Hash);
                }
                using (var backgroundStream = new MemoryStream(new byte[] {10, 20, 30, 40, 50}))
                {
                    var result = store.Operations.Send(new PutAttachmentOperation("users/1", names[1], backgroundStream, "ImGgE/jPeG"));
                    Assert.Equal(8, result.Etag);
                    Assert.Equal(names[1], result.Name);
                    Assert.Equal("users/1", result.DocumentId);
                    Assert.Equal("ImGgE/jPeG", result.ContentType);
                    Assert.Equal("mpqSy7Ky+qPhkBwhLiiM2no82Wvo9gQw", result.Hash);
                }
                using (var fileStream = new MemoryStream(new byte[] {1, 2, 3, 4, 5}))
                {
                    var result = store.Operations.Send(new PutAttachmentOperation("users/1", names[2], fileStream, null));
                    Assert.Equal(13, result.Etag);
                    Assert.Equal(names[2], result.Name);
                    Assert.Equal("users/1", result.DocumentId);
                    Assert.Equal("", result.ContentType);
                    Assert.Equal("PN5EZXRY470m7BLxu9MsOi/WwIRIq4WN", result.Hash);
                }
                AssertRevisions(store, names, (session, revisions) =>
                {
                    AssertNoRevisionAttachment(revisions[0], session);
                    AssertRevisionAttachments(names, 1, revisions[1], session);
                    AssertRevisionAttachments(names, 2, revisions[2], session);
                    AssertRevisionAttachments(names, 3, revisions[3], session);
                });

                // Delete document should delete all the attachments
                store.Commands().Delete("users/1", null);
                AssertRevisions(store, names, (session, revisions) =>
                {
                    AssertNoRevisionAttachment(revisions[0], session);
                    AssertRevisionAttachments(names, 1, revisions[1], session);
                    AssertRevisionAttachments(names, 2, revisions[2], session);
                    AssertRevisionAttachments(names, 3, revisions[3], session);
                }, expectedCountOfDocuments: 1);

                // Create another revision which should delete old revision
                using (var session = store.OpenSession()) // This will delete the revision #1 which is without attachment
                {
                    session.Store(new User {Name = "Fitzchak 2"}, "users/1");
                    session.SaveChanges();
                }
                AssertRevisions(store, names, (session, revisions) =>
                {
                    AssertRevisionAttachments(names, 1, revisions[0], session);
                    AssertRevisionAttachments(names, 2, revisions[1], session);
                    AssertRevisionAttachments(names, 3, revisions[2], session);
                    AssertNoRevisionAttachment(revisions[3], session);
                });

                using (var session = store.OpenSession()) // This will delete the revision #2 which is with attachment
                {
                    session.Store(new User { Name = "Fitzchak 3" }, "users/1");
                    session.SaveChanges();
                }
                AssertRevisions(store, names, (session, revisions) =>
                {
                    AssertRevisionAttachments(names, 2, revisions[0], session);
                    AssertRevisionAttachments(names, 3, revisions[1], session);
                    AssertNoRevisionAttachment(revisions[2], session);
                    AssertNoRevisionAttachment(revisions[3], session);
                });

                using (var session = store.OpenSession()) // This will delete the revision #3 which is with attachment
                {
                    session.Store(new User { Name = "Fitzchak 4" }, "users/1");
                    session.SaveChanges();
                }
                AssertRevisions(store, names, (session, revisions) =>
                {
                    AssertRevisionAttachments(names, 3, revisions[0], session);
                    AssertNoRevisionAttachment(revisions[1], session);
                    AssertNoRevisionAttachment(revisions[2], session);
                    AssertNoRevisionAttachment(revisions[3], session);
                });

                using (var session = store.OpenSession()) // This will delete the revision #4 which is with attachment
                {
                    session.Store(new User { Name = "Fitzchak 5" }, "users/1");
                    session.SaveChanges();
                }
                AssertRevisions(store, names, (session, revisions) =>
                {
                    AssertNoRevisionAttachment(revisions[0], session);
                    AssertNoRevisionAttachment(revisions[1], session);
                    AssertNoRevisionAttachment(revisions[2], session);
                    AssertNoRevisionAttachment(revisions[3], session);
                }, expectedCountOfAttachments: 0);

                var database = GetDocumentDatabaseInstanceFor(store).Result;
                using (var context = DocumentsOperationContext.ShortTermSingleUse(database))
                using (context.OpenReadTransaction())
                {
                    database.DocumentsStorage.AttachmentsStorage.AssertNoAttachments(context);
                }
            }
        }

        private void AssertRevisions(DocumentStore store, string[] names, Action<IDocumentSession, List<User>> assertAction, long expectedCountOfDocuments = 2, long expectedCountOfAttachments = 3)
        {
            var statistics = store.Admin.Send(new GetStatisticsOperation());
            Assert.Equal(expectedCountOfAttachments, statistics.CountOfAttachments);
            Assert.Equal(4, statistics.CountOfRevisionDocuments.Value);
            Assert.Equal(expectedCountOfDocuments, statistics.CountOfDocuments);
            Assert.Equal(0, statistics.CountOfIndexes);

            using (var session = store.OpenSession())
            {
                var revisions = session.Advanced.GetRevisionsFor<User>("users/1");
                Assert.Equal(4, revisions.Count);
                assertAction(session, revisions);
            }
        }

        private void AssertNoRevisionAttachment(User revision, IDocumentSession session)
        {
            var metadata = session.Advanced.GetMetadataFor(revision);
            Assert.Equal((DocumentFlags.Versioned | DocumentFlags.Revision).ToString(), metadata[Constants.Documents.Metadata.Flags]);
            Assert.False(metadata.ContainsKey(Constants.Documents.Metadata.Attachments));
        }

        private void AssertRevisionAttachments(string[] names, int expectedCount, User revision, IDocumentSession session)
        {
            var metadata = session.Advanced.GetMetadataFor(revision);
            Assert.Equal((DocumentFlags.Versioned | DocumentFlags.Revision | DocumentFlags.HasAttachments).ToString(), metadata[Constants.Documents.Metadata.Flags]);
            var attachments = metadata.GetObjects(Constants.Documents.Metadata.Attachments);
            Assert.Equal(expectedCount, attachments.Length);

            var orderedNames = names.Take(expectedCount).OrderBy(x => x).ToArray();
            for (var i = 0; i < expectedCount; i++)
            {
                var name = orderedNames[i];
                var attachment = attachments[i];
                Assert.Equal(name, attachment.GetString(nameof(Attachment.Name)));
                var hash = attachment.GetString(nameof(AttachmentResult.Hash));
                if (name == names[1])
                {
                    Assert.Equal("mpqSy7Ky+qPhkBwhLiiM2no82Wvo9gQw", hash);
                }
                else if (name == names[2])
                {
                    Assert.Equal("PN5EZXRY470m7BLxu9MsOi/WwIRIq4WN", hash);
                }
                else if (name == names[0])
                {
                    Assert.Equal("JCS/B3EIIB2gNVjsXTCD1aXlTgzuEz50", hash);
                }
            }

            var changeVector = session.Advanced.GetChangeVectorFor(revision);
            var readBuffer = new byte[8];
            for (var i = 0; i < names.Length; i++)
            {
                var name = names[i];
                using (var attachmentStream = new MemoryStream(readBuffer))
                {
                    var attachment = session.Advanced.GetRevisionAttachment("users/1", name, changeVector, (result, stream) => stream.CopyTo(attachmentStream));
                    if (i >= expectedCount)
                    {
                        Assert.Null(attachment);
                        continue;
                    }

                    Assert.Equal(name, attachment.Name);
                    if (name == names[0])
                    {
                        if (expectedCount == 1)
                            Assert.Equal(7, attachment.Etag);
                        else if (expectedCount == 2)
                            Assert.Equal(12, attachment.Etag);
                        else if (expectedCount == 3)
                            Assert.Equal(18, attachment.Etag);
                        else
                            throw new ArgumentOutOfRangeException(nameof(i));
                        Assert.Equal(new byte[] {1, 2, 3}, readBuffer.Take(3));
                        Assert.Equal("image/png", attachment.ContentType);
                        Assert.Equal(3, attachmentStream.Position);
                        Assert.Equal("JCS/B3EIIB2gNVjsXTCD1aXlTgzuEz50", attachment.Hash);
                    }
                    else if (name == names[1])
                    {
                        if (expectedCount == 2)
                            Assert.Equal(11, attachment.Etag);
                        else if (expectedCount == 3)
                            Assert.Equal(16, attachment.Etag);
                        else
                            throw new ArgumentOutOfRangeException(nameof(i));
                        Assert.Equal(new byte[] {10, 20, 30, 40, 50}, readBuffer.Take(5));
                        Assert.Equal("ImGgE/jPeG", attachment.ContentType);
                        Assert.Equal(5, attachmentStream.Position);
                        Assert.Equal("mpqSy7Ky+qPhkBwhLiiM2no82Wvo9gQw", attachment.Hash);
                    }
                    else if (name == names[2])
                    {
                        if (expectedCount == 3)
                            Assert.Equal(17, attachment.Etag);
                        else
                            throw new ArgumentOutOfRangeException(nameof(i));
                        Assert.Equal(new byte[] {1, 2, 3, 4, 5}, readBuffer.Take(5));
                        Assert.Equal("", attachment.ContentType);
                        Assert.Equal(5, attachmentStream.Position);
                        Assert.Equal("PN5EZXRY470m7BLxu9MsOi/WwIRIq4WN", attachment.Hash);
                    }
                }
            }
        }

        private class User
        {
            public string Name { get; set; }
        }
    }
}