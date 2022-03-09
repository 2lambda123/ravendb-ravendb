﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Server.Documents.Sharding;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.ShardedHandlers.ShardedCommands
{
    public abstract class ShardedBaseCommand<T> : RavenCommand<T>, IDisposable
    {
        protected readonly ShardedRequestHandler Handler;
        public BlittableJsonReaderObject Content;
        public readonly Dictionary<string, string> Headers = new Dictionary<string, string>();
        public string Url;
        public readonly HttpMethod Method;

        public HttpResponseMessage Response;
        private readonly IDisposable _disposable;
        public TransactionOperationContext Context;

        public override bool IsReadRequest => false;

        protected ShardedBaseCommand(ShardedRequestHandler handler, Headers headers, BlittableJsonReaderObject content = null)
        {
            _disposable = handler.ContextPool.AllocateOperationContext(out Context);

            Handler = handler;
            Method = handler.Method;
            Url = handler.RelativeShardUrl;
            Content = content;

            handler.AddHeaders(this, headers);
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            url = $"{node.Url}/databases/{node.Database}{Url}";
            var message = new HttpRequestMessage
            {
                Method = Method, 
                Content = Content == null ? null : new BlittableJsonContent(async (stream)=> await Content.WriteJsonToAsync(stream)),
            };
            foreach ((string key, string value) in Headers)
            {
                if (value == null) //TODO sharding: make sure it is okay to skip null
                    continue;

                message.Headers.TryAddWithoutValidation(key, value);
            }

            return message;
        }

        public override Task<ResponseDisposeHandling> ProcessResponse(JsonOperationContext context, HttpCache cache, HttpResponseMessage response, string url)
        {
            Response = response;
            return base.ProcessResponse(context, cache, response, url);
        }

        public void Dispose()
        {
            _disposable?.Dispose();
        }
    }
    
    public enum Headers
    {
        None,
        IfMatch,
        IfNoneMatch,
    }
}
