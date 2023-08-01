﻿using JetBrains.Annotations;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.DocumentsCompression
{
    internal sealed class DocumentsCompressionHandlerProcessorForPost : AbstractDocumentsCompressionHandlerProcessorForPost<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public DocumentsCompressionHandlerProcessorForPost([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }
    }
}
