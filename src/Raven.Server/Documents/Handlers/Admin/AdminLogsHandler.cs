﻿using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Raven.Client.ServerWide.Operations.Logs;
using Raven.Server.Json;
using Raven.Server.Routing;
using Raven.Server.Web;
using Sparrow;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Logging;
using Sparrow.Server.Platform.Posix;

namespace Raven.Server.Documents.Handlers.Admin
{
    public class AdminLogsHandler : ServerRequestHandler
    {
        [RavenAction("/admin/logs/configuration", "GET", AuthorizationStatus.Operator)]
        public async Task GetConfiguration()
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var djv = new DynamicJsonValue
                {
                    [nameof(GetLogsConfigurationResult.CurrentMode)] = LoggingSource.Instance.LogMode,
                    [nameof(GetLogsConfigurationResult.Mode)] = ServerStore.Configuration.Logs.Mode,
                    [nameof(GetLogsConfigurationResult.Path)] = ServerStore.Configuration.Logs.Path.FullPath,
                    [nameof(GetLogsConfigurationResult.UseUtcTime)] = ServerStore.Configuration.Logs.UseUtcTime,
                    [nameof(GetLogsConfigurationResult.RetentionTime)] = LoggingSource.Instance.RetentionTime,
                    [nameof(GetLogsConfigurationResult.RetentionSize)] = LoggingSource.Instance.RetentionSize == long.MaxValue ? null : (object)LoggingSource.Instance.RetentionSize,
                    [nameof(GetLogsConfigurationResult.Compress)] = LoggingSource.Instance.Compressing
                };

                var json = context.ReadObject(djv, "logs/configuration");

                writer.WriteObject(json);
            }
        }

        [RavenAction("/admin/logs/configuration", "POST", AuthorizationStatus.Operator)]
        public async Task SetConfiguration()
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                var json = await context.ReadForMemoryAsync(RequestBodyStream(), "logs/configuration");

                var configuration = JsonDeserializationServer.Parameters.SetLogsConfigurationParameters(json);

                if (configuration.RetentionTime == null)
                    configuration.RetentionTime = ServerStore.Configuration.Logs.RetentionTime?.AsTimeSpan;

                LoggingSource.Instance.SetupLogMode(
                    configuration.Mode,
                    Server.Configuration.Logs.Path.FullPath,
                    configuration.RetentionTime,
                    configuration.RetentionSize?.GetValue(SizeUnit.Bytes),
                    configuration.Compress);
            }

            NoContentStatus();
        }

        [RavenAction("/admin/logs/watch", "GET", AuthorizationStatus.Operator)]
        public async Task RegisterForLogs()
        {
            using (var socket = await HttpContext.WebSockets.AcceptWebSocketAsync())
            {
                var context = new LoggingSource.WebSocketContext();

                foreach (var filter in HttpContext.Request.Query["only"])
                {
                    context.Filter.Add(filter, true);
                }
                foreach (var filter in HttpContext.Request.Query["except"])
                {
                    context.Filter.Add(filter, false);
                }

                await LoggingSource.Instance.Register(socket, context, ServerStore.ServerShutdown);
            }
        }

        [RavenAction("/admin/logs/download", "GET", AuthorizationStatus.Operator)]
        public async Task Download()
        {
            var contentDisposition = $"attachment; filename={DateTime.UtcNow:yyyy-MM-dd H:mm:ss} - Node {ServerStore.NodeTag} - Logs.zip";
            HttpContext.Response.Headers["Content-Disposition"] = contentDisposition;

            using (ServerStore.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            {
                await using (var ms = new MemoryStream())
                {
                    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        foreach (var filePath in Directory.GetFiles(ServerStore.Configuration.Logs.Path.FullPath))
                        {
                            try
                            {
                                using (var fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    var fileName = Path.GetFileName(filePath);
                                    var entry = archive.CreateEntry(fileName);

                                    entry.ExternalAttributes = ((int)(FilePermissions.S_IRUSR | FilePermissions.S_IWUSR)) << 16;

                                    await using (var entryStream = entry.Open())
                                    {
                                        await fs.CopyToAsync(entryStream);
                                    }
                                }
                            }
                            catch
                            {
                                // something went wrong with copying the file, will continue to the next one
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
