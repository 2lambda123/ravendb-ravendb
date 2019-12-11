﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Raven.Server.Config.Categories;
using Raven.Server.Documents.Indexes.Persistence.Lucene;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow.Json;
using Sparrow.Logging;
using Sparrow.Server;
using Voron;

namespace Raven.Server.Documents.Indexes.Workers
{
    public sealed class HandleDocumentReferences : HandleReferences
    {
        public HandleDocumentReferences(Index index, Dictionary<string, HashSet<CollectionName>> referencedCollections, DocumentsStorage documentsStorage, IndexStorage indexStorage, IndexingConfiguration configuration)
            : base(index, referencedCollections, documentsStorage, indexStorage, configuration)
        {
        }

        protected unsafe override IndexItem GetItem(DocumentsOperationContext databaseContext, Slice key)
        {
            using (DocumentIdWorker.GetLower(databaseContext.Allocator, key.Content.Ptr, key.Size, out var loweredKey))
            {
                // when there is conflict, we need to apply same behavior as if the document would not exist
                var doc = _documentsStorage.Get(databaseContext, loweredKey, throwOnConflict: false);
                if (doc == null)
                    return default;

                return new DocumentIndexItem(doc.Id, doc.LowerId, doc.Etag, doc.LastModified, doc.Data.Size, doc);
            }
        }
    }

    public abstract class HandleReferences : IIndexingWork
    {
        private readonly Logger _logger;

        private readonly Index _index;
        private readonly Dictionary<string, HashSet<CollectionName>> _referencedCollections;
        protected readonly DocumentsStorage _documentsStorage;
        private readonly IndexingConfiguration _configuration;
        private readonly IndexStorage _indexStorage;

        protected readonly Reference _reference = new Reference();

        public HandleReferences(Index index, Dictionary<string, HashSet<CollectionName>> referencedCollections, DocumentsStorage documentsStorage, IndexStorage indexStorage, IndexingConfiguration configuration)
        {
            _index = index;
            _referencedCollections = referencedCollections;
            _documentsStorage = documentsStorage;
            _configuration = configuration;
            _indexStorage = indexStorage;
            _logger = LoggingSource.Instance
                .GetLogger<HandleReferences>(_indexStorage.DocumentDatabase.Name);
        }

        public string Name => "References";

        public bool Execute(DocumentsOperationContext databaseContext, TransactionOperationContext indexContext,
            Lazy<IndexWriteOperation> writeOperation, IndexingStatsScope stats, CancellationToken token)
        {
            const int pageSize = int.MaxValue;
            var maxTimeForDocumentTransactionToRemainOpen = Debugger.IsAttached == false
                            ? _configuration.MaxTimeForDocumentTransactionToRemainOpen.AsTimeSpan
                            : TimeSpan.FromMinutes(15);

            var moreWorkFound = HandleItems(ActionType.Tombstone, databaseContext, indexContext, writeOperation, stats, pageSize, maxTimeForDocumentTransactionToRemainOpen, token);
            moreWorkFound |= HandleItems(ActionType.Document, databaseContext, indexContext, writeOperation, stats, pageSize, maxTimeForDocumentTransactionToRemainOpen, token);

            return moreWorkFound;
        }

        public bool CanContinueBatch(DocumentsOperationContext documentsContext, TransactionOperationContext indexingContext,
            IndexingStatsScope stats, IndexWriteOperation indexWriteOperation, long currentEtag, long maxEtag, int count)
        {
            if (stats.Duration >= _configuration.MapTimeout.AsTimeSpan)
                return false;

            if (currentEtag >= maxEtag && stats.Duration >= _configuration.MapTimeoutAfterEtagReached.AsTimeSpan)
                return false;

            if (_index.CanContinueBatch(stats, documentsContext, indexingContext, indexWriteOperation, count) == false)
                return false;

            return true;
        }

