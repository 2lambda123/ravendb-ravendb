﻿using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;
using Raven.Client.Server;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.ServerWide.Commands
{
    public class AddDatabaseCommand : CommandBase
    {
        public string Name;
        public DatabaseRecord Record;
        public bool Encrypted;

        public override DynamicJsonValue ToJson(JsonOperationContext context)
        {
            return new DynamicJsonValue
            {
                ["Type"] = nameof(AddDatabaseCommand),
                [nameof(Name)] = Name,
                [nameof(Record)] = EntityToBlittable.ConvertEntityToBlittable(Record, DocumentConventions.Default, context),
                [nameof(Etag)] = Etag,
                [nameof(Encrypted)] = Encrypted
            };
        }
    }

}
