﻿using JetBrains.Annotations;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.Expiration
{
    internal sealed class ExpirationHandlerProcessorForPost : AbstractExpirationHandlerProcessorForPost<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public ExpirationHandlerProcessorForPost([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }
    }
}
