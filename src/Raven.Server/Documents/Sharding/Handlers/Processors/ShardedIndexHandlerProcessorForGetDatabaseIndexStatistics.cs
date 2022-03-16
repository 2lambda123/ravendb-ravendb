﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Indexes;
using Raven.Client.Http;
using Raven.Server.Documents.Handlers.Processors;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Processors
{
    internal class ShardedIndexHandlerProcessorForGetDatabaseIndexStatistics : AbstractIndexHandlerProcessorForGetDatabaseIndexStatistics<ShardedRequestHandler,
         TransactionOperationContext>
    {
        public ShardedIndexHandlerProcessorForGetDatabaseIndexStatistics([NotNull] ShardedRequestHandler requestHandler) : base(requestHandler,
            requestHandler.ContextPool)
        {
        }

        protected override bool SupportsCurrentNode => false;

        protected override ValueTask<IndexStats[]> GetResultForCurrentNodeAsync() => throw new NotSupportedException();

        protected override Task<IndexStats[]> GetResultForRemoteNodeAsync(RavenCommand<IndexStats[]> command) => 
            RequestHandler.ShardExecutor.ExecuteSingleShardAsync(command, GetShardNumber());
    }
}
