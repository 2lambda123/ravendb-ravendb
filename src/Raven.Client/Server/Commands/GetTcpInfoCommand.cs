﻿using System.Net.Http;
using Raven.Client.Http;
using Raven.Client.Json.Converters;
using Sparrow.Json;

namespace Raven.Client.Server.Commands
{
    public class GetTcpInfoCommand : RavenCommand<TcpConnectionInfo>
    {
        public override HttpRequestMessage CreateRequest(ServerNode node, out string url)
        {
            url = $"{node.Url}/info/tcp";
            RequestedNode = node;
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
            };
            return request;
        }

        public override void SetResponse(BlittableJsonReaderObject response, bool fromCache)
        {
            if (response == null)
                ThrowInvalidResponse();

            Result = JsonDeserializationClient.TcpConnectionInfo(response);
        }

        public ServerNode RequestedNode { get; private set; }


        public override bool IsReadRequest => true;
    }
}