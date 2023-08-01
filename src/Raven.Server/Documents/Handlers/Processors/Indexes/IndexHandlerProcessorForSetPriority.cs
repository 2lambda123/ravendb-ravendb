﻿using JetBrains.Annotations;
using Raven.Server.Documents.Indexes;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.Indexes;

internal sealed class IndexHandlerProcessorForSetPriority : AbstractIndexHandlerProcessorForSetPriority<DatabaseRequestHandler, DocumentsOperationContext>
{
    public IndexHandlerProcessorForSetPriority([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override AbstractIndexPriorityController GetIndexPriorityProcessor()
    {
        return RequestHandler.Database.IndexStore.Priority;
    }
}
