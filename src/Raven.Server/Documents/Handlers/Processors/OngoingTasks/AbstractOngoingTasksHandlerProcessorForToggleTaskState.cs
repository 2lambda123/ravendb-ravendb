﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Documents.Operations.OngoingTasks;
using Raven.Server.Documents.Handlers.Processors.Databases;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Handlers.Processors.OngoingTasks
{
    internal abstract class AbstractOngoingTasksHandlerProcessorForToggleTaskState<TRequestHandler, TOperationContext> : AbstractHandlerProcessorForUpdateDatabaseTask<TRequestHandler, TOperationContext>
        where TOperationContext : JsonOperationContext
        where TRequestHandler : AbstractDatabaseRequestHandler<TOperationContext>
    {
        private long _key;
        private string _desc;

        protected AbstractOngoingTasksHandlerProcessorForToggleTaskState([NotNull] TRequestHandler requestHandler)
            : base(requestHandler)
        {
        }

        protected override void OnBeforeResponseWrite(TransactionOperationContext context, DynamicJsonValue responseJson, object _, long index)
        {
            responseJson[nameof(ModifyOngoingTaskResult.TaskId)] = _key;
        }

        protected override ValueTask OnAfterUpdateConfiguration(TransactionOperationContext context, object configuration, string raftRequestId)
        {
            RequestHandler.LogTaskToAudit(_desc, _key, configuration: null);
            return ValueTask.CompletedTask;
        }

        protected override Task<(long Index, object Result)> OnUpdateConfiguration(TransactionOperationContext context, object _, string raftRequestId)
        {
            _key = RequestHandler.GetLongQueryString("key");

            var typeStr = RequestHandler.GetQueryStringValueAndAssertIfSingleAndNotEmpty("type");
            var disable = RequestHandler.GetBoolValueQueryString("disable") ?? true;
            var taskName = RequestHandler.GetStringQueryString("taskName", required: false);

            if (Enum.TryParse<OngoingTaskType>(typeStr, true, out var type) == false)
                throw new ArgumentException($"Unknown task type: {type}", nameof(type));

            _desc = (disable) ? "disable" : "enable";
            _desc += $"-{typeStr}-Task {(string.IsNullOrEmpty(taskName) ? string.Empty : $" with task name: '{taskName}'")}";

            return RequestHandler.ServerStore.ToggleTaskState(_key, taskName, type, disable, RequestHandler.DatabaseName, raftRequestId);
        }
    }
}
