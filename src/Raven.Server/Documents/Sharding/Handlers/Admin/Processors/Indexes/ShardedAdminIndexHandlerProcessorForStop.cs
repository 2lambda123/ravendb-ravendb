﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Server.Documents.Handlers.Admin.Processors.Indexes;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;

namespace Raven.Server.Documents.Sharding.Handlers.Admin.Processors.Indexes;

internal sealed class ShardedAdminIndexHandlerProcessorForStop : AbstractAdminIndexHandlerProcessorForStop<ShardedDatabaseRequestHandler, TransactionOperationContext>
{
    public ShardedAdminIndexHandlerProcessorForStop([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override bool SupportsCurrentNode => false;

    protected override ValueTask HandleCurrentNodeAsync()
    {
        throw new NotSupportedException();
    }

    protected override Task HandleRemoteNodeAsync(ProxyCommand<object> command, OperationCancelToken token)
    {
        var shardNumber = GetShardNumber();

        return RequestHandler.ShardExecutor.ExecuteSingleShardAsync(command, shardNumber, token.Token);
    }
}
