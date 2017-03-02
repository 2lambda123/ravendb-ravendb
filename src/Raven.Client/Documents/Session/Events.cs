﻿using System;
using System.Collections.Generic;

namespace Raven.Client.Documents.Session
{
    public class BeforeStoreEventArgs : EventArgs
    {
        private IDictionary<string, object> _documentMetadata;

        public BeforeStoreEventArgs(InMemoryDocumentSessionOperations session, string documentId, object entity)
        {
            Session = session;
            DocumentId = documentId;
            Entity = entity;
        }

        public InMemoryDocumentSessionOperations Session { get; }

        public string DocumentId { get; }
        public object Entity { get; }

        public IDictionary<string, object> DocumentMetadata => _documentMetadata ?? (_documentMetadata = Session.GetMetadataFor(Entity));
    }

    public class AfterStoreEventArgs : EventArgs
    {
        private IDictionary<string, object> _documentMetadata;

        public AfterStoreEventArgs(InMemoryDocumentSessionOperations session, string documentId, object entity)
        {
            Session = session;
            DocumentId = documentId;
            Entity = entity;
        }

        public InMemoryDocumentSessionOperations Session { get; }

        public string DocumentId { get; }
        public object Entity { get; }

        public IDictionary<string, object> DocumentMetadata => _documentMetadata ?? (_documentMetadata = Session.GetMetadataFor(Entity));
    }

    public class BeforeDeleteEventArgs : EventArgs
    {
        private IDictionary<string, object> _documentMetadata;

        public BeforeDeleteEventArgs(InMemoryDocumentSessionOperations session, string documentId, object entity)
        {
            Session = session;
            DocumentId = documentId;
            Entity = entity;
        }

        public InMemoryDocumentSessionOperations Session { get; }

        public string DocumentId { get; }
        public object Entity { get; }

        public IDictionary<string, object> DocumentMetadata => _documentMetadata ?? (_documentMetadata = Session.GetMetadataFor(Entity));
    }

    public class BeforeQueryExecutedEventArgs : EventArgs
    {
        public BeforeQueryExecutedEventArgs(InMemoryDocumentSessionOperations session, IDocumentQueryCustomization queryCustomization)
        {
            Session = session;
            QueryCustomization = queryCustomization;
        }

        public InMemoryDocumentSessionOperations Session { get; }

        public IDocumentQueryCustomization QueryCustomization { get; }
    }
}