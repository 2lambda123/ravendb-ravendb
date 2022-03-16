﻿using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Indexes;
using Raven.Client.Http;
using Raven.Client.Json.Serialization;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Indexes
{
    public class GetIndexingStatusOperation : IMaintenanceOperation<IndexingStatus>
    {
        public RavenCommand<IndexingStatus> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new GetIndexingStatusCommand();
        }

        internal class GetIndexingStatusCommand : RavenCommand<IndexingStatus>
        {
            public GetIndexingStatusCommand(string nodeTag = null)
            {
                SelectedNodeTag = nodeTag;
            }

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/indexes/status";

                return new HttpRequestMessage
                {
                    Method = HttpMethod.Get
                };
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.IndexingStatus(response);
            }

            public override bool IsReadRequest => true;
        }
    }
}
