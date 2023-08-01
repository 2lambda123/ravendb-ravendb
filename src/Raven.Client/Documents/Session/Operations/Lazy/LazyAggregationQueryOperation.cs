using System;
using System.Collections.Generic;
using System.Net.Http;
using Raven.Client.Documents.Commands.MultiGet;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Queries.Facets;
using Raven.Client.Json.Serialization;
using Sparrow.Json;

namespace Raven.Client.Documents.Session.Operations.Lazy
{
    internal sealed class LazyAggregationQueryOperation : ILazyOperation
    {
        private readonly InMemoryDocumentSessionOperations _session;
        private readonly IndexQuery _indexQuery;
        private readonly Action<QueryResult> _invokeAfterQueryExecuted;
        private readonly Func<QueryResult, Dictionary<string, FacetResult>> _processResults;

        public LazyAggregationQueryOperation(InMemoryDocumentSessionOperations session, IndexQuery indexQuery, Action<QueryResult> invokeAfterQueryExecuted, Func<QueryResult, Dictionary<string, FacetResult>> processResults)
        {
            _session = session;
            _indexQuery = indexQuery;
            _invokeAfterQueryExecuted = invokeAfterQueryExecuted;
            _processResults = processResults;
        }

        public GetRequest CreateRequest(JsonOperationContext ctx)
        {
            return new GetRequest
            {
                Url = "/queries",
                Method = HttpMethod.Post,
                Query = $"?queryHash={_indexQuery.GetQueryHash(ctx, _session.Conventions, _session.JsonSerializer)}",
                Content = new IndexQueryContent(_session.Conventions, _indexQuery)
            };
        }

        public object Result { get; private set; }
        public QueryResult QueryResult { get; private set; }
        public bool RequiresRetry { get; private set; }

        public void HandleResponse(GetResponse response)
        {
            if (response.ForceRetry)
            {
                Result = null;
                RequiresRetry = true;
                return;
            }

            var queryResult = JsonDeserializationClient.QueryResult((BlittableJsonReaderObject)response.Result);

            HandleResponse(queryResult);
        }

        private void HandleResponse(QueryResult queryResult)
        {
            Result = _processResults(queryResult);
            QueryResult = queryResult;
        }
    }
}
