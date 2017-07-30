﻿using System.Net;
using System.Threading.Tasks;
using Raven.Client;
using Raven.Server.Json;
using Raven.Server.Routing;
using Raven.Server.ServerWide.Commands;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Web.System
{
    public class AdminConfigurationHandler : RequestHandler
    {
        [RavenAction("/admin/configuration/client", "PUT", AuthorizationStatus.ServerAdmin)]
        public async Task PutClientConfiguration()
        {
            ServerStore.EnsureNotPassive();

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext ctx))
            {
                var clientConfigurationJson = ctx.ReadForDisk(RequestBodyStream(), Constants.Configuration.ClientId);

                var clientConfiguration = JsonDeserializationServer.ClientConfiguration(clientConfigurationJson);
                var res = await ServerStore.PutValueInClusterAsync(new PutClientConfigurationCommand(clientConfiguration));
                await ServerStore.Cluster.WaitForIndexNotification(res.Etag);

                HttpContext.Response.StatusCode = (int)HttpStatusCode.Created;
            }
        }

        [RavenAction("/admin/configuration/client", "GET", AuthorizationStatus.ServerAdmin)]
        public Task GetClientConfiguration()
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                using (context.OpenReadTransaction())
                {
                    var clientConfigurationJson = ServerStore.Cluster.Read(context, Constants.Configuration.ClientId, out long _);
                    if (clientConfigurationJson == null)
                    {
                        HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
                        return Task.CompletedTask;
                    }

                    using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                    {
                        writer.WriteObject(clientConfigurationJson);
                    }
                }
            }

            return Task.CompletedTask;
        }
    }
}