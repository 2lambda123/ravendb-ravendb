﻿using System;
using System.Net;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client;
using Raven.Client.Documents.Operations.TimeSeries;
using Raven.Server.Documents.Includes;
using Raven.Server.Extensions;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.TimeSeries
{
    internal sealed class TimeSeriesHandlerProcessorForGetTimeSeries : AbstractTimeSeriesHandlerProcessorForGetTimeSeries<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public TimeSeriesHandlerProcessorForGetTimeSeries([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override ValueTask<TimeSeriesRangeResult> GetTimeSeriesAndWriteAsync(DocumentsOperationContext context, string docId, string name, DateTime @from, DateTime to,
            int start, int pageSize, bool includeDoc, bool includeTags, bool fullResults)
        {
            using (context.OpenReadTransaction())
            {
                var stats = context.DocumentDatabase.DocumentsStorage.TimeSeriesStorage.Stats.GetStats(context, docId, name);
                if (stats == default)
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    return ValueTask.FromResult<TimeSeriesRangeResult>(null);
                }

                bool shouldGetMissingIncludes = RequestHandler.HttpContext.Request.IsFromOrchestrator();

                var includesCommand = includeDoc || includeTags
                    ? new IncludeDocumentsDuringTimeSeriesLoadingCommand(context, docId, includeDoc, includeTags, shouldGetMissingIncludes)
                    : null;

                bool incrementalTimeSeries = CheckIfIncrementalTs(name);

                var rangeResult = incrementalTimeSeries
                    ? GetIncrementalTimeSeriesRange(context, docId, name, from, to, ref start, ref pageSize, includesCommand, fullResults)
                    : GetTimeSeriesRange(context, docId, name, from, to, ref start, ref pageSize, includesCommand);
                
                var hash = rangeResult?.Hash ?? string.Empty;

                var etag = RequestHandler.GetStringFromHeaders(Constants.Headers.IfNoneMatch);
                if (etag == hash)
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.NotModified;
                    return ValueTask.FromResult<TimeSeriesRangeResult>(null);
                }

                HttpContext.Response.Headers[Constants.Headers.Etag] = "\"" + hash + "\"";

                if (from <= stats.Start && to >= stats.End)
                {
                    rangeResult.TotalResults = stats.Count;
                }

                return ValueTask.FromResult(rangeResult);
            }
        }
    }
}
