﻿using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using Raven.Client.Json;
using Sparrow.Json;

namespace Raven.Client.ServerWide.Operations.Certificates;

public class ValidateTwoFactorAuthenticationTokenOperation : IServerOperation
{
    private readonly string _validationCode;

    public ValidateTwoFactorAuthenticationTokenOperation(string validationCode)
    {
        _validationCode = validationCode;
    }
    
    public RavenCommand GetCommand(DocumentConventions conventions, JsonOperationContext context)
    {
        return new ValidateTwoFactorAuthenticationTokenCommand(_validationCode);
    }
    
    private class ValidateTwoFactorAuthenticationTokenCommand : RavenCommand
    {
        private readonly string _validationCode;

        public ValidateTwoFactorAuthenticationTokenCommand(string validationCode)
        {
            _validationCode = validationCode;
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            url = $"{node.Url}/authentication/2fa";

            return new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                Content = new BlittableJsonContent(async stream =>
                {
                    await using (var writer = new AsyncBlittableJsonTextWriter(ctx, stream))
                    {
                        writer.WriteStartObject();
                        writer.WritePropertyName("Token");
                        writer.WriteString(_validationCode);
                        writer.WriteEndObject();
                    }
                })
            };
        }
    }
}
