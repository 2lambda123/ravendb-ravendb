﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Session.Operations;
using Raven.Client.Exceptions.Documents.Indexes;
using Raven.Client.Extensions;
using Raven.Client.Http;
using Raven.Server.Documents.Commands.Streaming;
using Raven.Server.Documents.Handlers;
using Raven.Server.Documents.Handlers.Processors.Streaming;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Replication.Senders;
using Raven.Server.Documents.Sharding.Executors;
using Raven.Server.Documents.Sharding.Operations;
using Raven.Server.Documents.Sharding.Queries;
using Raven.Server.NotificationCenter;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow;
using Sparrow.Json;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Streaming
{
    internal class ShardedStreamingHandlerProcessorForGetStreamQuery : AbstractStreamingHandlerProcessorForGetStreamQuery<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedStreamingHandlerProcessorForGetStreamQuery([NotNull] ShardedDatabaseRequestHandler requestHandler, HttpMethod method) : base(requestHandler, method)
        {
        }

        protected override RequestTimeTracker GetTimeTracker()
        {
            return new RequestTimeTracker(HttpContext, Logger, RequestHandler.DatabaseContext.NotificationCenter, RequestHandler.DatabaseContext.Configuration, "StreamQuery", doPerformanceHintIfTooLong: false);
        }

        protected override async ValueTask<BlittableJsonReaderObject> GetDocumentDataAsync(TransactionOperationContext context, string fromDocument)
        {
            var shard = RequestHandler.DatabaseContext.GetShardNumberFor(context, fromDocument);

            var docs = await RequestHandler.ShardExecutor.ExecuteSingleShardAsync(context,
                new GetDocumentsCommand(new[] { fromDocument }, includes: null, metadataOnly: false), shard);
            return (BlittableJsonReaderObject)docs.Results[0];
        }

        protected override IDisposable AllocateContext(out TransactionOperationContext context)
        {
            return ContextPool.AllocateOperationContext(out context);
        }

        protected override QueryMetadataCache GetQueryMetadataCache()
        {
            return RequestHandler.DatabaseContext.QueryMetadataCache;
        }

        protected override IStreamQueryResultWriter<BlittableJsonReaderObject> GetBlittableQueryResultWriter(string format, bool isDebug, JsonOperationContext context, HttpResponse response, Stream responseBodyStream, bool fromSharded,
            string[] propertiesArray, string fileNamePrefix = null)
        {
            if (IsCsvFormat(format))
            {
                //does not write query stats to stream
                return new StreamCsvBlittableQueryResultWriter(response, responseBodyStream, propertiesArray, fileNamePrefix);
            }

            if (isDebug)
            {
                ThrowUnsupportedException($"You have selected \"{format}\" file format, which is not supported.");
            }

            return new StreamBlittableDocumentQueryResultWriter(responseBodyStream, context);
        }

        private async ValueTask<(IEnumerator<BlittableJsonReaderObject>, StreamQueryStatistics)> ExecuteQueryAsync(TransactionOperationContext context, IndexQueryServerSide query, string debug, bool ignoreLimit, OperationCancelToken token)
        {
            var indexName = AbstractQueryRunner.GetIndexName(query);

            using (RequestHandler.DatabaseContext.QueryRunner.MarkQueryAsRunning(indexName, query, token))
            {
                var queryProcessor = new ShardedQueryStreamProcessor(context, RequestHandler, query, debug, ignoreLimit, token.Token);

                await queryProcessor.InitializeAsync();

                return await queryProcessor.ExecuteShardedOperations(null); // TODO [ppekrol]
            }
        }

        protected override async ValueTask ExecuteAndWriteQueryStreamAsync(TransactionOperationContext context, IndexQueryServerSide query, string format,
            string[] propertiesArray, string fileNamePrefix, bool ignoreLimit, bool _, OperationCancelToken token)
        {
            //writer is blittable->document or blittable->csv

            await using (var writer = GetBlittableQueryResultWriter(format, isDebug: false, context, HttpContext.Response, RequestHandler.ResponseBodyStream(),
                             fromSharded: false, propertiesArray, fileNamePrefix))
            {
                try
                {
                    var (results, queryStatistics) = await ExecuteQueryAsync(context, query, null, ignoreLimit, token);

                    var queryResult = new StreamDocumentIndexEntriesQueryResult(HttpContext.Response, writer, token); // writes blittable docs as blittable docs
                    queryResult.TotalResults = queryStatistics.TotalResults;
                    queryResult.IndexName = queryStatistics.IndexName;
                    queryResult.IndexTimestamp = queryStatistics.IndexTimestamp;
                    queryResult.IsStale = queryStatistics.IsStale;
                    queryResult.ResultEtag = queryStatistics.ResultEtag;

                    foreach (BlittableJsonReaderObject doc in results)
                    {
                        await queryResult.AddResultAsync(doc, token.Token);
                    }

                    queryResult.Flush();
                }
                catch (IndexDoesNotExistException)
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    await writer.WriteErrorAsync($"Index {query.Metadata.IndexName} does not exist");
                }
            }
        }

        protected override async ValueTask ExecuteAndWriteIndexQueryStreamEntriesAsync(TransactionOperationContext context, IndexQueryServerSide query, string format, string debug, string[] propertiesArray,
            string fileNamePrefix, bool ignoreLimit, bool _, OperationCancelToken token)
        {
            //from debug

            await using (var writer = GetBlittableQueryResultWriter(format, isDebug: true, context, HttpContext.Response, RequestHandler.ResponseBodyStream(), fromSharded: false, propertiesArray,
                             fileNamePrefix))
            {
                try
                {
                    var (results, _) = await ExecuteQueryAsync(context, query, debug, ignoreLimit, token);

                    var queryResult = new StreamDocumentIndexEntriesQueryResult(HttpContext.Response, writer, token);

                    foreach (BlittableJsonReaderObject doc in results)
                    {
                        await queryResult.AddResultAsync(doc, token.Token);
                    }

                    queryResult.Flush();
                }
                catch (IndexDoesNotExistException)
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    await writer.WriteErrorAsync($"Index {query.Metadata.IndexName} does not exist");
                }
            }
        }

        public class DocumentBlittableLastModifiedComparer : Comparer<BlittableJsonReaderObject>
        {
            public override int Compare(BlittableJsonReaderObject x, BlittableJsonReaderObject y)
            {
                return y.GetMetadata().GetLastModified().CompareTo(x.GetMetadata().GetLastModified());
            }

            public static DocumentBlittableLastModifiedComparer Instance = new();
        }
    }

    public readonly struct ShardedStreamQueryOperation : IShardedOperation<StreamResult, (IEnumerator<BlittableJsonReaderObject>, StreamQueryStatistics)>
    {
        private readonly HttpContext _httpContext;
        private readonly Func<(JsonOperationContext, IDisposable)> _allocateJsonContext;
        private readonly IComparer<BlittableJsonReaderObject> _comparer;
        private readonly Dictionary<int, PostQueryStreamCommand> _queryStreamCommands;
        private readonly long _skip;
        private readonly long _take;
        private readonly CancellationToken _token;

        public ShardedStreamQueryOperation(HttpContext httpContext, Func<(JsonOperationContext, IDisposable)> allocateJsonContext, IComparer<BlittableJsonReaderObject> comparer, Dictionary<int, PostQueryStreamCommand> queryStreamCommands, long skip, long take, CancellationToken token)
        {
            _httpContext = httpContext;
            _allocateJsonContext = allocateJsonContext;
            _comparer = comparer;
            _queryStreamCommands = queryStreamCommands;
            _skip = skip;
            _take = take;
            _token = token;
            ExpectedEtag = null;
        }

        public HttpRequest HttpRequest => _httpContext.Request;

        public RavenCommand<StreamResult> CreateCommandForShard(int shardNumber) => _queryStreamCommands[shardNumber];

        public string ExpectedEtag { get; }

        public (IEnumerator<BlittableJsonReaderObject>, StreamQueryStatistics) Combine(Dictionary<int, ShardExecutionResult<StreamResult>> results)
        {
            var queryStats = new StreamQueryStatistics();

            var mergedEnumerator = new MergedEnumerator<BlittableJsonReaderObject>(_comparer);

            foreach (var streamResult in results.Values)
            {
                var qs = new StreamQueryStatistics();
                var enumerator = new StreamOperation.YieldStreamResults(_allocateJsonContext, streamResult.Result, isQueryStream: true, isTimeSeriesStream: false, isAsync: false, qs, _token);
                enumerator.Initialize();
                queryStats.TotalResults += qs.TotalResults;
                queryStats.IndexName = qs.IndexName;
                queryStats.IsStale |= qs.IsStale;
                queryStats.ResultEtag = Hashing.Combine(queryStats.ResultEtag, qs.ResultEtag);

                if (queryStats.IndexTimestamp < qs.IndexTimestamp)
                {
                    queryStats.IndexTimestamp = qs.IndexTimestamp;
                }

                mergedEnumerator.AddEnumerator(enumerator);
            }

            return (ApplySkipTake(mergedEnumerator, _skip, _take), queryStats);
        }

        private IEnumerator<BlittableJsonReaderObject> ApplySkipTake(MergedEnumerator<BlittableJsonReaderObject> mergedEnumerator, long skip, long take)
        {
            foreach (var item in mergedEnumerator)
            {
                if (skip-- > 0)
                    continue;

                if (take-- <= 0)
                    yield break;

                yield return item;
            }
        }
    }
}
