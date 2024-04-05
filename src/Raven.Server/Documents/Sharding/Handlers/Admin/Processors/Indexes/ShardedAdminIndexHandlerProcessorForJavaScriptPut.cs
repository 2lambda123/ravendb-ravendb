﻿using System.Threading.Tasks;
using System;
using JetBrains.Annotations;
using Raven.Server.Config;
using Raven.Server.Documents.Handlers.Admin.Processors.Indexes;
using Raven.Server.Documents.Indexes;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Admin.Processors.Indexes;

internal sealed class ShardedAdminIndexHandlerProcessorForJavaScriptPut : AbstractAdminIndexHandlerProcessorForJavaScriptPut<ShardedDatabaseRequestHandler, TransactionOperationContext>
{
    public ShardedAdminIndexHandlerProcessorForJavaScriptPut([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override AbstractIndexCreateController GetIndexCreateProcessor() => RequestHandler.DatabaseContext.Indexes.Create;

    protected override RavenConfiguration GetDatabaseConfiguration() => RequestHandler.DatabaseContext.Configuration;

    protected override ValueTask HandleIndexesFromLegacyReplicationAsync()
    {
        throw new NotSupportedException("Legacy replication of indexes isn't supported in a sharded environment");
    }
}
