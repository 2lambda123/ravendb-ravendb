//-----------------------------------------------------------------------
// <copyright file="ITransactionStorageActions.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Raven.Http;

namespace Raven.Database.Storage
{
    public interface ITransactionStorageActions
    {
        Guid AddDocumentInTransaction(string key, Guid? etag, JObject data, JObject metadata, TransactionInformation transactionInformation);
        void DeleteDocumentInTransaction(TransactionInformation transactionInformation, string key, Guid? etag);
        void RollbackTransaction(Guid txId);
        void ModifyTransactionId(Guid fromTxId, Guid toTxId, TimeSpan timeout);
        void CompleteTransaction(Guid txId, Action<DocumentInTransactionData> perDocumentModified);
        IEnumerable<Guid> GetTransactionIds();
    }
}