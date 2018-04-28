﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Raven.Server.Documents.Replication;
using Raven.Server.ServerWide.Context;
using Voron;
using Voron.Data.Tables;
using Voron.Impl;
using Sparrow;
using Sparrow.Binary;
using Sparrow.Utils;
using static Raven.Server.Documents.DocumentsStorage;
using static Raven.Server.Documents.Replication.ReplicationBatchItem;
using Raven.Server.Utils;
using Raven.Client.Documents.Changes;

namespace Raven.Server.Documents
{
    public unsafe class CountersStorage
    {
        private const int DbIdAsBase64Size = 22;

        private readonly DocumentDatabase _documentDatabase;
        private readonly DocumentsStorage _documentsStorage;

        private static readonly Slice CountersSlice;
        private static readonly Slice CountersTombstonesSlice;
        private static readonly Slice CountersEtagSlice;

        public static readonly string CountersTombstones = "Counters.Tombstones";

        private static readonly TableSchema CountersSchema = new TableSchema()
        {
            TableType = (byte)TableType.Counters
        };

        private enum CountersTable
        {
            // Format of this is:
            // lower document id, record separator, lower counter name, record separator, 16 bytes dbid
            CounterKey = 0,
            Name = 1, // format of lazy string key is detailed in GetLowerIdSliceAndStorageKey
            Etag = 2,
            Value = 3,
            ChangeVector = 4,
            TransactionMarker = 5
        }

        static CountersStorage()
        {
            Slice.From(StorageEnvironment.LabelsContext, "Counters", ByteStringType.Immutable, out CountersSlice);
            Slice.From(StorageEnvironment.LabelsContext, "CountersEtag", ByteStringType.Immutable, out CountersEtagSlice);
            Slice.From(StorageEnvironment.LabelsContext, CountersTombstones, ByteStringType.Immutable, out CountersTombstonesSlice);

            CountersSchema.DefineKey(new TableSchema.SchemaIndexDef
            {
                StartIndex = (int)CountersTable.CounterKey,
                Count = 1,
            });
            CountersSchema.DefineFixedSizeIndex(new TableSchema.FixedSizeSchemaIndexDef
            {
                StartIndex = (int)CountersTable.Etag,
                Name = CountersEtagSlice,
                IsGlobal = true
            });
        }

        public CountersStorage(DocumentDatabase documentDatabase, Transaction tx)
        {
            _documentDatabase = documentDatabase;
            _documentsStorage = documentDatabase.DocumentsStorage;

            CountersSchema.Create(tx, CountersSlice, 32);
            TombstonesSchema.Create(tx, CountersTombstonesSlice, 16);
        }

        public IEnumerable<ReplicationBatchItem> GetCountersFrom(DocumentsOperationContext context, long etag)
        {
            var table = new Table(CountersSchema, context.Transaction.InnerTransaction);

            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var result in table.SeekForwardFrom(CountersSchema.FixedSizeIndexes[CountersEtagSlice], etag, 0))
            {
                yield return CreateReplicationBatchItem(context, result);
            }
        }

        private static ReplicationBatchItem CreateReplicationBatchItem(DocumentsOperationContext context, Table.TableValueHolder result)
        {
            var p = result.Reader.Read((int)CountersTable.CounterKey, out var size);
            Debug.Assert(size > DbIdAsBase64Size + 2/* record separators */);
            int sizeOfDocId = 0;
            for (; sizeOfDocId < size; sizeOfDocId++)
            {
                if (p[sizeOfDocId] == 30)
                    break;
            }
            var doc = context.AllocateStringValue(null, p, sizeOfDocId);
            var name = context.AllocateStringValue(null, p + sizeOfDocId + 1, size - (sizeOfDocId + 1) - DbIdAsBase64Size - 1);

            return new ReplicationBatchItem
            {
                Type = ReplicationItemType.Counter,
                Id = doc,
                Etag = TableValueToEtag((int)CountersTable.Etag, ref result.Reader),
                Name = name,
                ChangeVector = TableValueToString(context, (int)CountersTable.ChangeVector, ref result.Reader),
                Value = TableValueToLong((int)CountersTable.Value, ref result.Reader),
                TransactionMarker = TableValueToShort((int)CountersTable.TransactionMarker, nameof(ReplicationBatchItem.TransactionMarker), ref result.Reader),
            };
        }

