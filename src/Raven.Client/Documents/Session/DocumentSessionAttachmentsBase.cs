//-----------------------------------------------------------------------
// <copyright file="DocumentSessionAttachmentsBase.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Operations.Attachments;
using Raven.Client.Json.Converters;
using Sparrow.Json;

namespace Raven.Client.Documents.Session
{
    /// <summary>
    /// Abstract implementation for in memory session operations
    /// </summary>
    public abstract class DocumentSessionAttachmentsBase : AdvancedSessionExtentionBase
    {
        protected DocumentSessionAttachmentsBase(InMemoryDocumentSessionOperations session) : base(session)
        {
        }

        public AttachmentName[] GetNames(object entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (DocumentsByEntity.TryGetValue(entity, out DocumentInfo document) == false)
                ThrowEntityNotInSession(entity);

            if (document.Metadata.TryGet(Constants.Documents.Metadata.Attachments, out BlittableJsonReaderArray attachments) == false)
                return Array.Empty<AttachmentName>();

            var results = new AttachmentName[attachments.Length];
            for (var i = 0; i < attachments.Length; i++)
            {
                var attachment = (BlittableJsonReaderObject)attachments[i];
                results[i] = JsonDeserializationClient.AttachmentName(attachment);
            }
            return results;
        }

        public void Store(string documentId, string name, Stream stream, string contentType = null)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.DELETE, null)))
                ThrowException(documentId, name, "store", "delete");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentPUT, name)))
                ThrowException(documentId, name, "store", "create");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentDELETE, name)))
                ThrowException(documentId, name, "store", "delete");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentRENAME, name)))
                ThrowException(documentId, name, "store", "rename");

            if (DocumentsById.TryGetValue(documentId, out DocumentInfo documentInfo) &&
                DeletedEntities.Contains(documentInfo.Entity))
                throw new InvalidOperationException($"Can't store attachment {name} of document {documentId}, the document was already deleted in this session.");

            Defer(new PutAttachmentCommandData(documentId, name, stream, contentType, null));
        }

        public void Store(object entity, string name, Stream stream, string contentType = null)
        {
            if (DocumentsByEntity.TryGetValue(entity, out DocumentInfo document) == false)
                ThrowEntityNotInSession(entity);

            Store(document.Id, name, stream, contentType);
        }

        protected void ThrowEntityNotInSession(object entity)
        {
            throw new ArgumentException($"{entity} is not associated with the session. Use documentId instead or track the entity in the session.", nameof(entity));
        }

        public void Delete(object entity, string name)
        {
            if (DocumentsByEntity.TryGetValue(entity, out DocumentInfo document) == false)
                ThrowEntityNotInSession(entity);

            Delete(document.Id, name);
        }

        public void Delete(string documentId, string name)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.DELETE, null)) ||
                DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentDELETE, name)))
                return; // no-op

            if (DocumentsById.TryGetValue(documentId, out DocumentInfo documentInfo) &&
                DeletedEntities.Contains(documentInfo.Entity))
                return; // no-op

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentPUT, name)))
                ThrowException(documentId, name, "delete", "create");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentRENAME, name)))
                ThrowException(documentId, name, "delete", "rename");

            Defer(new DeleteAttachmentCommandData(documentId, name, null));
        }

        public void Rename(string documentId, string name, string newName)
        {
            if (string.IsNullOrWhiteSpace(documentId))
                throw new ArgumentNullException(nameof(documentId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentNullException(nameof(newName));

            if (name == newName)
                return; // no-op

            if (DocumentsById.TryGetValue(documentId, out DocumentInfo documentInfo) && DeletedEntities.Contains(documentInfo.Entity))
                throw new InvalidOperationException($"Can't rename attachment {name} of document {documentId}, the document was already deleted in this session.");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentDELETE, name)))
                ThrowException(documentId, name, "rename", "delete");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentRENAME, name)))
                ThrowException(documentId, name, "rename", "rename");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentDELETE, newName)))
                ThrowException(documentId, newName, "rename", "delete");

            if (DeferredCommandsDictionary.ContainsKey((documentId, CommandType.AttachmentRENAME, newName)))
                ThrowException(documentId, newName, "rename", "rename");

            Defer(new RenameAttachmentCommandData(documentId, name, newName, null));
        }

        public void Rename(object entity, string name, string newName)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            if (DocumentsByEntity.TryGetValue(entity, out DocumentInfo document) == false)
                ThrowEntityNotInSession(entity);

            Rename(document.Id, name, newName);
        }

        private static void ThrowException(string documentId, string name, string operation, string previousOperation)
        {
            throw new InvalidOperationException($"Can't {operation} attachment '{name}' of document '{documentId}', there is a deferred command registered to {previousOperation} an attachment with '{name}' name.");
        }
    }
}
