﻿using System;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.Json.Serialization;
using Sparrow.Extensions;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Backups
{
    public class StartBackupOperation : IMaintenanceOperation<OperationIdResult<StartBackupOperationResult>>
    {
        private readonly bool _isFullBackup;
        private readonly long _taskId;

        public StartBackupOperation(bool isFullBackup, long taskId)
        {
            _isFullBackup = isFullBackup;
            _taskId = taskId;
        }

        public RavenCommand<OperationIdResult<StartBackupOperationResult>> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new StartBackupCommand(_isFullBackup, _taskId);
        }

        internal class StartBackupCommand : RavenCommand<OperationIdResult<StartBackupOperationResult>>
        {
            public override bool IsReadRequest => true;

            private readonly bool? _isFullBackup;
            private readonly long _taskId;
            private readonly long? _operationId;
            private readonly DateTime? _startTime;

            public StartBackupCommand(bool? isFullBackup, long taskId)
            {
                _isFullBackup = isFullBackup;
                _taskId = taskId;
            }

            internal StartBackupCommand(bool? isFullBackup, long taskId, long operationId, DateTime? startTime = null) : this(isFullBackup, taskId)
            {
                _operationId = operationId;
                _startTime = startTime;
            }

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/admin/backup/database?taskId={_taskId}";

                if (_isFullBackup.HasValue)
                    url += $"&isFullBackup={_isFullBackup}";
                if (_operationId.HasValue)
                    url += $"&operationId={_operationId}";
                if (_startTime.HasValue)
                    url += $"&startTime={_startTime.Value.GetDefaultRavenFormat()}";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post
                };

                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                var result = JsonDeserializationClient.BackupDatabaseNowResult(response);
                var operationIdResult = JsonDeserializationClient.OperationIdResult(response);
                // OperationNodeTag used to fetch operation status
                operationIdResult.OperationNodeTag ??= result.ResponsibleNode;
                Result = operationIdResult.ForResult(result);
            }
        }
    }

    public class StartBackupOperationResult
    {
        public string ResponsibleNode { get; set; }

        public long OperationId { get; set; }
    }
}
