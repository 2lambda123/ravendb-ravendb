﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using Raven.Client.Http;
using Sparrow.Json;

namespace Raven.Client.Documents.Commands
{
    public class SeedIdentityForCommand : RavenCommand<Dictionary<string, long>>
    {
        private readonly string _id;
        private readonly long _value;

        public SeedIdentityForCommand(string id, long value)
        {
            _id = id ?? throw new ArgumentNullException(nameof(id));
            _value = value;
        }

        public override bool IsReadRequest { get; } = false;

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            EnsureIsNotNullOrEmpty(_id, nameof(_id));

            url = $"{node.Url}/databases/{node.Database}/identity/seed?name={UrlEncode(_id)}&value={_value}";

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post
            };
            return request;
        }

        public override void SetResponse(BlittableJsonReaderObject response, bool fromCache)
        {
            if (response == null || response.TryGet("NewIdentityValue", out long result) == false)
            {
                ThrowInvalidResponse();
                return; // never hit
            }


            var resultDict = new Dictionary<string, long>
            {
                [_id] = result
            };

            Result = resultDict;
        }
    }
}
