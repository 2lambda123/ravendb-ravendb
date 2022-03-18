﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Raven.Client;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Smuggler;
using Raven.Client.Exceptions.Documents.Indexes;
using Raven.Client.Exceptions.Security;
using Raven.Server.Documents.Handlers.Processors.Indexes.Admin;
using Raven.Server.Json;
using Raven.Server.Routing;
using Raven.Server.ServerWide.Context;
using Raven.Server.Smuggler.Documents;
using Raven.Server.Smuggler.Documents.Data;
using Raven.Server.Smuggler.Migration;
using Raven.Server.TrafficWatch;
using Raven.Server.Web;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Logging;

namespace Raven.Server.Documents.Handlers.Admin
{
    public class AdminIndexHandler : DatabaseRequestHandler
    {
        [RavenAction("/databases/*/admin/indexes", "PUT", AuthorizationStatus.DatabaseAdmin, DisableOnCpuCreditsExhaustion = true)]
        public async Task Put()
        {
            var isReplicated = GetBoolValueQueryString("is-replicated", required: false);
            if (isReplicated.HasValue && isReplicated.Value)
            {
                await HandleLegacyIndexesAsync();
                return;
            }

            await PutInternal(new PutIndexParameters(this, validatedAsAdmin: true, Database.ServerStore.ContextPool, Database.Name, PutIndexTask));
        }

        [RavenAction("/databases/*/indexes", "PUT", AuthorizationStatus.ValidUser, EndpointType.Write, DisableOnCpuCreditsExhaustion = true)]
        public async Task PutJavaScript()
        {
            if (HttpContext.Features.Get<IHttpAuthenticationFeature>() is RavenServer.AuthenticateConnection feature && Database.Configuration.Indexing.RequireAdminToDeployJavaScriptIndexes)
            {
                if (feature.CanAccess(Database.Name, requireAdmin: true, requireWrite: true) == false)
                    throw new AuthorizationException("Deployments of JavaScript indexes has been restricted to admin users only");
            }

            await PutInternal(new PutIndexParameters(this, validatedAsAdmin: false, Database.ServerStore.ContextPool, Database.Name, PutIndexTask));
        }

        private async Task<long> PutIndexTask((IndexDefinition IndexDefinition, string RaftRequestId, string Source) args)
        {
            return await Database.IndexStore.CreateIndexInternal(args.IndexDefinition, $"{args.RaftRequestId}/{args.IndexDefinition.Name}", args.Source);
        }

