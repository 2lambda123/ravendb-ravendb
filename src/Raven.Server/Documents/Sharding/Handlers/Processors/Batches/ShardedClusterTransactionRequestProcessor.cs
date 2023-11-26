using System;
using Raven.Server.Config.Categories;
using Raven.Server.Documents.Handlers.Batches;
using Raven.Server.Documents.Handlers.Processors.Batches;
using Raven.Server.Documents.Sharding.Handlers.Batches;
using Raven.Server.ServerWide.Commands;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.Batches;

public sealed class ShardedClusterTransactionRequestProcessor : AbstractClusterTransactionRequestProcessor<ShardedDatabaseRequestHandler, ShardedBatchCommand>
{
    public ShardedClusterTransactionRequestProcessor(ShardedDatabaseRequestHandler requestHandler, DocumentDatabase database)
        : base(requestHandler, database)
    {
    }

    protected override ArraySegment<BatchRequestParser.CommandData> GetParsedCommands(ShardedBatchCommand command) => command.ParsedCommands;
    
    protected override ClusterConfiguration GetClusterConfiguration() => RequestHandler.DatabaseContext.Configuration.Cluster;

    protected override ClusterTransactionCommand CreateClusterTransactionCommand(
        ArraySegment<BatchRequestParser.CommandData> parsedCommands,
        ClusterTransactionCommand.ClusterTransactionOptions options,
        string raftRequestId)
    {
        return new ClusterTransactionCommand(
            RequestHandler.DatabaseContext.DatabaseName,
            RequestHandler.DatabaseContext.IdentityPartsSeparator,
            parsedCommands,
            options,
            raftRequestId);
    }
}
