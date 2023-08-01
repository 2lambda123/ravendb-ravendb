﻿using JetBrains.Annotations;
using Raven.Server.Documents.Handlers.Processors.TimeSeries;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.TimeSeries
{
    internal sealed class ShardedTimeSeriesHandlerProcessorForPostTimeSeriesNamesConfiguration : AbstractTimeSeriesHandlerProcessorForPostTimeSeriesNamesConfiguration<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedTimeSeriesHandlerProcessorForPostTimeSeriesNamesConfiguration([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }
    }
}
