﻿using JetBrains.Annotations;
using Raven.Server.Config.Categories;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Handlers.Processors.OngoingTasks
{
    internal sealed class OngoingTasksHandlerProcessorForUpdatePeriodicBackup : AbstractOngoingTasksHandlerProcessorForUpdatePeriodicBackup<DatabaseRequestHandler, DocumentsOperationContext>
    {
        public OngoingTasksHandlerProcessorForUpdatePeriodicBackup([NotNull] DatabaseRequestHandler requestHandler) : base(requestHandler)
        {
        }

        protected override BackupConfiguration GetBackupConfiguration() => RequestHandler.Database.Configuration.Backup;
    }
}
