﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Sparrow.Json;

namespace Raven.Server.Documents.Handlers.Debugging
{
    public class DatabaseDebugInfoPackageHandler : DatabaseRequestHandler
    {
        [RavenAction("/databases/*/debug/info-package", "GET", IsDebugInformationEndpoint = true)]
        public async Task GetInfoPackage()
        {
            var contentDisposition = $"attachment; filename=debug-info of {Database.Name} {DateTime.UtcNow}.zip";
            HttpContext.Response.Headers["Content-Disposition"] = contentDisposition;
            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                using (var ms = new MemoryStream())
                {
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        var localEndpointClient = new LocalEndpointClient(Server);
                        var endpointParameters = new Dictionary<string, StringValues>
                        {
                            { "database",new StringValues(Database.Name) },
                        };

                        foreach (var route in DebugInfoPackageUtils.Routes.Where(x => x.TypeOfRoute == RouteInformation.RouteType.Databases))
                        {
                            var entry = archive.CreateEntry(DebugInfoPackageUtils.GetOutputPathFromRouteInformation(route, null));
                            using (var entryStream = entry.Open())
                            using (var writer = new BlittableJsonTextWriter(context, entryStream))
                            {
                                using (var endpointOutput = await localEndpointClient.InvokeAndReadObjectAsync(route, context, endpointParameters))
                                {
                                    context.Write(writer, endpointOutput);
                                    writer.Flush();
                                    await entryStream.FlushAsync();
                                }
                            }
                        }
                    }

                    ms.Position = 0;
                    await ms.CopyToAsync(ResponseBodyStream());
                }
            }
        }
    }
}
