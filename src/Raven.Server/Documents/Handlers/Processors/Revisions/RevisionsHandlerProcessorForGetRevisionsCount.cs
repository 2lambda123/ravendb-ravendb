﻿using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Session.Operations;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.Handlers.Processors.Revisions
{
    internal sealed class RevisionsHandlerProcessorForGetRevisionsCount : AbstractRevisionsHandlerProcessorForGetRevisionsCount<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public RevisionsHandlerProcessorForGetRevisionsCount([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override ValueTask<GetRevisionsCountOperation.DocumentRevisionsCount> GetRevisionsCountAsync(string docId)
        {
            using (ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            using (context.OpenReadTransaction())
            {
                return ValueTask.FromResult(new GetRevisionsCountOperation.DocumentRevisionsCount()
                {
                    RevisionsCount = RequestHandler.Database.DocumentsStorage.RevisionsStorage.GetRevisionsCount(context, docId)
                });
            }
        }
    }
}
