﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Raven.Client.Documents.Commands;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Server.Documents.Handlers.Processors.Revisions;
using Raven.Server.Documents.Sharding.Handlers.ContinuationTokens;
using Raven.Server.Documents.Sharding.Operations;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Utils;
using static Raven.Server.Documents.Sharding.Executors.AbstractExecutor;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Revisions
{
    internal class ShardedRevisionsHandlerProcessorForGetRevisionsBin : AbstractRevisionsHandlerProcessorForGetRevisionsBin<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedRevisionsHandlerProcessorForGetRevisionsBin([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override async ValueTask GetAndWriteRevisionsBinAsync(TransactionOperationContext context, int start, int pageSize)
        {
            var sw = Stopwatch.StartNew();

            var continuationToken = RequestHandler.ContinuationTokens.GetOrCreateContinuationToken(context, start, pageSize);
            var op = new ShardedGetRevisionsBinOperation(context, RequestHandler, continuationToken);
            var result = await RequestHandler.ShardExecutor.ExecuteParallelForAllAsync(op);
            await ShardedRevisionsHandlerProcessorForGetRevisions.WriteRevisionsResultAsync(context, RequestHandler, result, totalResult: null, continuationToken);

            DevelopmentHelper.ShardingToDo(DevelopmentHelper.TeamMember.Pawel, DevelopmentHelper.Severity.Minor, "RavenDB-19074 Implement AddPagingPerformanceHint. Make sure this gets passed real params");
            RequestHandler.AddPagingPerformanceHint(PagingOperationType.Revisions, "GetRevisionsBin", HttpContext.Request.QueryString.Value, 0, pageSize, sw.ElapsedMilliseconds, 0);
        }

        internal readonly struct ShardedGetRevisionsBinOperation : IShardedOperation<BlittableArrayResult, BlittableJsonReaderObject[]>
        {
            private readonly JsonOperationContext _context;
            private readonly ShardedDatabaseRequestHandler _handler;
            private readonly ShardedPagingContinuation _token;

            public ShardedGetRevisionsBinOperation(JsonOperationContext context, ShardedDatabaseRequestHandler handler, ShardedPagingContinuation token)
            {
                _context = context;
                _handler = handler;
                _token = token;
            }

            public HttpRequest HttpRequest => _handler.HttpContext.Request;

            public BlittableJsonReaderObject[] Combine(Dictionary<int, ShardExecutionResult<BlittableArrayResult>> results)
            {
                var list = new List<BlittableJsonReaderObject>();
                
                foreach (var item in _handler.DatabaseContext.Streaming.PagedShardedItemDocumentsByLastModified(
                             results, 
                             arr => arr.Results.Items.Select(i => (BlittableJsonReaderObject)i), 
                             _token))
                {
                    list.Add(item?.Clone(_context));
                }

                return list.ToArray();
            }

            public RavenCommand<BlittableArrayResult> CreateCommandForShard(int shardNumber) => new GetRevisionsBinEntryCommand(_token.Pages[shardNumber].Start, _token.PageSize);
        }
    }
}
