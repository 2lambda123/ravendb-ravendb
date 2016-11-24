using System;
using System.Collections.Generic;
using System.Linq;
using Raven.NewClient.Abstractions.Data;
using Raven.NewClient.Client.Commands;
using Raven.NewClient.Client.Data;
using Raven.NewClient.Client.Data.Queries;
using Raven.NewClient.Client.Shard;


namespace Raven.NewClient.Client.Document.Batches
{
    public class LazyTransformerLoadOperation<T> : ILazyOperation
    {
        private readonly string[] ids;
        private readonly string transformer;

        private readonly Dictionary<string, object> transformerParameters;

        private readonly LoadTransformerOperation loadTransformerOperation;
        private readonly bool singleResult;

        public LazyTransformerLoadOperation(string[] ids, string transformer, Dictionary<string, object> transformerParameters, LoadTransformerOperation loadTransformerOperation, bool singleResult)
        {
            this.ids = ids;
            this.transformer = transformer;
            this.transformerParameters = transformerParameters;
            this.loadTransformerOperation = loadTransformerOperation;
            this.singleResult = singleResult;
        }

        public GetRequest CreateRequest()
        {
            string query = "?" + string.Join("&", ids.Select(x => "id=" + Uri.EscapeDataString(x)).ToArray());
            if (string.IsNullOrEmpty(transformer) == false)
            {
                query += "&transformer=" + transformer;

                if (transformerParameters != null)
                    query = transformerParameters.Aggregate(query, (current, queryInput) => current + ("&" + string.Format("tp-{0}={1}", queryInput.Key, queryInput.Value)));
            }

            return new GetRequest
            {
                Url = "/docs",
                Query = query
            };
        }

        public object Result { get; set; }

        public QueryResult QueryResult { get; set; }

        public bool RequiresRetry { get; set; }

        public void HandleResponses(GetResponse[] responses, ShardStrategy shardStrategy)
        {
            var response = responses.OrderBy(x => x.Status).First(); // this way, 200 response is higher than 404
            HandleResponse(response);
        }

        public void HandleResponse(GetResponse response)
        {
            throw new NotImplementedException();
            /*if (response.RequestHasErrors())
            {
                throw new InvalidOperationException("Got bad status code: " + response.Status);
            }

            HandleRespose(new LoadResult
            {
                Includes = response.Result.Value<RavenJArray>("Includes").Cast<RavenJObject>().ToList(),
                Results = response.Result.Value<RavenJArray>("Results").Select(x => x as RavenJObject).ToList()
            });*/
        }

        public IDisposable EnterContext()
        {
            return null;
        }
        /*
        private void HandleRespose(LoadResult loadResult)
        {
            throw new NotImplementedException();
            T[] complete = loadTransformerOperation.Complete<T>(loadResult);
            if (singleResult)
            {
                Result = complete.Length > 0 ? complete[0] : (object)null;
                return;
            }

            Result = complete;
        }*/
    }
}
