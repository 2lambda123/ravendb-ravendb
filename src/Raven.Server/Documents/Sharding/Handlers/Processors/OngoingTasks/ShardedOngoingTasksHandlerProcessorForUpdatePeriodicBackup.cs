﻿using System;
using JetBrains.Annotations;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Exceptions.Sharding;
using Raven.Server.Documents.Handlers.Processors.OngoingTasks;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using BackupConfiguration = Raven.Server.Config.Categories.BackupConfiguration;

namespace Raven.Server.Documents.Sharding.Handlers.Processors.OngoingTasks
{
    internal sealed class ShardedOngoingTasksHandlerProcessorForUpdatePeriodicBackup : AbstractOngoingTasksHandlerProcessorForUpdatePeriodicBackup<ShardedDatabaseRequestHandler, TransactionOperationContext>
    {
        public ShardedOngoingTasksHandlerProcessorForUpdatePeriodicBackup([NotNull] ShardedDatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override BackupConfiguration GetBackupConfiguration() => RequestHandler.DatabaseContext.Configuration.Backup;

        protected override void OnBeforeUpdateConfiguration(ref PeriodicBackupConfiguration configuration, JsonOperationContext context)
        {
            if (configuration.BackupType == BackupType.Snapshot)
                throw new NotSupportedInShardingException($"Backups of type '{nameof(BackupType.Snapshot)}' are not supported in sharding.");

            if (string.IsNullOrEmpty(configuration.MentorNode) == false)
                throw new InvalidOperationException($"Can't create or update periodic backup {configuration.Name}. Choosing a mentor node for an ongoing task is not supported in sharding");

            base.OnBeforeUpdateConfiguration(ref configuration, context);
        }
    }
}
