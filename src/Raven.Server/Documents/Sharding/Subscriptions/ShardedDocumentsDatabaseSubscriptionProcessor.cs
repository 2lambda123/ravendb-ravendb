﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Raven.Client.ServerWide.Sharding;
using Raven.Server.Documents.Includes;
using Raven.Server.Documents.Includes.Sharding;
using Raven.Server.Documents.Subscriptions;
using Raven.Server.Documents.Subscriptions.Stats;
using Raven.Server.Documents.Subscriptions.SubscriptionProcessor;
using Raven.Server.Documents.TcpHandlers;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow;
using Sparrow.Server;
using Sparrow.Threading;
using static Raven.Server.Documents.Subscriptions.SubscriptionFetcher;

namespace Raven.Server.Documents.Sharding.Subscriptions;

public class ShardedDocumentsDatabaseSubscriptionProcessor : DocumentsDatabaseSubscriptionProcessor
{
    private readonly ShardedDocumentDatabase _database;
    private ShardingConfiguration _sharding;
    private readonly ByteStringContext _allocator;

    public ShardedDocumentsDatabaseSubscriptionProcessor(ServerStore server, ShardedDocumentDatabase database, SubscriptionConnection connection) : base(server, database, connection)
    {
        _database = database;
        _allocator = new ByteStringContext(SharedMultipleUseFlag.None);
    }

    protected override SubscriptionFetcher<Document> CreateFetcher()
    {
        _sharding = _database.ShardingConfiguration;
        return base.CreateFetcher();
    }

    protected override ConflictStatus GetConflictStatus(string changeVector)
    {
        SubscriptionState.ShardingState.ChangeVectorForNextBatchStartingPointPerShard.TryGetValue(_database.Name, out var cv);
        var conflictStatus = ChangeVectorUtils.GetConflictStatus(
            remoteAsString: changeVector,
            localAsString: cv);
        return conflictStatus;
    }

    protected override async ValueTask<bool> CanContinueBatchAsync(BatchItem batchItem, Size size, int numberOfDocs, Stopwatch sendingCurrentBatchStopwatch)
    {
        if (batchItem.Status == BatchItemStatus.ActiveMigration)
            return false;

        return await base.CanContinueBatchAsync(batchItem, size, numberOfDocs, sendingCurrentBatchStopwatch);
    }

    protected override BatchStatus SetBatchStatus(SubscriptionBatchResult result)
    {
        if (result.Status == BatchStatus.ActiveMigration)
            return BatchStatus.ActiveMigration;

        return base.SetBatchStatus(result);
    }

    protected override void HandleBatchItem(SubscriptionBatchStatsScope batchScope, BatchItem batchItem, SubscriptionBatchResult result, Document item)
    {
        if (batchItem.Status == BatchItemStatus.ActiveMigration)
        {
            item.Data?.Dispose();
            item.Data = null;
            result.Status = BatchStatus.ActiveMigration;
            return;
        }

        base.HandleBatchItem(batchScope, batchItem, result, item);
    }

    protected override string SetLastChangeVectorInThisBatch(IChangeVectorOperationContext context, string currentLast, BatchItem batchItem)
    {
        if (batchItem.Document.Etag == 0) // got this document from resend
        {
            if (batchItem.Document.Data == null)
                return currentLast;

            // shard might read only from resend 
        }

        var vector = context.GetChangeVector(batchItem.Document.ChangeVector);

        var result = ChangeVectorUtils.MergeVectors(
            currentLast,
            ChangeVectorUtils.NewChangeVector(_database.ServerStore.NodeTag, batchItem.Document.Etag, _database.DbBase64Id),
            vector.Order);

        return result;
    }

    protected override bool ShouldSend(Document item, out string reason, out Exception exception, out Document result, out bool isActiveMigration)
    {
        exception = null;
        result = item;

        if (IsUnderActiveMigration(item.Id, _sharding, _allocator, _database.ShardNumber, Fetcher.FetchingFrom, out reason, out isActiveMigration))
        {
            item.Data = null;
            item.ChangeVector = string.Empty;
            return false;
        }

        return base.ShouldSend(item, out reason, out exception, out result, out isActiveMigration);
    }
  