        private unsafe bool HandleItems(ActionType actionType, DocumentsOperationContext databaseContext, TransactionOperationContext indexContext, Lazy<IndexWriteOperation> writeOperation, IndexingStatsScope stats, int pageSize, TimeSpan maxTimeForDocumentTransactionToRemainOpen, CancellationToken token)
        {
            var moreWorkFound = false;
            Dictionary<string, long> lastIndexedEtagsByCollection = null;

            foreach (var collection in _index.Collections)
            {
                if (_referencedCollections.TryGetValue(collection, out HashSet<CollectionName> referencedCollections) == false)
                    continue;

                if (lastIndexedEtagsByCollection == null)
                    lastIndexedEtagsByCollection = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

                if (lastIndexedEtagsByCollection.TryGetValue(collection, out long lastIndexedEtag) == false)
                    lastIndexedEtagsByCollection[collection] = lastIndexedEtag = _indexStorage.ReadLastIndexedEtag(indexContext.Transaction, collection);

                if (lastIndexedEtag == 0) // we haven't indexed yet, so we are skipping references for now
                    continue;

                foreach (var referencedCollection in referencedCollections)
                {
                    var inMemoryStats = _index.GetReferencesStats(referencedCollection.Name);

                    using (var collectionStats = stats.For("Collection_" + referencedCollection.Name))
                    {
                        if (_logger.IsInfoEnabled)
                            _logger.Info($"Executing handle references for '{_index.Name}'. Collection: {referencedCollection.Name}. Type: {actionType}.");

                        long lastReferenceEtag;

                        switch (actionType)
                        {
                            case ActionType.Document:
                                lastReferenceEtag = _indexStorage.ReadLastProcessedReferenceEtag(indexContext.Transaction, collection, referencedCollection);
                                break;
                            case ActionType.Tombstone:
                                lastReferenceEtag = _indexStorage.ReadLastProcessedReferenceTombstoneEtag(indexContext.Transaction, collection, referencedCollection);
                                break;
                            default:
                                throw new NotSupportedException();
                        }

                        if (_logger.IsInfoEnabled)
                            _logger.Info($"Executing handle references for '{_index.Name}'. LastReferenceEtag: {lastReferenceEtag}.");

                        var lastEtag = lastReferenceEtag;
                        var count = 0;

                        var sw = new Stopwatch();
                        IndexWriteOperation indexWriter = null;

                        var keepRunning = true;
                        var lastCollectionEtag = -1L;
                        while (keepRunning)
                        {
                            var batchCount = 0;

                            using (databaseContext.OpenReadTransaction())
                            {
                                sw.Restart();

                                IEnumerable<Reference> references;
                                switch (actionType)
                                {
                                    case ActionType.Document:
                                        if (lastCollectionEtag == -1)
                                            lastCollectionEtag = _index.GetLastItemEtagInCollection(databaseContext, collection);

                                        references = GetItemReferences(databaseContext, referencedCollection, lastEtag, 0, pageSize);
                                        break;
                                    case ActionType.Tombstone:
                                        if (lastCollectionEtag == -1)
                                            lastCollectionEtag = _index.GetLastTombstoneEtagInCollection(databaseContext, collection);

                                        references = GetTombstoneReferences(databaseContext, referencedCollection, lastEtag, 0, pageSize);
                                        break;
                                    default:
                                        throw new NotSupportedException();
                                }

                                var isTombstone = actionType == ActionType.Tombstone;

                                foreach (var referencedDocument in references)
                                {
                                    if (_logger.IsInfoEnabled)
                                        _logger.Info($"Executing handle references for '{_index.Name}'. Processing reference: {referencedDocument.Key}.");

                                    lastEtag = referencedDocument.Etag;
                                    inMemoryStats.UpdateLastEtag(lastEtag, isTombstone);
                                    count++;
                                    batchCount++;

                                    var items = GetItemsFromCollectionThatReference(databaseContext, indexContext, collection, referencedDocument, lastIndexedEtag);

                                    using (var itemsEnumerator = _index.GetMapEnumerator(items, collection, indexContext, collectionStats, _index.Type))
                                    {
                                        while (itemsEnumerator.MoveNext(out IEnumerable mapResults, out var etag))
                                        {
                                            token.ThrowIfCancellationRequested();

                                            var current = itemsEnumerator.Current;

                                            if (indexWriter == null)
                                                indexWriter = writeOperation.Value;

                                            if (_logger.IsInfoEnabled)
                                                _logger.Info($"Executing handle references for '{_index.Name}'. Processing document: {current.Id}.");

                                            try
                                            {
                                                var numberOfResults = _index.HandleMap(current, mapResults, indexWriter, indexContext, collectionStats);

                                                _index.MapsPerSec.MarkSingleThreaded(numberOfResults);
                                            }
                                            catch (Exception e) when (e.IsIndexError())
                                            {
                                                itemsEnumerator.OnError();
                                                _index.ErrorIndexIfCriticalException(e);

                                                collectionStats.RecordMapError();
                                                if (_logger.IsInfoEnabled)
                                                    _logger.Info($"Failed to execute mapping function on '{current.Id}' for '{_index.Name}'.", e);

                                                collectionStats.AddMapError(current.Id, $"Failed to execute mapping function on {current.Id}. " +
                                                                                        $"Exception: {e}");
                                            }

                                            _index.UpdateThreadAllocations(indexContext, indexWriter, stats, updateReduceStats: false);
                                        }
                                    }

                                    if (CanContinueBatch(databaseContext, indexContext, collectionStats, indexWriter, lastEtag, lastCollectionEtag, batchCount) == false)
                                    {
                                        keepRunning = false;
                                        break;
                                    }

                                    if (MapDocuments.MaybeRenewTransaction(databaseContext, sw, _configuration, ref maxTimeForDocumentTransactionToRemainOpen))
                                        break;
                                }

                                if (batchCount == 0 || batchCount >= pageSize)
                                    break;
                            }
                        }

                        if (count == 0)
                            continue;

                        if (_logger.IsInfoEnabled)
                            _logger.Info($"Executing handle references for '{_index} ({_index.Name})'. Processed {count} references in '{referencedCollection.Name}' collection in {collectionStats.Duration.TotalMilliseconds:#,#;;0} ms.");

                        switch (actionType)
                        {
                            case ActionType.Document:
                                _indexStorage.WriteLastReferenceEtag(indexContext.Transaction, collection, referencedCollection, lastEtag);
                                break;
                            case ActionType.Tombstone:
                                _indexStorage.WriteLastReferenceTombstoneEtag(indexContext.Transaction, collection, referencedCollection, lastEtag);
                                break;
                            default:
                                throw new NotSupportedException();
                        }

                        moreWorkFound = true;
                    }
                }
            }

            return moreWorkFound;
        }

