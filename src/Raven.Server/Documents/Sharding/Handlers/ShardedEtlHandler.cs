﻿using System.Threading.Tasks;
using Raven.Server.Documents.Sharding.Handlers.Processors.ETL;
using Raven.Server.Routing;

namespace Raven.Server.Documents.Sharding.Handlers;

public class ShardedEtlHandler : ShardedDatabaseRequestHandler
{
    [RavenShardedAction("/databases/*/etl/progress", "GET")]
    public async Task Progress()
    {
        using (var processor = new ShardedEtlHandlerProcessorForProgress(this))
            await processor.ExecuteAsync();
    }
}
