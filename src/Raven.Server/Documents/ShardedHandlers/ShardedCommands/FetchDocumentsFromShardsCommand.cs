﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Raven.Server.Documents.Sharding;
using Raven.Server.ServerWide.Context;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.ShardedHandlers.ShardedCommands
{
    public class FetchDocumentsFromShardsCommand : ShardedCommand, IDisposable
    {
        public List<int> PositionMatches;

        private readonly IDisposable _disposable;
        public TransactionOperationContext Context;

        public FetchDocumentsFromShardsCommand(ShardedRequestHandler handler, List<string> ids, StringBuilder query) : base(handler, ShardedCommands.Headers.None)
        {
            _disposable = handler.ContextPool.AllocateOperationContext(out Context);

            if (handler.Method == HttpMethod.Post)
            {
                var body = new DynamicJsonValue
                {
                    ["Ids"] = new DynamicJsonArray(ids)
                };

                Content = Context.ReadObject(body, nameof(FetchDocumentsFromShardsCommand));
                return;
            }

            foreach (var id in ids)
            {
                query.Append("&id=").Append(Uri.EscapeDataString(id));
            }

            Url = query.ToString();
        }

        public void Dispose()
        {
            _disposable?.Dispose();
        }
    }
}
