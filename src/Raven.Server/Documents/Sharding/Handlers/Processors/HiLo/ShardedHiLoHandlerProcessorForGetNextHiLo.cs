﻿using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Identity;
using Raven.Client.Http;
using Raven.Server.Documents.Handlers;
using Raven.Server.Documents.Handlers.Processors.HiLo;
using Raven.Server.ServerWide.Context;
using Raven.Server.Web.Http;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.HiLo;

internal sealed class ShardedHiLoHandlerProcessorForGetNextHiLo : AbstractHiLoHandlerProcessorForGetNextHiLo<ShardedDatabaseRequestHandler, TransactionOperationContext>
{
    public ShardedHiLoHandlerProcessorForGetNextHiLo([NotNull] ShardedDatabaseRequestHandler requestHandler)
        : base(requestHandler)
    {
    }

    public override async ValueTask ExecuteAsync()
    {
        using (var token = RequestHandler.CreateHttpRequestBoundOperationToken())
        {
            var tag = GetTag();
            var hiloDocId = HiLoHandler.RavenHiloIdPrefix + tag;

            int shardNumber;
            using (RequestHandler.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                shardNumber = RequestHandler.DatabaseContext.GetShardNumberFor(context, hiloDocId);

            var command = CreateCommand();
            var proxyCommand = new ProxyCommand<HiLoResult>(command, HttpContext.Response);

            await RequestHandler.DatabaseContext.ShardExecutor.ExecuteSingleShardAsync(proxyCommand, shardNumber, token.Token);
        }
    }

    private RavenCommand<HiLoResult> CreateCommand()
    {
        var tag = GetTag();
        var lastSize = GetLastBatchSize();
        var lastRangeAt = GetLastRangeAt();
        var identityPartsSeparator = GetIdentityPartsSeparator();
        var lastMax = GetLastMax();

        return new NextHiLoCommand(tag, lastSize, lastRangeAt, identityPartsSeparator, lastMax);
    }
}