        private IEnumerable<IndexItem> GetItemsFromCollectionThatReference(DocumentsOperationContext databaseContext, TransactionOperationContext indexContext, string collection, Reference referencedDocument, long lastIndexedEtag)
        {
            foreach (var key in _indexStorage.GetItemKeysFromCollectionThatReference(collection, referencedDocument.Key, indexContext.Transaction))
            {
                var item = GetItem(databaseContext, key);

                if (item == null)
                    continue;

                if (item.Etag > lastIndexedEtag)
                {
                    item.Dispose();
                    continue;
                }

                yield return item;
            }
        }

        protected IEnumerable<Reference> GetItemReferences(DocumentsOperationContext databaseContext, CollectionName referencedCollection, long lastEtag, int start, int pageSize)
        {
            return _documentsStorage
                .GetDocumentsFrom(databaseContext, referencedCollection.Name, lastEtag + 1, 0, pageSize, DocumentFields.Id | DocumentFields.Etag)
                .Select(document =>
                {
                    _reference.Key = document.Id;
                    _reference.Etag = document.Etag;

                    return _reference;
                });
        }

        protected IEnumerable<Reference> GetTombstoneReferences(DocumentsOperationContext databaseContext, CollectionName referencedCollection, long lastEtag, int start, int pageSize)
        {
            return _documentsStorage
                .GetTombstonesFrom(databaseContext, referencedCollection.Name, lastEtag + 1, 0, pageSize)
                .Select(tombstone =>
                {
                    _reference.Key = tombstone.LowerId;
                    _reference.Etag = tombstone.Etag;

                    return _reference;
                });
        }

        protected abstract IndexItem GetItem(DocumentsOperationContext databaseContext, Slice key);

        public unsafe void HandleDelete(Tombstone tombstone, string collection, IndexWriteOperation writer, TransactionOperationContext indexContext, IndexingStatsScope stats)
        {
            var tx = indexContext.Transaction.InnerTransaction;
            var loweredKey = tombstone.LowerId;
            using (Slice.External(tx.Allocator, loweredKey, out Slice tombstoneKeySlice))
                _indexStorage.RemoveReferences(tombstoneKeySlice, collection, null, indexContext.Transaction);
        }

        private enum ActionType
        {
            Document,
            Tombstone
        }

        protected class Reference
        {
            public LazyStringValue Key;

            public long Etag;
        }
    }
}
