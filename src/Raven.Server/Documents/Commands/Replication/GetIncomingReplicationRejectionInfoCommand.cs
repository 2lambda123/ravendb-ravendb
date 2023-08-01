﻿using System.Net.Http;
using Raven.Client.Http;
using Raven.Server.Documents.Handlers.Processors.Replication;
using Raven.Server.Json;
using Sparrow.Json;

namespace Raven.Server.Documents.Commands.Replication
{
    internal sealed class GetIncomingReplicationRejectionInfoCommand : RavenCommand<ReplicationIncomingRejectionInfoPreview>
    {
        public GetIncomingReplicationRejectionInfoCommand()
        {
        }

        public GetIncomingReplicationRejectionInfoCommand(string nodeTag)
        {
            SelectedNodeTag = nodeTag;
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            url = $"{node.Url}/databases/{node.Database}/replication/debug/incoming-rejection-info";

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get
            };

            return request;
        }

        public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
        {
            if (response == null)
                ThrowInvalidResponse();

            Result = JsonDeserializationServer.ReplicationIncomingRejectionInfoPreview(response);
        }

        public override bool IsReadRequest => true;
    }
}
