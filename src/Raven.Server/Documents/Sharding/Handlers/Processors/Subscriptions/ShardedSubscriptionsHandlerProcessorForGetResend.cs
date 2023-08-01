﻿using System.Collections.Generic;
using JetBrains.Annotations;
using Raven.Client.Documents.Subscriptions;
using Raven.Server.Documents.Handlers.Processors.Subscriptions;
using Raven.Server.Documents.Sharding.Subscriptions;
using Raven.Server.ServerWide.Context;
using Sparrow.Utils;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Subscriptions
{
    internal sealed class ShardedSubscriptionsHandlerProcessorForGetResend : AbstractSubscriptionsHandlerProcessorForGetResend<ShardedDatabaseRequestHandler, TransactionOperationContext, SubscriptionConnectionsStateOrchestrator>
    {
        public ShardedSubscriptionsHandlerProcessorForGetResend([NotNull] ShardedDatabaseRequestHandler requestHandler) 
            : base(requestHandler, requestHandler.DatabaseContext.SubscriptionsStorage)
        {
        }

        protected override HashSet<long> GetActiveBatches(ClusterOperationContext _, SubscriptionState subscriptionState)
        {
            DevelopmentHelper.ShardingToDo(DevelopmentHelper.TeamMember.Stav, DevelopmentHelper.Severity.Normal, "RavenDB-19081 Get active batches from orchestrator getting orchestrator's node is implemented");
            return new HashSet<long>();
        }
    }
}
