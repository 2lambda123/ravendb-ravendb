﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.Documents.Replication.Messages;
using Raven.Client.ServerWide;
using Raven.Server.Documents.Replication.Incoming;
using Raven.Server.Documents.Replication.Outgoing;
using Raven.Server.Documents.Sharding;
using Raven.Server.Documents.TcpHandlers;
using Raven.Server.ServerWide;
using Raven.Server.Utils;
using Sparrow.Json;

namespace Raven.Server.Documents.Replication;

public class ShardReplicationLoader : ReplicationLoader
{
    private readonly ShardedDocumentDatabase _database;
    private readonly int _myShardNumber;
    private readonly string _shardedName;

    public ShardReplicationLoader(ShardedDocumentDatabase database, ServerStore server) : base(database, server)
    {
        _database = database;
        _myShardNumber = database.ShardNumber;
        _shardedName = database.ShardedDatabaseName;
        IncomingReplicationAdded += handler =>
        {
            handler.DocumentsReceived += OnDocumentReceived;
        };
    }

    protected override IncomingReplicationHandler CreateIncomingReplicationHandler(
        TcpConnectionOptions tcpConnectionOptions, 
        JsonOperationContext.MemoryBuffer buffer,
        PullReplicationParams incomingPullParams,
        ReplicationLatestEtagRequest getLatestEtagMessage)
    {
        if (getLatestEtagMessage.ReplicationsType == ReplicationLatestEtagRequest.ReplicationType.Migration)
        {
            return new IncomingMigrationReplicationHandler(
                tcpConnectionOptions,
                getLatestEtagMessage,
                this,
                buffer,
                getLatestEtagMessage.ReplicationsType,
                getLatestEtagMessage.MigrationIndex);
        }

        return base.CreateIncomingReplicationHandler(tcpConnectionOptions, buffer, incomingPullParams, getLatestEtagMessage);
    }

    protected override void HandleReplicationChanges(DatabaseRecord newRecord, List<IDisposable> instancesToDispose)
    {
        base.HandleReplicationChanges(newRecord, instancesToDispose);
        HandleMigrationReplication(newRecord, instancesToDispose);
    }

    private void OnDocumentReceived(IncomingReplicationHandler handler) => _database.HandleReshardingChanges();

    private void HandleMigrationReplication(DatabaseRecord newRecord, List<IDisposable> instancesToDispose)
    {
        var toRemove = new List<BucketMigrationReplication>();
        // remove
        foreach (var handler in OutgoingHandlers)
        {
            if (handler is not OutgoingMigrationReplicationHandler migrationHandler)
                continue;

            if (newRecord.Sharding.BucketMigrations.TryGetValue(migrationHandler.BucketMigrationNode.Bucket, out var migration) == false)
            {
                toRemove.Add(migrationHandler.BucketMigrationNode);
                continue;
            }

            if (migrationHandler.BucketMigrationNode.ForBucketMigration(migration) == false)
            {
                toRemove.Add(migrationHandler.BucketMigrationNode);
                continue;
            }

            var destTopology = GetTopologyForShard(migration.DestinationShard);
            var destNode = destTopology.WhoseTaskIsIt(RachisState.Follower, migration, getLastResponsibleNode: null);

            // can happened if all nodes are in Rehab
            if (destNode == null)
                continue;

            var source = newRecord.Topology.WhoseTaskIsIt(RachisState.Follower, migration, getLastResponsibleNode: null);
            if (_server.NodeTag != source || migrationHandler.Destination.Url != _clusterTopology.GetUrlFromTag(destNode))
            {
                toRemove.Add(migrationHandler.BucketMigrationNode);
                continue;
            }

            // even if the status is ownership transferred we will keep the connection open to send any left overs if needed
        }

        DropOutgoingConnections(toRemove, instancesToDispose);

        // add
        foreach (var migration in newRecord.Sharding.BucketMigrations)
        {
            var process = migration.Value;

            if (_myShardNumber != process.SourceShard)
                continue;

            var node = newRecord.Topology.WhoseTaskIsIt(RachisState.Follower, process, getLastResponsibleNode: null);
            if (node == _server.NodeTag)
            {
                var current = OutgoingHandlers.OfType<OutgoingMigrationReplicationHandler>().SingleOrDefault(
                    o => o.BucketMigrationNode.ForBucketMigration(process));

                if (current == null)
                {

                    var destTopology = GetTopologyForShard(process.DestinationShard);
                    var destNode = destTopology.WhoseTaskIsIt(RachisState.Follower, process, getLastResponsibleNode: null);

                    // can happened if all nodes are in Rehab
                    if (destNode == null)
                        continue;

                    var migrationDestination = new BucketMigrationReplication(process, destNode)
                    {
                        Database = ShardHelper.ToShardName(_shardedName, process.DestinationShard),
                        Url = _clusterTopology.GetUrlFromTag(destNode)
                    };

                    Task.Run(() => AddAndStartOutgoingReplication(migrationDestination));
                }
            }
        }
    }
}
