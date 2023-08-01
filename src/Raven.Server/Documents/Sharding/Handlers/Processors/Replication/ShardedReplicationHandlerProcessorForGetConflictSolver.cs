﻿using JetBrains.Annotations;
using Raven.Server.Documents.Handlers.Processors.Replication;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Replication;

internal sealed class ShardedReplicationHandlerProcessorForGetConflictSolver : AbstractReplicationHandlerProcessorForGetConflictSolver<ShardedDatabaseRequestHandler, TransactionOperationContext>
{
    public ShardedReplicationHandlerProcessorForGetConflictSolver([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }
}
