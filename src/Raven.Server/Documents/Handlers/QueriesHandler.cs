﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Raven.Client;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;
using Raven.Client.Exceptions;
using Raven.Client.Exceptions.Documents.Indexes;
using Raven.Client.Extensions;
using Raven.Server.Documents.Handlers.Processors.Queries;
using Raven.Server.Documents.Indexes;
using Raven.Server.Documents.Operations;
using Raven.Server.Documents.Patch;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.AST;
using Raven.Server.Json;
using Raven.Server.NotificationCenter;
using Raven.Server.NotificationCenter.Notifications.Details;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.TrafficWatch;
using Sparrow.Json;

namespace Raven.Server.Documents.Handlers
{
    public class QueriesHandler : DatabaseRequestHandler
    {
        [RavenAction("/databases/*/queries", "POST", AuthorizationStatus.ValidUser, EndpointType.Read, DisableOnCpuCreditsExhaustion = true)]
        public Task Post()
        {
            return HandleQuery(HttpMethod.Post);
        }

        [RavenAction("/databases/*/queries", "GET", AuthorizationStatus.ValidUser, EndpointType.Read, DisableOnCpuCreditsExhaustion = true)]
        public Task Get()
        {
            return HandleQuery(HttpMethod.Get);
        }

        [RavenAction("/databases/*/queries", "PATCH", AuthorizationStatus.ValidUser, EndpointType.Write, DisableOnCpuCreditsExhaustion = true)]
        public async Task Patch()
        {
            using (var processor = new DatabaseQueriesHandlerProcessorForPatch(this)) 
                await processor.ExecuteAsync();
        }

        [RavenAction("/databases/*/queries/test", "PATCH", AuthorizationStatus.ValidUser, EndpointType.Write, DisableOnCpuCreditsExhaustion = true)]
        public async Task PatchTest()
        {
            using (var processor = new DatabaseQueriesHandlerProcessorForPatchTest(this))
                await processor.ExecuteAsync();
        }

        [RavenAction("/databases/*/queries", "DELETE", AuthorizationStatus.ValidUser, EndpointType.Write)]
        public async Task Delete()
        {
            using (var processor = new DatabaseQueriesHandlerProcessorForDelete(this))
                await processor.ExecuteAsync();
        }

        public async Task HandleQuery(HttpMethod httpMethod)
        {
            using (var tracker = new RequestTimeTracker(HttpContext, Logger, Database, "Query"))
            {
                try
                {
                    using (var token = CreateTimeLimitedQueryToken())
                    using (var queryContext = QueryOperationContext.Allocate(Database))
                    {
                        var debug = GetStringQueryString("debug", required: false);
                        if (string.IsNullOrWhiteSpace(debug) == false)
                        {
                            await Debug(queryContext, debug, token, tracker, httpMethod);
                            return;
                        }

                        await Query(queryContext, token, tracker, httpMethod);
                    }
                }
                catch (Exception e)
                {
                    if (tracker.Query == null)
                    {
                        string errorMessage;
                        if (e is EndOfStreamException || e is ArgumentException)
                        {
                            errorMessage = "Failed: " + e.Message;
                        }
                        else
                        {
                            errorMessage = "Failed: " +
                                           HttpContext.Request.Path.Value +
                                           e.ToString();
                        }
                        tracker.Query = errorMessage;
                        if (TrafficWatchManager.HasRegisteredClients)
                            AddStringToHttpContext(errorMessage, TrafficWatchChangeType.Queries);
                    }
                    throw;
                }
            }
        }

        private async Task FacetedQuery(IndexQueryServerSide indexQuery, QueryOperationContext queryContext, OperationCancelToken token)
        {
            var existingResultEtag = GetLongFromHeaders(Constants.Headers.IfNoneMatch);

            var result = await Database.QueryRunner.ExecuteFacetedQuery(indexQuery, existingResultEtag, queryContext, token);

            if (result.NotModified)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            HttpContext.Response.Headers[Constants.Headers.Etag] = CharExtensions.ToInvariantString(result.ResultEtag);

            long numberOfResults;
            await using (var writer = new AsyncBlittableJsonTextWriter(queryContext.Documents, ResponseBodyStream()))
            {
                numberOfResults = await writer.WriteFacetedQueryResultAsync(queryContext.Documents, result, token.Token);
            }

            Database.QueryMetadataCache.MaybeAddToCache(indexQuery.Metadata, result.IndexName);

            if (ShouldAddPagingPerformanceHint(numberOfResults))
                AddPagingPerformanceHint(PagingOperationType.Queries, $"{nameof(FacetedQuery)} ({result.IndexName})", $"{indexQuery.Metadata.QueryText}\n{indexQuery.QueryParameters}", numberOfResults, indexQuery.PageSize, result.DurationInMs, -1);
        }

