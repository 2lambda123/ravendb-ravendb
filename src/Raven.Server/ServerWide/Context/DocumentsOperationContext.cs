﻿using Raven.Server.Documents;
using Raven.Server.Json;

namespace Raven.Server.ServerWide.Context
{
    public class DocumentsOperationContext : TransactionOperationContext<DocumentsTransaction>
    {
        private readonly DocumentDatabase _documentDatabase;

        public DocumentsOperationContext(UnmanagedBuffersPool pool, DocumentDatabase documentDatabase)
            : base(pool)
        {
            _documentDatabase = documentDatabase;
        }

        protected override DocumentsTransaction CreateReadTransaction()
        {
            return new DocumentsTransaction(this, _documentDatabase.DocumentsStorage.Environment.ReadTransaction(), _documentDatabase.TasksStorage, _documentDatabase.Notifications);
        }

        protected override DocumentsTransaction CreateWriteTransaction()
        {
            return new DocumentsTransaction(this, _documentDatabase.DocumentsStorage.Environment.WriteTransaction(), _documentDatabase.TasksStorage, _documentDatabase.Notifications);
        }
    }
}