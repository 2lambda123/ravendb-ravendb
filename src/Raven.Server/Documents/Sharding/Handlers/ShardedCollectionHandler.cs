﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session.Operations;
using Raven.Client.Http;
using Raven.Server.Documents.Sharding.Handlers.ContinuationTokens;
using Raven.Server.Documents.Sharding.Operations;
using Raven.Server.Documents.Sharding.Streaming;
using Raven.Server.Json;
using Raven.Server.Routing;
using Sparrow.Json;
using Sparrow.Utils;

namespace Raven.Server.Documents.Sharding.Handlers
{
    public class ShardedCollectionHandler : ShardedRequestHandler
    {
        [RavenShardedAction("/databases/*/collections/stats", "GET")]
        public async Task GetCollectionStats()
        {
            var op = new ShardedCollectionStatisticsOperation();

            var stats = await ShardExecutor.ExecuteParallelForAllAsync(op);

            using (ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                    context.Write(writer, stats.ToJson());
            }
        }

        [RavenShardedAction("/databases/*/collections/stats/detailed", "GET")]
        public async Task GetDetailedCollectionStats()
        {
            var op = new ShardedDetailedCollectionStatisticsOperation();
            var stats = await ShardExecutor.ExecuteParallelForAllAsync(op);

            using (ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                    context.Write(writer, stats.ToJson());
            }
        }

        [RavenShardedAction("/databases/*/collections/docs", "GET")]
        public async Task GetCollectionDocuments()
        {
            using (Server.ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                var token = GetOrCreateContinuationToken(context);

                var op = new ShardedCollectionDocumentsOperation(token);
                var results = await ShardExecutor.ExecuteParallelForAllAsync(op);
                using var streams = await results.InitializeAsync(ShardedContext, HttpContext.RequestAborted);

                long numberOfResults;
                long totalDocumentsSizeInBytes;
                using (var cancelToken = CreateOperationToken())
                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    var documents = GetDocuments(streams, token);
                    writer.WriteStartObject();
                    writer.WritePropertyName(nameof(CollectionResult.Results));
                    (numberOfResults, totalDocumentsSizeInBytes) = await writer.WriteDocumentsAsync(context, documents, metadataOnly: false, cancelToken.Token);
                    writer.WriteComma();
                    writer.WriteContinuationToken(context, token);
                    writer.WriteEndObject();
                }

                //AddPagingPerformanceHint(PagingOperationType.Documents, "Collection", HttpContext.Request.QueryString.Value, numberOfResults, pageSize, sw.ElapsedMilliseconds, totalDocumentsSizeInBytes);
                DevelopmentHelper.ShardingToDo(DevelopmentHelper.TeamMember.Karmel, DevelopmentHelper.Severity.Normal, "Handle sharded AddPagingPerformanceHint");
            }
        }

        public async IAsyncEnumerable<Document> GetDocuments(CombinedReadContinuationState documents, ShardedPagingContinuation pagingContinuation)
        {
            await foreach (var result in ShardedContext.Streaming.PagedShardedDocumentsByLastModified(documents, nameof(CollectionResult.Results), pagingContinuation))
            {
                yield return result.Item;
            }
        }
        
        public class CollectionResult
        {
            public List<object> Results;
            public string ContinuationToken;
        }

        private readonly struct ShardedCollectionDocumentsOperation : IShardedStreamableOperation
        {
            private readonly ShardedPagingContinuation _token;

            public ShardedCollectionDocumentsOperation(ShardedPagingContinuation token)
            {
                _token = token;
            }

            public RavenCommand<StreamResult> CreateCommandForShard(int shard) =>
                StreamOperation.CreateStreamCommand(startsWith: null, matches: null, _token.Pages[shard].Start, _token.PageSize, exclude: null);
        }

        private struct ShardedCollectionStatisticsOperation : IShardedOperation<CollectionStatistics>
        {
            public CollectionStatistics Combine(Memory<CollectionStatistics> results)
            {
                var stats = new CollectionStatistics();
                var span = results.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    var result = span[i];
                    stats.CountOfDocuments += result.CountOfDocuments;
                    stats.CountOfConflicts += result.CountOfConflicts;
                    foreach (var collectionInfo in result.Collections)
                    {
                        stats.Collections[collectionInfo.Key] = stats.Collections.ContainsKey(collectionInfo.Key)
                            ? stats.Collections[collectionInfo.Key] + collectionInfo.Value
                            : collectionInfo.Value;
                    }
                }

                return stats;
            }

            public RavenCommand<CollectionStatistics> CreateCommandForShard(int shard) => new GetCollectionStatisticsOperation.GetCollectionStatisticsCommand();
        }

        private struct ShardedDetailedCollectionStatisticsOperation : IShardedOperation<DetailedCollectionStatistics>
        {
            public DetailedCollectionStatistics Combine(Memory<DetailedCollectionStatistics> results)
            {
                var stats = new DetailedCollectionStatistics();
                var span = results.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    var result = span[i];
                    stats.CountOfDocuments += result.CountOfDocuments;
                    stats.CountOfConflicts += result.CountOfConflicts;
                    foreach (var collectionInfo in result.Collections)
                    {
                        if (stats.Collections.ContainsKey(collectionInfo.Key))
                        {
                            stats.Collections[collectionInfo.Key].CountOfDocuments += collectionInfo.Value.CountOfDocuments;
                            stats.Collections[collectionInfo.Key].DocumentsSize.SizeInBytes += collectionInfo.Value.DocumentsSize.SizeInBytes;
                            stats.Collections[collectionInfo.Key].RevisionsSize.SizeInBytes += collectionInfo.Value.RevisionsSize.SizeInBytes;
                            stats.Collections[collectionInfo.Key].TombstonesSize.SizeInBytes += collectionInfo.Value.TombstonesSize.SizeInBytes;
                        }
                        else
                        {
                            stats.Collections[collectionInfo.Key] = collectionInfo.Value;
                        }
                    }
                }

                return stats;
            }

            public RavenCommand<DetailedCollectionStatistics> CreateCommandForShard(int shard) =>
                new GetDetailedCollectionStatisticsOperation.GetDetailedCollectionStatisticsCommand();
        }
    }
}
