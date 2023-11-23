﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.Backups;
using Raven.Server.ServerWide;
using Raven.Server.Smuggler.Documents;

namespace Raven.Server.Documents.PeriodicBackup.Restore
{
    internal class RestoreBackupTask : AbstractRestoreBackupTask
    {
        public RestoreBackupTask(ServerStore serverStore, RestoreBackupConfigurationBase restoreConfiguration, IRestoreSource restoreSource, 
            List<string> filesToRestore, OperationCancelToken operationCancelToken) : base(serverStore, restoreConfiguration, restoreSource, filesToRestore, operationCancelToken)
        {
        }

        protected override async Task RestoreAsync()
        {
            await SmugglerRestoreAsync(Database, Context, new DatabaseDestination(Database));
        }

        protected override async Task InitializeAsync()
        {
            await base.InitializeAsync();

            Result.Files.FileCount = FilesToRestore.Count;
            Result.SnapshotRestore.Skipped = true;
            Result.SnapshotRestore.Processed = true;

            Progress.Invoke(Result.Progress);

            CreateRestoreSettings();
        }
    }
}