        private async Task Query(QueryOperationContext queryContext, OperationCancelToken token, RequestTimeTracker tracker, HttpMethod method)
        {
            var addSpatialProperties = GetBoolValueQueryString("addSpatialProperties", required: false) ?? false;
            var indexQueryReader = new IndexQueryReader(GetStart(), GetPageSize(), HttpContext, RequestBodyStream(), Database.QueryMetadataCache, Database, addSpatialProperties);
            var indexQuery = await indexQueryReader.GetIndexQueryAsync(queryContext.Documents, method, tracker);

            indexQuery.Diagnostics = GetBoolValueQueryString("diagnostics", required: false) ?? false ? new List<string>() : null;
            indexQuery.AddTimeSeriesNames = GetBoolValueQueryString("addTimeSeriesNames", false) ?? false;
            indexQuery.DisableAutoIndexCreation = GetBoolValueQueryString("disableAutoIndexCreation", false) ?? false;

            queryContext.WithQuery(indexQuery.Metadata);

            if (TrafficWatchManager.HasRegisteredClients)
                TrafficWatchQuery(indexQuery);

            var existingResultEtag = GetLongFromHeaders(Constants.Headers.IfNoneMatch);

            if (indexQuery.Metadata.HasFacet)
            {
                await FacetedQuery(indexQuery, queryContext, token);
                return;
            }

            if (indexQuery.Metadata.HasSuggest)
            {
                await SuggestQuery(indexQuery, queryContext, token);
                return;
            }

            var metadataOnly = GetBoolValueQueryString("metadataOnly", required: false) ?? false;
            var shouldReturnServerSideQuery = GetBoolValueQueryString("includeServerSideQuery", required: false) ?? false;

            DocumentQueryResult result;
            try
            {
                result = await Database.QueryRunner.ExecuteQuery(indexQuery, queryContext, existingResultEtag, token).ConfigureAwait(false);
            }
            catch (IndexDoesNotExistException)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (result.NotModified)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            HttpContext.Response.Headers[Constants.Headers.Etag] = CharExtensions.ToInvariantString(result.ResultEtag);

            long numberOfResults;
            long totalDocumentsSizeInBytes;
            await using (var writer = new AsyncBlittableJsonTextWriter(queryContext.Documents, ResponseBodyStream()))
            {
                result.Timings = indexQuery.Timings?.ToTimings();
                (numberOfResults, totalDocumentsSizeInBytes) = await writer.WriteDocumentQueryResultAsync(queryContext.Documents, result, metadataOnly, WriteAdditionalData(indexQuery, shouldReturnServerSideQuery), token.Token);
            }

            Database.QueryMetadataCache.MaybeAddToCache(indexQuery.Metadata, result.IndexName);

            if (ShouldAddPagingPerformanceHint(numberOfResults))
                AddPagingPerformanceHint(PagingOperationType.Queries, $"{nameof(Query)} ({result.IndexName})", $"{indexQuery.Metadata.QueryText}\n{indexQuery.QueryParameters}", numberOfResults, indexQuery.PageSize, result.DurationInMs, totalDocumentsSizeInBytes);
        }

        private Action<AbstractBlittableJsonTextWriter> WriteAdditionalData(IndexQueryServerSide indexQuery, bool shouldReturnServerSideQuery)
        {
            if (indexQuery.Diagnostics == null && shouldReturnServerSideQuery == false)
                return null;

            return w =>
            {
                if (shouldReturnServerSideQuery)
                {
                    w.WriteComma();
                    w.WritePropertyName(nameof(indexQuery.ServerSideQuery));
                    w.WriteString(indexQuery.ServerSideQuery);
                }

                if (indexQuery.Diagnostics != null)
                {
                    w.WriteComma();
                    w.WriteArray(nameof(indexQuery.Diagnostics), indexQuery.Diagnostics);
                }
            };
        }

