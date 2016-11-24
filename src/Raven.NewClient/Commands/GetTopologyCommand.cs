﻿using System.Net.Http;
using Raven.NewClient.Client.Http;
using Raven.NewClient.Client.Json;
using Sparrow.Json;

namespace Raven.NewClient.Client.Commands
{
    public class GetTopologyCommand : RavenCommand<Topology>
    {
        public override HttpRequestMessage CreateRequest(ServerNode node, out string url)
        {
            url = $"{node.Url}/databases/{node.Database}/topology?url={node.Url}";
            return new HttpRequestMessage
            {
                Method = HttpMethod.Get,
            };
        }

        public override void SetResponse(BlittableJsonReaderObject response)
        {
            Result = JsonDeserializationClient.ClusterTopology(response);
        }
    }
}