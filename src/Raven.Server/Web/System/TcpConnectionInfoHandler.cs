﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features.Authentication;
using Raven.Client.Documents.Operations.Replication;
using Raven.Client.Exceptions.Security;
using Raven.Client.ServerWide;
using Raven.Server.Extensions;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Web.System
{
    public class TcpConnectionInfoHandler : RequestHandler
    {
        [RavenAction("/info/tcp", "GET", AuthorizationStatus.ValidUser)]
        public Task Get()
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var output = Server.ServerStore.GetTcpInfoAndCertificates(HttpContext.Request.GetClientRequestedNodeUrl());
                context.Write(writer, output);
            }

            return Task.CompletedTask;
        }

        [RavenAction("/info/remote-task/topology", "GET", AuthorizationStatus.RestrictedAccess)]
        public Task GetRemoteTaskTopology()
        {
            var database = GetStringQueryString("database");
            var databaseGroupId = GetStringQueryString("groupId");
            var remoteTask = GetStringQueryString("remote-task");

            Authenticate(HttpContext, ServerStore, database, remoteTask);

            List<string> nodes;
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                var pullReplication = ServerStore.Cluster.ReadPullReplicationDefinition(database, remoteTask, context);
                var topology = ServerStore.Cluster.ReadDatabaseTopology(context, database);
                nodes = GetResponsibleNodes(topology, databaseGroupId, pullReplication);
            }

            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var output = new DynamicJsonArray();
                var clusterTopology = ServerStore.GetClusterTopology();
                foreach (var node in nodes)
                {
                    output.Add(clusterTopology.GetUrlFromTag(node));
                }
                context.Write(writer, new DynamicJsonValue
                {
                    ["Results"] = output
                });
            }
            return Task.CompletedTask;
        }

        [RavenAction("/info/remote-task/tcp", "GET", AuthorizationStatus.RestrictedAccess)]
        public Task GetRemoteTaskTcp()
        {
            var remoteTask = GetStringQueryString("remote-task");
            var database = GetStringQueryString("database");

            Authenticate(HttpContext, ServerStore, database, remoteTask);

            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var output = Server.ServerStore.GetTcpInfoAndCertificates(HttpContext.Request.GetClientRequestedNodeUrl());
                context.Write(writer, output);
            }

            return Task.CompletedTask;
        }

        public static void Authenticate(HttpContext httpContext, ServerStore serverStore, string database, string remoteTask)
        {
            var feature = httpContext.Features.Get<IHttpAuthenticationFeature>() as RavenServer.AuthenticateConnection;

            if (feature == null) // we are not using HTTPS 
                return;

            switch (feature.Status)
            {
                case RavenServer.AuthenticationStatus.Operator:
                case RavenServer.AuthenticationStatus.ClusterAdmin:
                    // we can trust this certificate
                    return;

                case RavenServer.AuthenticationStatus.Allowed:
                    // check that the certificate is allowed for this database.
                    if (feature.CanAccess(database, requireAdmin: false))
                        return;

                    throw new AuthorizationException(PullReplicationAuthorizationExceptionMessage(database, remoteTask));

                case RavenServer.AuthenticationStatus.UnfamiliarCertificate:
                    using (serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                    using (context.OpenReadTransaction())
                    {
                        if (serverStore.Cluster.TryReadPullReplicationDefinition(database, remoteTask, context, out var pullReplication))
                        {
                            var cert = httpContext.Connection.ClientCertificate;
                            if (pullReplication.CanAccess(cert?.Thumbprint))
                                return;
                        }
                        throw new AuthorizationException(PullReplicationAuthorizationExceptionMessage(database, remoteTask));
                    }

                default:
                    throw new ArgumentException($"This is a bug, we should deal with '{feature?.Status}' authentication status at RequestRoute.TryAuthorize function.");
            }
        }

        private static string PullReplicationAuthorizationExceptionMessage(string database, string remoteTask)
        {
            return $"Cannot connect to '{remoteTask}' on '{database}'. The database or task may not exists or you don't have the credentials to access it.";
        }

        private List<string> GetResponsibleNodes(DatabaseTopology topology, string databaseGroupId, PullReplicationDefinition pullReplication)
        {
            var list = new List<string>();
            // we distribute connections to have load balancing when many edges are connected.
            // this is the central cluster, so we make the decision which node will do the pull replication only once and only here,
            // for that we create a dummy IDatabaseTask.
            var mentorNodeTask = new PullNodeTask
            {
                Mentor = pullReplication.MentorNode,
                DatabaseGroupId = databaseGroupId
            };

            while (topology.Members.Count > 0)
            {
                var next = topology.WhoseTaskIsIt(ServerStore.CurrentRachisState, mentorNodeTask, null);
                list.Add(next);
                topology.Members.Remove(next);
            }

            return list;
        }

        private class PullNodeTask : IDatabaseTask
        {
            public string Mentor;
            public string DatabaseGroupId;

            public ulong GetTaskKey()
            {
                return Hashing.Mix(Hashing.XXHash64.Calculate(DatabaseGroupId, Encodings.Utf8));
            }

            public string GetMentorNode()
            {
                return Mentor;
            }

            public string GetDefaultTaskName()
            {
                throw new NotImplementedException();
            }

            public string GetTaskName()
            {
                throw new NotImplementedException();
            }
        }
    }
}
