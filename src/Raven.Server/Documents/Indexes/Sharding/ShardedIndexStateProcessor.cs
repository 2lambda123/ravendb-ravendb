﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Indexes;
using Raven.Client.Exceptions.Documents.Indexes;
using Raven.Server.Documents.Sharding;
using Raven.Server.ServerWide;

namespace Raven.Server.Documents.Indexes.Sharding;

public class ShardedIndexStateProcessor : AbstractIndexStateProcessor
{
    private readonly ShardedContext _context;

    public ShardedIndexStateProcessor([NotNull] ShardedContext context, ServerStore serverStore)
        : base(serverStore)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    protected override void ValidateIndex(string name, IndexState state)
    {
        if (_context.Indexes.TryGetIndexDefinition(name, out var indexDefinition) == false)
            IndexDoesNotExistException.ThrowFor(name);
    }

    protected override string GetDatabaseName()
    {
        return _context.DatabaseName;
    }

    protected override async ValueTask WaitForIndexNotificationAsync(long index)
    {
        await ServerStore.Cluster.WaitForIndexNotification(index, ServerStore.Engine.OperationTimeout);
    }
}