    public static bool IsUnderActiveMigration(string id, ShardingConfiguration sharding, ByteStringContext allocator, int shardNumber, FetchingOrigin fetchingFrom, out string reason, out bool isActiveMigration)
    {
        reason = null;
        isActiveMigration = false;
        var bucket = ShardHelper.GetBucketFor(sharding, allocator, id);
        var shard = ShardHelper.GetShardNumberFor(sharding, bucket);
        if (sharding.BucketMigrations.TryGetValue(bucket, out var migration) && migration.IsActive)
        {
            reason = $"The document '{id}' from bucket '{bucket}' is under active migration and fetched from '{fetchingFrom}'.";
            if (fetchingFrom == FetchingOrigin.Storage || shard == shardNumber)
            {
                reason += " Will set IsActiveMigration to true.";
                // we pulled doc with active migration from storage or from resend list (when it belongs to us)
                isActiveMigration = true;
            }

            return true;
        }

        if (shard != shardNumber)
        {
            reason = $"The owner of '{id}' document is shard '{shard}' (current shard number: '{shardNumber}') and fetched from '{fetchingFrom}'.";
            if (fetchingFrom == FetchingOrigin.Storage)
            {
                reason += " Will set IsActiveMigration to true.";
                isActiveMigration = true;
            }

            return true;
        }

        return false;
    }

    public override void Dispose()
    {
        base.Dispose();

        _allocator?.Dispose();
    }

    protected override bool CheckIfNewerInResendList(DocumentsOperationContext context, string id, string cvInStorage, string cvInResendList)
    {
        var resendListCvIsNewer = Database.DocumentsStorage.GetConflictStatus(context, cvInResendList, cvInStorage, ChangeVectorMode.Version);
        if (resendListCvIsNewer == ConflictStatus.Update)
        {
            return true;
        }

        return false;
    }

    protected override bool ShouldFetchFromResend(DocumentsOperationContext context, string id, DocumentsStorage.DocumentOrTombstone item, string currentChangeVector, out string reason)
    {
        reason = null;
        if (item.Document == null)
        {
            // the document was delete while it was processed by the client
            ItemsToRemoveFromResend.Add(id);
            reason = $"document '{id}' removed and skipped from resend";
            return false;
        }

        var cv = context.GetChangeVector(item.Document.ChangeVector);
        if (cv.IsSingle)
            return base.ShouldFetchFromResend(context, id, item, currentChangeVector, out reason);

        item.Document.ChangeVector = context.GetChangeVector(cv.Version, cv.Order.RemoveId(_sharding.DatabaseId, context));

        return base.ShouldFetchFromResend(context, id, item, currentChangeVector, out reason);
    }

    public HashSet<string> Skipped;

    public override async Task<long> RecordBatchAsync(string lastChangeVectorSentInThisBatch)
    {
        var result = await SubscriptionConnectionsState.RecordBatchDocumentsAsync(BatchItems, ItemsToRemoveFromResend, lastChangeVectorSentInThisBatch);
        Skipped = result.Skipped as HashSet<string>;
        return result.Index;
    }

    public override async Task AcknowledgeBatchAsync(long batchId, string changeVector)
    {
        ItemsToRemoveFromResend.Clear();
        BatchItems.Clear();

        await SubscriptionConnectionsState.AcknowledgeShardingBatchAsync(Connection.LastSentChangeVectorInThisConnection, changeVector, batchId, BatchItems);
    }

    protected override ShardIncludesCommandImpl CreateIncludeCommands()
    {
        var hasIncludes = TryCreateIncludesCommand(Database, DocsContext, Connection, Connection.Subscription, out IncludeCountersCommand includeCounters, out IncludeDocumentsCommand includeDocuments, out IncludeTimeSeriesCommand includeTimeSeries);
        var includes = hasIncludes ? new ShardIncludesCommandImpl(includeDocuments, includeTimeSeries, includeCounters) : null;

        return includes;
    }
}
