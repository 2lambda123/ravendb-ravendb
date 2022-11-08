﻿using System.Threading.Tasks;
using Raven.Server.Documents.Handlers.Admin.Processors.Configuration;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Admin.Processors.Configuration;

internal class ShardedAdminConfigurationHandlerProcessorForPutSettings : AbstractAdminConfigurationHandlerProcessorForPutSettings<ShardedDatabaseRequestHandler, TransactionOperationContext>
{
    public ShardedAdminConfigurationHandlerProcessorForPutSettings(ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected override async ValueTask WaitForIndexNotificationAsync(long index)
    {
        await RequestHandler.WaitForIndexNotificationAsync(index);
    }
}
