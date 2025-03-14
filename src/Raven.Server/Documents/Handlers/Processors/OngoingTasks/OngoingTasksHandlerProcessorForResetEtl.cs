﻿using JetBrains.Annotations;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.OngoingTasks
{
    internal sealed class OngoingTasksHandlerProcessorForResetEtl : AbstractOngoingTasksHandlerProcessorForResetEtl<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public OngoingTasksHandlerProcessorForResetEtl([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }
    }
}
