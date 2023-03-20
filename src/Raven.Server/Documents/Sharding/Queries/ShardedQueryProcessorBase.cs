﻿using System;
using System.Threading;
using Raven.Client.Documents.Queries;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.Timings;
using Raven.Server.Documents.Sharding.Commands.Querying;
using Raven.Server.Documents.Sharding.Comparers;
using Raven.Server.Documents.Sharding.Handlers;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.Sharding.Queries;

/// <summary>
/// A struct that we use to hold state and break the process
/// of handling a sharded query into distinct steps
/// </summary>
public abstract class ShardedQueryProcessorBase<TCombinedResult> : AbstractShardedQueryProcessor<ShardedQueryCommand, QueryResult, TCombinedResult>
    where TCombinedResult : QueryResultServerSide<BlittableJsonReaderObject>
{
    protected ShardedQueryProcessorBase(
        TransactionOperationContext context,
        ShardedDatabaseRequestHandler requestHandler,
        IndexQueryServerSide query,
        long? existingResultEtag,
        bool metadataOnly,
        bool indexEntriesOnly,
        bool ignoreLimit,
        CancellationToken token)
        : base(context, requestHandler, query, metadataOnly, indexEntriesOnly, ignoreLimit, existingResultEtag, token)
    {

    }

    protected override ShardedQueryCommand CreateCommand(int shardNumber, BlittableJsonReaderObject query, QueryTimingsScope scope) => CreateShardedQueryCommand(shardNumber, query, scope);

    protected void ApplyPaging(ref TCombinedResult result, QueryTimingsScope scope)
    {
        QueryTimingsScope pagingScope = null;

        if (Query.Offset is > 0)
        {
            using (GetPagingScope())
            {
                var count = Math.Min(result.Results.Count, Math.Min(Query.Offset ?? 0, int.MaxValue));
                result.Results.RemoveRange(0, (int)count);
            }
        }

        var limit = Math.Min(Query.Limit ?? long.MaxValue, Query.FilterLimit ?? long.MaxValue);
        if (result.Results.Count > limit)
        {
            using (GetPagingScope())
            {
                long index = Math.Min(limit, int.MaxValue);
                long count = result.Results.Count - limit;
                if (count > int.MaxValue)
                    count = int.MaxValue; //todo: Grisha: take a look
                result.Results.RemoveRange((int)index, (int)count);
            }
        }

        QueryTimingsScope GetPagingScope()
        {
            if (scope == null)
                return null;

            pagingScope ??= scope.For(nameof(QueryTimingsScope.Names.Paging), start: false);
            pagingScope.Start();

            return pagingScope;
        }
    }

    protected void ReduceResults(ref TCombinedResult result, QueryTimingsScope scope)
    {
        if (IsMapReduceIndex == false && IsAutoMapReduceQuery == false)
            return;

        using (scope?.For(nameof(QueryTimingsScope.Names.Reduce)))
        {
            var merger = CreateMapReduceQueryResultsMerger(result);
            result.Results = merger.Merge();

            var orderByFields = OrderByFields?.ToArray() ?? Query.Metadata.OrderBy;
            if (orderByFields?.Length > 0)
            {
                // apply ordering after the re-reduce of a map-reduce index
                result.Results.Sort(new DocumentsComparer(orderByFields, extractFromData: true));
            }
        }
    }

    protected virtual ShardedMapReduceQueryResultsMerger CreateMapReduceQueryResultsMerger(TCombinedResult result) => new(result.Results, RequestHandler.DatabaseContext.Indexes, result.IndexName, IsAutoMapReduceQuery, Context, Token);
}
