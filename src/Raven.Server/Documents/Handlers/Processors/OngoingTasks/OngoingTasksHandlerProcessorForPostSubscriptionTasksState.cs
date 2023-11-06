﻿using JetBrains.Annotations;
using Raven.Server.Documents.Subscriptions;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.OngoingTasks
{
    internal sealed class OngoingTasksHandlerProcessorForPostSubscriptionTasksState : AbstractOngoingTasksHandlerProcessorForPostSubscriptionTasksState<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public OngoingTasksHandlerProcessorForPostSubscriptionTasksState([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override AbstractSubscriptionStorage GetSubscriptionStorage()
        {
            return RequestHandler.Database.SubscriptionStorage;
        }
    }
}
