﻿using JetBrains.Annotations;
using Raven.Client.Exceptions.Commercial;
using Raven.Server.Config;
using Raven.Server.Documents;
using Raven.Server.Documents.Handlers.Processors;
using Raven.Server.Utils.Features;
using Sparrow.Json;

namespace Raven.Server.Integrations.PostgreSQL.Handlers.Processors;

internal abstract class AbstractPostgreSqlIntegrationHandlerProcessor<TRequestHandler, TOperationContext> : AbstractDatabaseHandlerProcessor<TRequestHandler, TOperationContext>
    where TOperationContext : JsonOperationContext
    where TRequestHandler : AbstractDatabaseRequestHandler<TOperationContext>
{
    protected AbstractPostgreSqlIntegrationHandlerProcessor([NotNull] TRequestHandler requestHandler)
        : base(requestHandler)
    {
    }

    public static void AssertCanUsePostgreSqlIntegration(TRequestHandler requestHandler)
    {
        if (requestHandler.ServerStore.LicenseManager.CanUsePowerBi(false, out _))
            return;

        if (requestHandler.ServerStore.LicenseManager.CanUsePostgreSqlIntegration(withNotification: true))
        {
            requestHandler.ServerStore.FeatureGuardian.Assert(Feature.PostgreSql, () =>
                $"You have enabled the PostgreSQL integration via '{RavenConfiguration.GetKey(x => x.Integrations.PostgreSql.Enabled)}' configuration but " +
                "this is an experimental feature and the server does not support experimental features. " +
                $"Please enable experimental features by changing '{RavenConfiguration.GetKey(x => x.Core.FeaturesAvailability)}' configuration value to '{nameof(FeaturesAvailability.Experimental)}'.");
            return;
        }

        throw new LicenseLimitException("You cannot use this feature because your license doesn't allow neither PostgreSQL integration feature nor Power BI");
    }
}