        public void PutCounterFromReplication(DocumentsOperationContext context, string documentId, string name, string changeVector, long value)
        {
            if (context.Transaction == null)
            {
                DocumentPutAction.ThrowRequiresTransaction();
                Debug.Assert(false);// never hit
            }

            var table = context.Transaction.InnerTransaction.OpenTable(CountersSchema, CountersSlice);
            using (GetCounterKey(context, documentId, name, changeVector, out var counterKey))
            {
                using (DocumentIdWorker.GetSliceFromId(context, name, out Slice nameSlice))
                using (table.Allocate(out TableValueBuilder tvb))
                {
                    if (table.ReadByKey(counterKey, out var existing))
                    {
                        var existingChangeVector = TableValueToChangeVector(context, (int)CountersTable.ChangeVector, ref existing);

                        if (ChangeVectorUtils.GetConflictStatus(changeVector, existingChangeVector) == ConflictStatus.AlreadyMerged)
                            return;
                    }

                    // if tombstone exists, remove it
                    using (GetCounterPartialKey(context, documentId, name, out var keyPerfix))
                    {
                        var tombstoneTable = context.Transaction.InnerTransaction.OpenTable(TombstonesSchema, CountersTombstonesSlice);

                        if (tombstoneTable.ReadByKey(counterKey, out var existingTombstone))
                        {
                            table.Delete(existingTombstone.Id);
                        }
                    }

                    var etag = _documentsStorage.GenerateNextEtag();
                    using (Slice.From(context.Allocator, changeVector, out var cv))
                    {
                        tvb.Add(counterKey);
                        tvb.Add(nameSlice);
                        tvb.Add(Bits.SwapBytes(etag));
                        tvb.Add(value);
                        tvb.Add(cv);
                        tvb.Add(context.TransactionMarkerOffset);

                        table.Set(tvb);
                    }

                    context.Transaction.AddAfterCommitNotification(new DocumentChange
                    {
                        ChangeVector = changeVector,
                        Id = documentId,
                        CounterName = name,
                        Type = DocumentChangeTypes.Counter,
                    });
                }
            }
        }

        public void IncrementCounter(DocumentsOperationContext context, string documentId, string name, long value)
        {
            if (context.Transaction == null)
            {
                DocumentPutAction.ThrowRequiresTransaction();
                Debug.Assert(false);// never hit
            }
            
            var table = context.Transaction.InnerTransaction.OpenTable(CountersSchema, CountersSlice);
            using (GetCounterKey(context, documentId, name, context.Environment.Base64Id, out var counterKey))
            {
                long prev = 0;
                if (table.ReadByKey(counterKey, out var existing))
                {
                    prev = *(long*)existing.Read((int)CountersTable.Value, out var size);
                    Debug.Assert(size == sizeof(long));
                }

                // if tombstone exists, remove it
                using (GetCounterPartialKey(context, documentId, name, out var keyPerfix))
                {
                    var tombstoneTable = context.Transaction.InnerTransaction.OpenTable(TombstonesSchema, CountersTombstonesSlice);

                    if (tombstoneTable.ReadByKey(counterKey, out var existingTombstone))
                    {
                        table.Delete(existingTombstone.Id);
                    }
                }

                var etag = _documentsStorage.GenerateNextEtag();
                var result = ChangeVectorUtils.TryUpdateChangeVector(_documentDatabase.ServerStore.NodeTag, _documentsStorage.Environment.Base64Id, etag, string.Empty);

                using (Slice.From(context.Allocator, result.ChangeVector, out var cv))
                using (DocumentIdWorker.GetSliceFromId(context, name, out Slice nameSlice))
                using (table.Allocate(out TableValueBuilder tvb))
                {
                    tvb.Add(counterKey);
                    tvb.Add(nameSlice);
                    tvb.Add(Bits.SwapBytes(etag));
                    tvb.Add(prev + value); //inc
                    tvb.Add(cv); 
                    tvb.Add(context.TransactionMarkerOffset);

                    table.Set(tvb);
                }

                context.Transaction.AddAfterCommitNotification(new DocumentChange
                {
                    ChangeVector = result.ChangeVector,
                    Id = documentId,
                    CounterName = name,
                    Type = DocumentChangeTypes.Counter,
                });
            }
        }

