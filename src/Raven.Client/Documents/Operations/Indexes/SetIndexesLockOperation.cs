﻿using System;
using System.Linq;
using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Session;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Util;
using Sparrow.Json;

namespace Raven.Client.Documents.Operations.Indexes
{
    public class SetIndexesLockOperation : IMaintenanceOperation
    {
        private readonly Parameters _parameters;

        public SetIndexesLockOperation(string indexName, IndexLockMode mode)
        {
            if (indexName == null)
                throw new ArgumentNullException(nameof(indexName));

            _parameters = new Parameters
            {
                IndexNames = new[] { indexName },
                Mode = mode
            };
            
            FilterAutoIndexes();
        }

        public SetIndexesLockOperation(Parameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (parameters.IndexNames == null || parameters.IndexNames.Length == 0)
                throw new ArgumentNullException(nameof(parameters.IndexNames));

            _parameters = parameters;

            FilterAutoIndexes();
        }

        private void FilterAutoIndexes()
        {
            // Check for auto-indexes - we do not set lock for auto-indexes
            if (_parameters.IndexNames.Any(indexName => indexName.StartsWith("Auto/", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("'Indexes list contains Auto-Indexes. Lock Mode' is not set for Auto-Indexes.");
            }
        }

        public RavenCommand GetCommand(DocumentConventions conventions, JsonOperationContext context)
        {
            return new SetIndexLockCommand(conventions, context, _parameters);
        }

        private class SetIndexLockCommand : RavenCommand, IRaftCommand
        {
            private readonly Parameters _parameters;

            public SetIndexLockCommand(DocumentConventions conventions, JsonOperationContext context, Parameters parameters)
            {
                if (conventions == null)
                    throw new ArgumentNullException(nameof(conventions));
                if (context == null)
                    throw new ArgumentNullException(nameof(context));
                if (parameters == null)
                    throw new ArgumentNullException(nameof(parameters));

                _parameters = parameters;
            }

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/indexes/set-lock";

                var parameters = EntityToBlittable.ConvertCommandToBlittable(_parameters, ctx);

                return new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new BlittableJsonContent(this, stream =>
                    {
                        ctx.Write(stream, parameters);
                    })
                };
            }

            public string RaftUniqueRequestId { get; } = RaftIdGenerator.NewId();
        }

        public class Parameters
        {
            public string[] IndexNames { get; set; }
            public IndexLockMode Mode { get; set; }
        }
    }
}
