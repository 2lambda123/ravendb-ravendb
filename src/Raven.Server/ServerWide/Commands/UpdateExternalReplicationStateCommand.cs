﻿using Raven.Client.ServerWide;
using Raven.Server.Documents.Replication;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.ServerWide.Commands
{
    public class UpdateExternalReplicationStateCommand : UpdateValueForDatabaseCommand
    {
        public ExternalReplicationState ExternalReplicationState { get; set; }

        private UpdateExternalReplicationStateCommand()
        {
            // for deserialization
        }

        public UpdateExternalReplicationStateCommand(string databaseName, string guid) : base(databaseName, guid)
        {
        }

        public override string GetItemId()
        {
            return ExternalReplicationState.GenerateItemName(DatabaseName, ExternalReplicationState.TaskId);
        }

        protected override BlittableJsonReaderObject GetUpdatedValue(long index, DatabaseRecord record, JsonOperationContext context, BlittableJsonReaderObject existingValue)
        {
            return context.ReadObject(ExternalReplicationState.ToJson(), GetItemId());
        }

        public override void FillJson(DynamicJsonValue json)
        {
            json[nameof(ExternalReplicationState)] = ExternalReplicationState.ToJson();
        }
    }
}