        public IEnumerable<string> GetCountersForDocument(DocumentsOperationContext context, string docId, int skip, int take)
        {
            var table = context.Transaction.InnerTransaction.OpenTable(CountersSchema, CountersSlice);
            using (GetCounterPartialKey(context, docId, out var key))
            {
                ByteStringContext<ByteStringMemoryCache>.ExternalScope scope = default;
                ByteString prev = default;
                foreach (var result in table.SeekByPrimaryKeyPrefix(key, Slices.Empty, skip))
                {
                    if (take-- <= 0)
                        break;

                    var currentScope = ExtractCounterName(context, result.Key, key, out var current);

                    if (prev.HasValue && prev.Match(current))
                    {
                        // already seen this one, skip it 
                        currentScope.Dispose();
                        continue;
                    }

                    yield return current.ToString(Encoding.UTF8);

                    prev = current;
                    scope = currentScope;
                }

                scope.Dispose();
            }
        }

        public long? GetCounterValue(DocumentsOperationContext context, string docId, string name)
        {
            var table = context.Transaction.InnerTransaction.OpenTable(CountersSchema, CountersSlice);
            using (GetCounterPartialKey(context, docId, name, out var key))
            {
                long? value = null;
                foreach (var result in table.SeekByPrimaryKeyPrefix(key, Slices.Empty, 0))
                {
                    value = value ?? 0;
                    var pCounterDbValue = result.Value.Reader.Read((int)CountersTable.Value, out var size);
                    Debug.Assert(size == sizeof(long));
                    value += *(long*)pCounterDbValue;
                }

                return value;
            }
        }

        public IEnumerable<(string ChangeVector, long Value)> GetCounterValues(DocumentsOperationContext context, string docId, string name)
        {
            var table = context.Transaction.InnerTransaction.OpenTable(CountersSchema, CountersSlice);
            using (GetCounterPartialKey(context, docId, name, out var keyPerfix))
            {
                foreach (var result in table.SeekByPrimaryKeyPrefix(keyPerfix, Slices.Empty, 0))
                {
                    (string, long) val = ExtractDbIdAndValue(result);
                    yield return val;
                }
            }
        }

        private static (string ChangeVector , long Value) ExtractDbIdAndValue((Slice Key, Table.TableValueHolder Value) result)
        {
            var counterKey = result.Value.Reader.Read((int)CountersTable.CounterKey, out var size);
            Debug.Assert(size > DbIdAsBase64Size);
            var pCounterDbValue = result.Value.Reader.Read((int)CountersTable.Value, out size);
            Debug.Assert(size == sizeof(long));
            var changeVector = result.Value.Reader.Read((int)CountersTable.ChangeVector, out size);
            
            return (Encoding.UTF8.GetString(changeVector, size), *(long*)pCounterDbValue);
        }

        private static ByteStringContext<ByteStringMemoryCache>.ExternalScope ExtractCounterName(DocumentsOperationContext context, Slice counterKey, Slice documentIdPrefix, out ByteString current)
        {
            var scope = context.Allocator.FromPtr(counterKey.Content.Ptr + documentIdPrefix.Size,
                counterKey.Size - documentIdPrefix.Size - DbIdAsBase64Size - 1, /* record separator*/
                ByteStringType.Immutable,
                out current
            );

            return scope;
        }

        public ByteStringContext.InternalScope GetCounterKey(DocumentsOperationContext context, string documentId, string name, string changeVector, out Slice partialKeySlice)
        {
            Debug.Assert(changeVector.Length >= DbIdAsBase64Size);

            using (DocumentIdWorker.GetLowerIdSliceAndStorageKey(context, documentId, out var docIdLower, out _))
            using (DocumentIdWorker.GetLowerIdSliceAndStorageKey(context, name, out var nameLower, out _))
            using (Slice.From(context.Allocator, changeVector, out var cv))
            {
                var scope = context.Allocator.Allocate(docIdLower.Size
                                                       + 1 // record separator
                                                       + nameLower.Size
                                                       + 1 // record separator
                                                       + DbIdAsBase64Size, // db id
                                                       out ByteString buffer);

                docIdLower.CopyTo(buffer.Ptr);
                buffer.Ptr[docIdLower.Size] = SpecialChars.RecordSeparator;
                byte* dest = buffer.Ptr + docIdLower.Size + 1;
                nameLower.CopyTo(dest);
                dest[nameLower.Size] = SpecialChars.RecordSeparator;
                cv.CopyTo(cv.Size - DbIdAsBase64Size, dest, nameLower.Size + 1, DbIdAsBase64Size);

                partialKeySlice = new Slice(buffer);

                return scope;
            }
        }

