﻿// -----------------------------------------------------------------------
//  <copyright file="CounterHandler.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Raven.Client;
using Raven.Client.Documents.Operations.Counters;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Documents;
using Raven.Client.Json.Converters;
using Raven.Server.Routing;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Handlers
{
    public class CountersHandler : DatabaseRequestHandler
    {
        public class ExecuteCounterBatchCommand : TransactionOperationsMerger.MergedTransactionCommand
        {
            private readonly DocumentDatabase _database;
            private readonly CounterBatch _counterBatch;
            public bool HasWrites;
            public CountersDetail CountersDetail;

            public ExecuteCounterBatchCommand(DocumentDatabase database, CounterBatch counterBatch)
            {
                _database = database;
                _counterBatch = counterBatch;

                CountersDetail = new CountersDetail
                {
                    Counters = new List<CounterDetail>()
                };
                foreach (var docOps  in counterBatch.Documents)
                {
                    foreach (var operation in docOps.Operations)
                    {
                        HasWrites |= operation.Type != CounterOperationType.Get &&
                                     operation.Type != CounterOperationType.None;
                    }
                }
            }

            public override int Execute(DocumentsOperationContext context)
            {
                foreach (var docOps in _counterBatch.Documents)
                {
                    Document doc = null;
                    BlittableJsonReaderObject metadata = null;
                    
                    foreach (var operation in docOps.Operations)
                    {
                        switch (operation.Type)
                        {
                            case CounterOperationType.Increment:
                                LoadDocument();
                            {
                                _database.DocumentsStorage.CountersStorage.IncrementCounter(context, docOps.DocumentId,
                                    operation.CounterName, operation.Delta);

                                GetCounterValue(context, _database, docOps.DocumentId, operation.CounterName, _counterBatch.ReplyWithAllNodesValues, CountersDetail);
                            }
                                break;
                            case CounterOperationType.Delete:
                                LoadDocument();
                                _database.DocumentsStorage.CountersStorage.DeleteCounter(context, docOps.DocumentId,
                                    operation.CounterName);
                                break;
                            case CounterOperationType.None:
                                break;
                            case CounterOperationType.Get:
                                GetCounterValue(context, _database, docOps.DocumentId, operation.CounterName, _counterBatch.ReplyWithAllNodesValues, CountersDetail);
                                break;
                            default:
                                ThrowInvalidBatchOperationType(operation);
                                break;
                        }
                    }

                    if (metadata != null)
                    {
                        UpdateDocumentCounters(metadata, docOps.Operations);

                        if (metadata.Modifications != null)
                        {
                            var data = context.ReadObject(doc.Data, docOps.DocumentId, BlittableJsonDocumentBuilder.UsageMode.ToDisk);
                            _database.DocumentsStorage.Put(context, docOps.DocumentId, null, data, 
                                flags: DocumentFlags.HasCounters); // todo: CHECK FLAG HERE
                        }
                    }

                    void LoadDocument()
                    {
                        if (doc != null)
                            return;
                        try
                        {
                            doc = _database.DocumentsStorage.Get(context, docOps.DocumentId,
                                throwOnConflict: true);
                            if (doc == null)
                            {
                                ThrowMissingDocument(docOps.DocumentId);
                                return; // never hit
                            }

                            if (doc.TryGetMetadata(out metadata) == false)
                                ThrowInvalidDocumentWithNoMetadata(doc);
                        }
                        catch (DocumentConflictException)
                        {
                            // this is fine, we explicitly support
                            // setting the flag if we are in conflicted state is 
                            // done by the conflict resolver

                            // avoid loading same document again, we validate write using the metadata instance
                            doc = new Document();
                        }
                    }
                }

                return CountersDetail.Counters.Count;
            }

            private static void ThrowInvalidBatchOperationType(CounterOperation operation)
            {
                throw new ArgumentOutOfRangeException($"Unknown value {operation.Type}");
            }

            private void UpdateDocumentCounters(BlittableJsonReaderObject metadata, List<CounterOperation> countersOperations)
            {
                List<string> updates = null;
                if (metadata.TryGet(Constants.Documents.Metadata.Counters, out BlittableJsonReaderArray counters))
                {
                    foreach (var operation in countersOperations)
                    {
                        // we need to check the updates to avoid inserting duplicate counter names
                        int loc = updates?.BinarySearch(operation.CounterName, StringComparer.OrdinalIgnoreCase) ??
                                  counters.BinarySearch(operation.CounterName, StringComparison.OrdinalIgnoreCase);

                        switch (operation.Type)
                        {
                            case CounterOperationType.Increment:
                                if (loc < 0)
                                {
                                    CreateUpdatesIfNeeded();
                                    updates.Insert(~loc, operation.CounterName);
                                }

                                break;
                            case CounterOperationType.Delete:
                                if (loc >= 0)
                                {
                                    CreateUpdatesIfNeeded();
                                    updates.RemoveAt(loc);
                                }
                                break;
                            case CounterOperationType.None:
                            case CounterOperationType.Get:
                                break;
                            default:
                                ThrowInvalidBatchOperationType(operation);
                                break;
                        }
                    }
                }
                else
                {
                    updates = new List<string>(countersOperations.Count);
                    foreach (var operation in countersOperations)
                    {
                        updates.Add(operation.CounterName);
                    }
                    updates.Sort(StringComparer.OrdinalIgnoreCase);
                }

                if (updates != null)
                {
                    metadata.Modifications = new DynamicJsonValue(metadata)
                    {
                        [Constants.Documents.Metadata.Counters] = new DynamicJsonArray(updates)
                    };
                }

                void CreateUpdatesIfNeeded()
                {
                    if (updates != null)
                        return;

                    updates = new List<string>(counters.Length + countersOperations.Count);
                    for (int i = 0; i < counters.Length; i++)
                    {
                        var val = counters.GetStringByIndex(i);
                        if (val == null)
                            continue;
                        updates.Add(val);
                    }
                }
            }

            private static void ThrowMissingDocument(string docId)
            {
                throw new CounterDocumentMissingException($"There is no document '{docId}' (or it has been deleted), cannot operate on counters of a missing document");
            }
        }

        [RavenAction("/databases/*/counters", "GET", AuthorizationStatus.ValidUser)]
        public Task Get()
        {
            var docId = GetStringValuesQueryString("doc");
            var full = GetBoolValueQueryString("full", required: false) ?? false;
            var counters = GetStringValuesQueryString("counter", required: false);
            var countersDetail = new CountersDetail();

            using (ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            {
                using (context.OpenReadTransaction())
                {
                    var names = counters.Count != 0 ? 
                                counters : 
                                Database.DocumentsStorage.CountersStorage.GetCountersForDocument(context, docId);
                    foreach (var counter in names)
                    {
                        GetCounterValue(context, Database, docId, counter, full, countersDetail);
                    }
                }

                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    context.Write(writer, countersDetail.ToJson());
                    writer.Flush();
                }
            }

            return Task.CompletedTask;
        }

        [RavenAction("/databases/*/counters", "POST", AuthorizationStatus.ValidUser)]
        public async Task Batch()
        {
            using (ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            {
                var countersBlittable = await context.ReadForMemoryAsync(RequestBodyStream(), "counters");

                var counterBatch = JsonDeserializationClient.CounterBatch(countersBlittable);

                var cmd = new ExecuteCounterBatchCommand(Database, counterBatch);

                if (cmd.HasWrites)
                {
                    try
                    {
                        await Database.TxMerger.Enqueue(cmd);
                    }
                    catch (CounterDocumentMissingException)
                    {
                        HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        throw;
                    }
                }
                else
                {
                    using (context.OpenReadTransaction())
                    {
                        cmd.Execute(context);
                    }
                }
                using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    context.Write(writer, cmd.CountersDetail.ToJson());
                    writer.Flush();
                }
            }
        }

        private static void GetCounterValue(DocumentsOperationContext context, DocumentDatabase database, string docId, string counterName, bool addFullValues, CountersDetail result)
        {
            var fullValues = addFullValues ? new Dictionary<string, long>() : null;
            long? value = null;
            foreach (var (cv, val) in database.DocumentsStorage.CountersStorage.GetCounterValues(context,
                docId, counterName))
            {
                value = value ?? 0;
                value += val;

                if (addFullValues)
                {
                    fullValues[cv] = val;
                }
            }

            if (value == null)
                return;

            if (result.Counters == null)
                result.Counters = new List<CounterDetail>();

            result.Counters.Add(new CounterDetail
            {
                DocumentId = docId,
                CounterName = counterName,
                TotalValue = value.Value,
                CounterValues = fullValues
            });
        }

        private static void ThrowInvalidDocumentWithNoMetadata(Document doc)
        {
            throw new InvalidOperationException("Cannot increment counters for " + doc + " because the document has no metadata. Should not happen ever");
        }
    }
}
