﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Raven.Client;
using Raven.Client.Http;
using Raven.Server.Documents.Handlers.Processors.Indexes;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Sharding.Executors;
using Raven.Server.Documents.Sharding.Operations;
using Raven.Server.Documents.Sharding.Streaming;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Indexes
{
    internal sealed class ShardedIndexHandlerProcessorForTerms : AbstractIndexHandlerProcessorForTerms<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedIndexHandlerProcessorForTerms([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override async ValueTask<TermsQueryResultServerSide> GetTermsAsync(string indexName, string field, string fromValue, int pageSize, long? resultEtag, OperationCancelToken token)
        {
            var op = new ShardedGetTermsOperation(RequestHandler, indexName, field, fromValue, pageSize, resultEtag);
            var result = await RequestHandler.ShardExecutor.ExecuteParallelForAllAsync(op, token.Token);

            if (result.StatusCode == (int)HttpStatusCode.NotModified)
                return TermsQueryResultServerSide.NotModifiedResult;

            HttpContext.Response.Headers[Constants.Headers.Etag] = "\"" + result.CombinedEtag + "\"";
            result.Result.ResultEtag = Convert.ToInt64(result.CombinedEtag);

            return result.Result;
        }


        private readonly struct ShardedGetTermsOperation : IShardedReadOperation<TermsQueryResultServerSide>
        {
            private readonly ShardedDatabaseRequestHandler _handler;
            private readonly string _indexName;
            private readonly string _field;
            private readonly string _fromValue;
            private readonly int _pageSize;

            public ShardedGetTermsOperation(ShardedDatabaseRequestHandler handler, string indexName, string field, string fromValue, int pageSize, long? resultEtag)
            {
                _handler = handler;
                _indexName = indexName;
                _field = field;
                _fromValue = fromValue;
                _pageSize = pageSize;
                ExpectedEtag = Convert.ToString(resultEtag);
            }

            public HttpRequest HttpRequest => _handler.HttpContext.Request;

            public string ExpectedEtag { get; }

            public TermsQueryResultServerSide CombineResults(Dictionary<int, ShardExecutionResult<TermsQueryResultServerSide>> results)
            {
                var pageSize = _pageSize;
                var terms = new SortedSet<string>();
                foreach (var res in _handler.DatabaseContext.Streaming.CombinedResults(results, r => r.Terms, TermsComparer.Instance))
                {
                    if (terms.Add(res.Item))
                        pageSize--;

                    if (pageSize <= 0)
                        break;
                }

                return new TermsQueryResultServerSide { IndexName = results.First().Value.Result.IndexName, Terms = terms.ToList() };
            }

            public RavenCommand<TermsQueryResultServerSide> CreateCommandForShard(int shardNumber) => new GetIndexTermsCommand(indexName: _indexName, field: _field, _fromValue, _pageSize);

            public string CombineCommandsEtag(Dictionary<int, ShardExecutionResult<TermsQueryResultServerSide>> commands)
            {
                var etags = ComputeHttpEtags.EnumerateEtags(commands);

                long combinedEtag = 0;
                foreach (var res in etags)
                {
                    var etag = Convert.ToInt64(res);
                    combinedEtag ^= etag;
                }

                return Convert.ToString(combinedEtag);
            }
        }

        private class TermsComparer : Comparer<ShardStreamItem<string>>
        {
            public override int Compare(ShardStreamItem<string> x,
                ShardStreamItem<string> y)
            {
                return string.CompareOrdinal(x?.Item, y?.Item);
            }

            public static readonly TermsComparer Instance = new();
        }
    }
}
