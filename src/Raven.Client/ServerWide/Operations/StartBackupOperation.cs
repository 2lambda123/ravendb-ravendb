﻿using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations;
using Raven.Client.Http;
using Sparrow.Json;

namespace Raven.Client.ServerWide.Operations
{
    public class StartBackupOperation : IMaintenanceOperation
    {
        private readonly bool _isFullBackup;
        private readonly long _taskId;

        public StartBackupOperation(bool isFullBackup, long taskId)
        {
            _isFullBackup = isFullBackup;
            _taskId = taskId;
        }

        public RavenCommand GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new StartBackupCommand(_isFullBackup, _taskId);
        }
    }    
}
