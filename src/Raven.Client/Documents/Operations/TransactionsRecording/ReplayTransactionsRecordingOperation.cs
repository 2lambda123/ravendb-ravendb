﻿using System;
using System.IO;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.Json.Converters;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.TransactionsRecording
{
    public class ReplayTransactionsRecordingOperation : IMaintenanceOperation<OperationIdResult>
    {
        private readonly Stream _replayStream;

        public ReplayTransactionsRecordingOperation(Stream replayStream)
        {
            _replayStream = replayStream;
            if (_replayStream.Position != 0)
                throw new ArgumentException("For replay transactions recording the stream position must to be set to zero");
        }

        public RavenCommand<OperationIdResult> GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new ReplayTransactionsRecordingCommand(_replayStream);
        }

        private class ReplayTransactionsRecordingCommand : RavenCommand<OperationIdResult>
        {
            private readonly Stream _replayStream;

            public ReplayTransactionsRecordingCommand(Stream replayStream)
            {
                _replayStream = replayStream;
            }

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/transactions/replay?";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new StreamContent(_replayStream)
                };
                _replayStream.Position = 0;
                return request;
            }

            public override void SetResponse(JsonOperationContext context, BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.OperationIdResult(response);
            }

            public override bool IsReadRequest => false;
        }
    }
}