        private async Task SuggestQuery(IndexQueryServerSide indexQuery, QueryOperationContext queryContext, OperationCancelToken token)
        {
            var existingResultEtag = GetLongFromHeaders(Constants.Headers.IfNoneMatch);
            var result = await Database.QueryRunner.ExecuteSuggestionQuery(indexQuery, queryContext, existingResultEtag, token);
            if (result.NotModified)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            HttpContext.Response.Headers[Constants.Headers.Etag] = CharExtensions.ToInvariantString(result.ResultEtag);

            long numberOfResults;
            long totalDocumentsSizeInBytes;
            await using (var writer = new AsyncBlittableJsonTextWriter(queryContext.Documents, ResponseBodyStream()))
            {
                (numberOfResults, totalDocumentsSizeInBytes) = await writer.WriteSuggestionQueryResultAsync(queryContext.Documents, result, token.Token);
            }

            if (ShouldAddPagingPerformanceHint(numberOfResults))
                AddPagingPerformanceHint(PagingOperationType.Queries, $"{nameof(SuggestQuery)} ({result.IndexName})", indexQuery.Query, numberOfResults, indexQuery.PageSize, result.DurationInMs, totalDocumentsSizeInBytes);
        }

        private async Task Explain(QueryOperationContext queryContext, RequestTimeTracker tracker, HttpMethod method)
        {
            var indexQueryReader = new IndexQueryReader(GetStart(), GetPageSize(), HttpContext, RequestBodyStream(), Database.QueryMetadataCache, Database);
            var indexQuery = await indexQueryReader.GetIndexQueryAsync(queryContext.Documents, method, tracker);


            var explanations = Database.QueryRunner.ExplainDynamicIndexSelection(indexQuery, out string indexName);

            await using (var writer = new AsyncBlittableJsonTextWriter(queryContext.Documents, ResponseBodyStream()))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("IndexName");
                writer.WriteString(indexName);
                writer.WriteComma();
                writer.WriteArray(queryContext.Documents, "Results", explanations, (w, c, explanation) => w.WriteExplanation(queryContext.Documents, explanation));

                writer.WriteEndObject();
            }
        }

        private async Task ServerSideQuery(QueryOperationContext queryContext, RequestTimeTracker tracker, HttpMethod method)
        {
            var indexQueryReader = new IndexQueryReader(GetStart(), GetPageSize(), HttpContext, RequestBodyStream(), Database.QueryMetadataCache, Database);
            var indexQuery = await indexQueryReader.GetIndexQueryAsync(queryContext.Documents, method, tracker);

            await using (var writer = new AsyncBlittableJsonTextWriter(queryContext.Documents, ResponseBodyStream()))
            {
                writer.WriteStartObject();
                writer.WritePropertyName(nameof(indexQuery.ServerSideQuery));
                writer.WriteString(indexQuery.ServerSideQuery);

                writer.WriteEndObject();
            }
        }

        private async Task Debug(QueryOperationContext queryContext, string debug, OperationCancelToken token, RequestTimeTracker tracker, HttpMethod method)
        {
            if (string.Equals(debug, "entries", StringComparison.OrdinalIgnoreCase))
            {
                var ignoreLimit = GetBoolValueQueryString("ignoreLimit", required: false) ?? false;
                await IndexEntries(queryContext, token, tracker, method, ignoreLimit);
                return;
            }

            if (string.Equals(debug, "explain", StringComparison.OrdinalIgnoreCase))
            {
                await Explain(queryContext, tracker, method);
                return;
            }

            if (string.Equals(debug, "serverSideQuery", StringComparison.OrdinalIgnoreCase))
            {
                await ServerSideQuery(queryContext, tracker, method);
                return;
            }

            throw new NotSupportedException($"Not supported query debug operation: '{debug}'");
        }

        private async Task IndexEntries(QueryOperationContext queryContext, OperationCancelToken token, RequestTimeTracker tracker, HttpMethod method, bool ignoreLimit)
        {
            var indexQueryReader = new IndexQueryReader(GetStart(), GetPageSize(), HttpContext, RequestBodyStream(), Database.QueryMetadataCache, Database);
            var indexQuery = await indexQueryReader.GetIndexQueryAsync(queryContext.Documents, method, tracker);

            var existingResultEtag = GetLongFromHeaders(Constants.Headers.IfNoneMatch);

            var result = await Database.QueryRunner.ExecuteIndexEntriesQuery(indexQuery, queryContext, ignoreLimit, existingResultEtag, token);

            if (result.NotModified)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotModified;
                return;
            }

            HttpContext.Response.Headers[Constants.Headers.Etag] = CharExtensions.ToInvariantString(result.ResultEtag);

            await using (var writer = new AsyncBlittableJsonTextWriter(queryContext.Documents, ResponseBodyStream()))
            {
                await writer.WriteIndexEntriesQueryResultAsync(queryContext.Documents, result, token.Token);
            }
        }
    }
}