        public ByteStringContext.InternalScope GetCounterPartialKey(DocumentsOperationContext context, string documentId, string name, out Slice partialKeySlice)
        {
            using (DocumentIdWorker.GetLowerIdSliceAndStorageKey(context, documentId, out var docIdLower, out _))
            using (DocumentIdWorker.GetLowerIdSliceAndStorageKey(context, name, out var nameLower, out _))
            {
                var scope = context.Allocator.Allocate(docIdLower.Size
                                                       + 1 // record separator
                                                       + nameLower.Size
                                                       + 1 // record separator
                                                       , out ByteString buffer);

                docIdLower.CopyTo(buffer.Ptr);
                buffer.Ptr[docIdLower.Size] = SpecialChars.RecordSeparator;

                byte* dest = buffer.Ptr + docIdLower.Size + 1;
                nameLower.CopyTo(dest);
                dest[nameLower.Size] = SpecialChars.RecordSeparator;

                partialKeySlice = new Slice(buffer);

                return scope;
            }
        }

        public ByteStringContext.InternalScope GetCounterPartialKey(DocumentsOperationContext context, string documentId,  out Slice partialKeySlice)
        {
            using (DocumentIdWorker.GetLowerIdSliceAndStorageKey(context, documentId, out var docIdLower, out _))
            {
                var scope = context.Allocator.Allocate(docIdLower.Size
                                                       + 1 // record separator
                                                       , out ByteString buffer);

                docIdLower.CopyTo(buffer.Ptr);
                buffer.Ptr[docIdLower.Size] = SpecialChars.RecordSeparator;

                partialKeySlice = new Slice(buffer);

                return scope;
            }
        }

        public void DeleteCountersForDocument(DocumentsOperationContext context, string documentId)
        {
            // this will called as part of document's delete, so we don't bother creating
            // tombstones (existing tombstones will remain and be cleaned up by the usual
            // tombstone cleaner task
            var table = context.Transaction.InnerTransaction.OpenTable(CountersSchema, CountersSlice);

            if (table.NumberOfEntries == 0)
                return; 

            using (GetCounterPartialKey(context, documentId, out var keyPerfix))
            {
                table.DeleteByPrimaryKeyPrefix(keyPerfix);
            }
        }

        public bool DeleteCounter(DocumentsOperationContext context, string documentId, string name)
        {
            if (context.Transaction == null)
            {
                DocumentPutAction.ThrowRequiresTransaction();
                Debug.Assert(false);// never hit
            }

            using (GetCounterPartialKey(context, documentId, name, out var keyPerfix))
            {
                var lastModifiedTicks = _documentDatabase.Time.GetUtcNow().Ticks;
                return DeleteCounter(context, keyPerfix, lastModifiedTicks,
                    // let's avoid creating a tombstone for missing counter if writing locally
                    forceTombstone: false);
            }
        }

        public bool DeleteCounter(DocumentsOperationContext context, Slice key, long lastModifiedTicks, bool forceTombstone)
        {
            var table = context.Transaction.InnerTransaction.OpenTable(CountersSchema, CountersSlice);
            if (table.DeleteByPrimaryKeyPrefix(key) == false
                && forceTombstone == false)
                return false;
          
            CreateTombstone(context, key, lastModifiedTicks);
            return true;
        }

        private void CreateTombstone(DocumentsOperationContext context, Slice keySlice, long lastModifiedTicks)
        {
            var newEtag = _documentsStorage.GenerateNextEtag();

            var table = context.Transaction.InnerTransaction.OpenTable(TombstonesSchema, CountersTombstonesSlice);
            using (table.Allocate(out TableValueBuilder tvb))
            {
                tvb.Add(keySlice.Content.Ptr, keySlice.Size);
                tvb.Add(Bits.SwapBytes(newEtag));
                tvb.Add(0L); // etag that was deleted
                tvb.Add(context.GetTransactionMarker());
                tvb.Add((byte)DocumentTombstone.TombstoneType.Counter);
                tvb.Add(null, 0); // doc data
                tvb.Add((int)DocumentFlags.None);
                tvb.Add(null, 0); // change vector
                tvb.Add(lastModifiedTicks);
                table.Insert(tvb);
            }
        }
    }
}