        internal static async Task PutInternal(PutIndexParameters parameters)
        {
            using (parameters.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                var createdIndexes = new List<PutIndexResult>();
                var raftIndexIds = new List<long>();

                var input = await context.ReadForMemoryAsync(parameters.RequestHandler.RequestBodyStream(), "Indexes");
                if (input.TryGet("Indexes", out BlittableJsonReaderArray indexes) == false)
                    ThrowRequiredPropertyNameInRequest("Indexes");

                var raftRequestId = parameters.RequestHandler.GetRaftRequestIdFromQuery();
                foreach (BlittableJsonReaderObject indexToAdd in indexes)
                {
                    var indexDefinition = JsonDeserializationServer.IndexDefinition(indexToAdd);
                    indexDefinition.Name = indexDefinition.Name?.Trim();

                    var source = IsLocalRequest(parameters.RequestHandler.HttpContext) ? Environment.MachineName : parameters.RequestHandler.HttpContext.Connection.RemoteIpAddress.ToString();

                    if (LoggingSource.AuditLog.IsInfoEnabled)
                    {
                        var clientCert = parameters.RequestHandler.GetCurrentCertificate();

                        var auditLog = LoggingSource.AuditLog.GetLogger(parameters.DatabaseName, "Audit");
                        auditLog.Info($"Index {indexDefinition.Name} PUT by {clientCert?.Subject} {clientCert?.Thumbprint} with definition: {indexToAdd} from {source} at {DateTime.UtcNow}");
                    }

                    if (indexDefinition.Maps == null || indexDefinition.Maps.Count == 0)
                        throw new ArgumentException("Index must have a 'Maps' fields");

                    indexDefinition.Type = indexDefinition.DetectStaticIndexType();

                    // C# index using a non-admin endpoint
                    if (indexDefinition.Type.IsJavaScript() == false && parameters.ValidatedAsAdmin == false)
                    {
                        throw new UnauthorizedAccessException($"Index {indexDefinition.Name} is a C# index but was sent through a non-admin endpoint using REST api, this is not allowed.");
                    }

                    if (indexDefinition.Name.StartsWith(Constants.Documents.Indexing.SideBySideIndexNamePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException(
                            $"Index name must not start with '{Constants.Documents.Indexing.SideBySideIndexNamePrefix}'. Provided index name: '{indexDefinition.Name}'");
                    }

                    var index = await parameters.PutIndexTask((indexDefinition, $"{raftRequestId}/{indexDefinition.Name}", source));
                    raftIndexIds.Add(index);

                    createdIndexes.Add(new PutIndexResult
                    {
                        Index = indexDefinition.Name,
                        RaftCommandIndex = index
                    });
                }

                if (TrafficWatchManager.HasRegisteredClients)
                    parameters.RequestHandler.AddStringToHttpContext(indexes.ToString(), TrafficWatchChangeType.Index);

                if (parameters.WaitForIndexNotification != null)
                    await parameters.WaitForIndexNotification((context, raftIndexIds));

                parameters.RequestHandler.HttpContext.Response.StatusCode = (int)HttpStatusCode.Created;

                await using (var writer = new AsyncBlittableJsonTextWriter(context, parameters.RequestHandler.ResponseBodyStream()))
                {
                    writer.WritePutIndexResponse(context, createdIndexes);
                }
            }
        }

        internal class PutIndexParameters
        {
            public PutIndexParameters(RequestHandler requestHandler, bool validatedAsAdmin, TransactionContextPool contextPool, 
                string databaseName, Func<(IndexDefinition IndexDefinition, string RaftRequestId, string Source), Task<long>> putIndexTask,
                Func<(JsonOperationContext Context, List<long> RaftIndexIds), Task> waitForIndexNotification = null)
            {
                RequestHandler = requestHandler;
                ValidatedAsAdmin = validatedAsAdmin;
                ContextPool = contextPool;
                DatabaseName = databaseName;
                PutIndexTask = putIndexTask;
                WaitForIndexNotification = waitForIndexNotification;
            }

            public RequestHandler RequestHandler { get; }

            public bool ValidatedAsAdmin { get; }

            public TransactionContextPool ContextPool { get; }

            public string DatabaseName { get; }

            public Func<(IndexDefinition IndexDefinition, string RaftRequestId, string Source), Task<long>> PutIndexTask { get; }

            public Func<(JsonOperationContext, List<long>), Task> WaitForIndexNotification { get; }
        }

        private async Task HandleLegacyIndexesAsync()
        {
            using (ContextPool.AllocateOperationContext(out JsonOperationContext jsonOperationContext))
            await using (var stream = new ArrayStream(RequestBodyStream(), nameof(DatabaseItemType.Indexes)))
            using (var source = new StreamSource(stream, jsonOperationContext, Database.Name))
            {
                var destination = new DatabaseDestination(Database);
                var options = new DatabaseSmugglerOptionsServerSide
                {
                    OperateOnTypes = DatabaseItemType.Indexes
                };

                var smuggler = SmugglerBase.GetDatabaseSmuggler(Database, source, destination, Database.Time, jsonOperationContext, options);
                await smuggler.ExecuteAsync();
            }
        }

        public static bool IsLocalRequest(HttpContext context)
        {
            if (context.Connection.RemoteIpAddress == null && context.Connection.LocalIpAddress == null)
            {
                return true;
            }
            if (context.Connection.RemoteIpAddress.Equals(context.Connection.LocalIpAddress))
            {
                return true;
            }
            if (IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
            {
                return true;
            }
            return false;
        }

        [RavenAction("/databases/*/admin/indexes/stop", "POST", AuthorizationStatus.DatabaseAdmin)]
        public Task Stop()
        {
            var types = HttpContext.Request.Query["type"];
            var names = HttpContext.Request.Query["name"];
            if (types.Count == 0 && names.Count == 0)
            {
                Database.IndexStore.StopIndexing();
                return NoContent();
            }

            if (types.Count != 0 && names.Count != 0)
                throw new ArgumentException("Query string value 'type' and 'names' are mutually exclusive.");

            if (types.Count != 0)
            {
                if (types.Count != 1)
                    throw new ArgumentException("Query string value 'type' must appear exactly once");
                if (string.IsNullOrWhiteSpace(types[0]))
                    throw new ArgumentException("Query string value 'type' must have a non empty value");

                if (string.Equals(types[0], "map", StringComparison.OrdinalIgnoreCase))
                {
                    Database.IndexStore.StopMapIndexes();
                }
                else if (string.Equals(types[0], "map-reduce", StringComparison.OrdinalIgnoreCase))
                {
                    Database.IndexStore.StopMapReduceIndexes();
                }
                else
                {
                    throw new ArgumentException("Query string value 'type' can only be 'map' or 'map-reduce' but was " + types[0]);
                }
            }
            else if (names.Count != 0)
            {
                if (names.Count != 1)
                    throw new ArgumentException("Query string value 'name' must appear exactly once");
                if (string.IsNullOrWhiteSpace(names[0]))
                    throw new ArgumentException("Query string value 'name' must have a non empty value");

                Database.IndexStore.StopIndex(names[0]);
            }

            return NoContent();
        }

        [RavenAction("/databases/*/admin/indexes/start", "POST", AuthorizationStatus.DatabaseAdmin)]
        public async Task Start()
        {
            using (var processor = new AdminIndexHandlerProcessorForStart(this))
                await processor.ExecuteAsync();
        }

        [RavenAction("/databases/*/admin/indexes/enable", "POST", AuthorizationStatus.DatabaseAdmin)]
        public async Task Enable()
        {
            var raftRequestId = GetRaftRequestIdFromQuery();
            var name = GetStringQueryString("name");
            var clusterWide = GetBoolValueQueryString("clusterWide", false) ?? false;
            var index = Database.IndexStore.GetIndex(name);
            if (index == null)
                IndexDoesNotExistException.ThrowFor(name);

            if (clusterWide)
            {
                await Database.IndexStore.SetState(name, IndexState.Normal, $"{raftRequestId}/{index}");
            }
            else
            {
                index.Enable();
            }

            NoContentStatus();
        }

        [RavenAction("/databases/*/admin/indexes/disable", "POST", AuthorizationStatus.DatabaseAdmin)]
        public async Task Disable()
        {
            var raftRequestId = GetRaftRequestIdFromQuery();
            var name = GetStringQueryString("name");
            var clusterWide = GetBoolValueQueryString("clusterWide", false) ?? false;
            var index = Database.IndexStore.GetIndex(name);
            if (index == null)
                IndexDoesNotExistException.ThrowFor(name);

            if (clusterWide)
            {
                await Database.IndexStore.SetState(name, IndexState.Disabled, $"{raftRequestId}/{index}");
            }
            else
            {
                index.Disable();
            }

            NoContentStatus();
        }

        [RavenAction("/databases/*/admin/indexes/dump", "POST", AuthorizationStatus.DatabaseAdmin)]
        public async Task Dump()
        {
            var name = GetStringQueryString("name");
            var path = GetStringQueryString("path");
            var index = Database.IndexStore.GetIndex(name);
            if (index == null)
            {
                IndexDoesNotExistException.ThrowFor(name);
                return; //never hit
            }

            var operationId = Database.Operations.GetNextOperationId();
            var token = CreateTimeLimitedQueryOperationToken();

            _ = Database.Operations.AddOperation(
                Database,
                "Dump index " + name + " to " + path,
                Operations.Operations.OperationType.DumpRawIndexData,
                onProgress =>
                {
                    var totalFiles = index.Dump(path, onProgress);
                    return Task.FromResult((IOperationResult)new DumpIndexResult
                    {
                        Message = $"Dumped {totalFiles} files from {name}",
                    });
                }, operationId, token: token);

            using (ContextPool.AllocateOperationContext(out DocumentsOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                writer.WriteOperationIdAndNodeTag(context, operationId, ServerStore.NodeTag);
            }
        }

        public class DumpIndexResult : IOperationResult
        {
            public string Message { get; set; }

            public DynamicJsonValue ToJson()
            {
                return new DynamicJsonValue(GetType())
                {
                    [nameof(Message)] = Message,
                };
            }

            public bool ShouldPersist => false;
        }

        public class DumpIndexProgress : IOperationProgress
        {
            public int ProcessedFiles { get; set; }
            public int TotalFiles { get; set; }
            public string Message { get; set; }
            public long CurrentFileSizeInBytes { get; internal set; }
            public long CurrentFileCopiedBytes { get; internal set; }

            public virtual DynamicJsonValue ToJson()
            {
                return new DynamicJsonValue(GetType())
                {
                    [nameof(ProcessedFiles)] = ProcessedFiles,
                    [nameof(TotalFiles)] = TotalFiles,
                    [nameof(Message)] = Message,
                    [nameof(CurrentFileSizeInBytes)] = CurrentFileSizeInBytes,
                    [nameof(CurrentFileCopiedBytes)] = CurrentFileCopiedBytes
                };
            }
        }
    }
}
