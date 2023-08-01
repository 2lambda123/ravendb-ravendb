﻿using JetBrains.Annotations;
using Raven.Client.Documents.Operations.Revisions;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.Revisions
{
    internal sealed class RevisionsHandlerProcessorForGetRevisionsConfiguration : AbstractRevisionsHandlerProcessorForGetRevisionsConfiguration<DatabaseRequestHandler>
    {
        public RevisionsHandlerProcessorForGetRevisionsConfiguration([NotNull] DatabaseRequestHandler requestHandler) 
            : base(requestHandler)
        {
        }

        protected override RevisionsConfiguration GetRevisionsConfiguration()
        {
            using (RequestHandler.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            using (var rawRecord = RequestHandler.Server.ServerStore.Cluster.ReadRawDatabaseRecord(context, RequestHandler.Database.Name))
            {
                return rawRecord?.RevisionsConfiguration;
            }
        }
    }
}
