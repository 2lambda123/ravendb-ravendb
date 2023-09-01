﻿using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Server.Documents.Indexes;
using Raven.Server.Json;
using Sparrow.Json;

namespace Raven.Server.Documents.Handlers.Processors.Indexes;

internal abstract class AbstractIndexHandlerProcessorForSetLockMode<TRequestHandler, TOperationContext> : AbstractDatabaseHandlerProcessor<TRequestHandler, TOperationContext>
    where TOperationContext : JsonOperationContext 
    where TRequestHandler : AbstractDatabaseRequestHandler<TOperationContext>
{
    protected AbstractIndexHandlerProcessorForSetLockMode([NotNull] TRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected abstract AbstractIndexLockModeController GetIndexLockModeProcessor();

    public override async ValueTask ExecuteAsync()
    {
        var raftRequestId = RequestHandler.GetRaftRequestIdFromQuery();
        using (ContextPool.AllocateOperationContext(out JsonOperationContext context))
        {
            var json = await context.ReadForMemoryAsync(RequestHandler.RequestBodyStream(), "index/set-lock");
            var parameters = JsonDeserializationServer.Parameters.SetIndexLockParameters(json);

            if (parameters.IndexNames == null || parameters.IndexNames.Length == 0)
                throw new ArgumentNullException(nameof(parameters.IndexNames));

            // Check for auto-indexes - we do not set lock for auto-indexes
            if (parameters.IndexNames.Any(indexName => indexName.StartsWith("Auto/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("'Indexes list contains Auto-Indexes. Lock Mode' is not set for Auto-Indexes.");
            }

            var processor = GetIndexLockModeProcessor();

            for (var index = 0; index < parameters.IndexNames.Length; index++)
            {
                var indexName = parameters.IndexNames[index];
                await processor.SetLockAsync(indexName, parameters.Mode, $"{raftRequestId}/{index}");
            }
        }

        RequestHandler.NoContentStatus();
    }
}
