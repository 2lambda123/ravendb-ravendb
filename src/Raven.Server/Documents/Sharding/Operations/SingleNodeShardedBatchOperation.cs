﻿using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Raven.Client.Http;
using Raven.Server.Documents.Sharding.Commands;
using Raven.Server.Documents.Sharding.Executors;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Sharding.Operations
{
    public readonly struct SingleNodeShardedBatchOperation : IShardedOperation<BlittableJsonReaderObject, DynamicJsonArray>
    {
        private readonly HttpContext _httpContext;
        private readonly JsonOperationContext _resultContext;
        private readonly Dictionary<int, ShardedSingleNodeBatchCommand> _commands;
        private readonly int _totalCommands;

        public SingleNodeShardedBatchOperation(HttpContext httpContext, JsonOperationContext resultContext,
            Dictionary<int, ShardedSingleNodeBatchCommand> commands, int totalCommands)
        {
            _httpContext = httpContext;
            _resultContext = resultContext;
            _commands = commands;
            _totalCommands = totalCommands;
        }

        public HttpRequest HttpRequest => _httpContext.Request;

        public DynamicJsonArray Combine(Dictionary<int, AbstractExecutor.ShardExecutionResult<BlittableJsonReaderObject>> results)
        {
            var reply = new object[_totalCommands];
            foreach (var c in _commands.Values)
                c.AssembleShardedReply(_resultContext, reply);

            return new DynamicJsonArray(reply);
        }

        public RavenCommand<BlittableJsonReaderObject> CreateCommandForShard(int shardNumber) => _commands[shardNumber];
    }
}
