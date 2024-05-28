﻿using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client;
using Raven.Client.Documents.Commands;
using Raven.Client.Json;
using Raven.Server.Documents.Handlers.Processors.Revisions;
using Raven.Server.Documents.Sharding.Handlers.ContinuationTokens;
using Raven.Server.Documents.Sharding.Operations;
using Raven.Server.Json;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Utils;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Revisions
{
    internal sealed class ShardedRevisionsHandlerProcessorForGetRevisions : AbstractRevisionsHandlerProcessorForGetRevisions<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedRevisionsHandlerProcessorForGetRevisions([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override async ValueTask GetRevisionByChangeVectorAsync(TransactionOperationContext context, Microsoft.Extensions.Primitives.StringValues changeVectors, bool metadataOnly, CancellationToken token)
        {
            var etag = RequestHandler.GetStringFromHeaders(Constants.Headers.IfNoneMatch);
            var cmd = new ShardedGetRevisionsByChangeVectorsOperation(HttpContext, changeVectors.ToArray(), metadataOnly, context, etag);
            var result = await RequestHandler.ShardExecutor.ExecuteParallelForAllAsync(cmd, token);

            if (result.StatusCode == (int)HttpStatusCode.NotModified)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            if (result.Result == null && changeVectors.Count == 1)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (string.IsNullOrEmpty(result.CombinedEtag) == false)
                HttpContext.Response.Headers[Constants.Headers.Etag] = "\"" + result.CombinedEtag + "\"";

            var res = result.Result;
            var blittable = RequestHandler.GetBoolValueQueryString("blittable", required: false) ?? false;

            if (blittable)
            {
                WriteRevisionsBlittable(context, res, out long numberOfResults, out long totalDocumentsSizeInBytes);
            }
            else
            {
                await WriteRevisionsResultAsync(context, RequestHandler, res, totalResult: null);
            }

            AddPagingPerformanceHint(PagingOperationType.Revisions, "", "", 0, 0, 0, 0);
        }

        protected override async ValueTask GetRevisionsAsync(TransactionOperationContext context, string id, DateTime? before, int start, int pageSize, bool metadataOnly, CancellationToken token)
        {
            GetRevisionsCommand cmd;
            if (before.HasValue)
            {
                cmd = new GetRevisionsCommand(id, before.Value);
            }
            else
            {
                cmd = new GetRevisionsCommand(id, start, pageSize, metadataOnly);
            }

            int shardNumber = RequestHandler.DatabaseContext.GetShardNumberFor(context, id);
            var result = await RequestHandler.ExecuteSingleShardAsync(context, cmd, shardNumber, token);

            string actualEtag = cmd.Etag;
            if (NotModified(actualEtag))
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            if (string.IsNullOrEmpty(actualEtag) == false)
                HttpContext.Response.Headers[Constants.Headers.Etag] = "\"" + actualEtag + "\"";

            var array = result.Results.Items.Select(x => (BlittableJsonReaderObject)x).ToArray();
            await WriteRevisionsResultAsync(context, RequestHandler, array, result.TotalResults);

            AddPagingPerformanceHint(PagingOperationType.Revisions, "", "", 0, 0, 0, 0);
        }

        public static async ValueTask WriteRevisionsResultAsync(JsonOperationContext context, ShardedDatabaseRequestHandler handler, BlittableJsonReaderObject[] array, long? totalResult, ContinuationToken continuationToken = null)
        {
            await using (var writer = new AsyncBlittableJsonTextWriter(context, handler.ResponseBodyStream()))
            {
                writer.WriteStartObject();

                writer.WriteArray(nameof(BlittableArrayResult.Results), array);

                if (totalResult.HasValue)
                {
                    writer.WriteComma();
                    writer.WritePropertyName(nameof(BlittableArrayResult.TotalResults));
                    writer.WriteInteger(totalResult.Value);
                }

                if (continuationToken != null && array.Length != 0)
                {
                    writer.WriteComma();
                    writer.WriteContinuationToken(context, continuationToken);
                }

                writer.WriteEndObject();
            }
        }

        private void AddPagingPerformanceHint(PagingOperationType operation, string action, string details, long numberOfResults, int pageSize, long duration,
            long totalDocumentsSizeInBytes)
        {
            DevelopmentHelper.ShardingToDo(DevelopmentHelper.TeamMember.Pawel, DevelopmentHelper.Severity.Minor, "RavenDB-19074 Implement AddPagingPerformanceHint, collect and pass real params");
            RequestHandler.AddPagingPerformanceHint(operation, action, details, numberOfResults, pageSize, duration, totalDocumentsSizeInBytes);
        }
    }
}
