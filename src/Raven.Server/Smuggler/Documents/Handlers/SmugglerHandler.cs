// -----------------------------------------------------------------------
//  <copyright file="SmugglerHandler.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jint;
using Jint.Parser.Ast;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Raven.Abstractions;
using Raven.Client.Data;
using Raven.Client.Smuggler;
using Raven.Server.Documents;
using Raven.Server.Json;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Smuggler.Documents.Data;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Smuggler.Documents.Handlers
{
    public class SmugglerHandler : DatabaseRequestHandler
    {

        [RavenAction("/databases/*/smuggler/validate-options", "POST")]
        public async Task PostValidateOptions()
        {
            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            using (context.OpenReadTransaction())
            {
                var blittableJson = await context.ReadForMemoryAsync(RequestBodyStream(), "");
                var options = JsonDeserializationServer.DatabaseSmugglerOptions(blittableJson);

                if (!string.IsNullOrEmpty(options.FileName) && options.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException($"{options.FileName} is Invalid File Name");

                if (string.IsNullOrEmpty(options?.TransformScript))
                    return;

                try
                {
                    var jint = new Engine(cfg =>
                    {
                        cfg.AllowDebuggerStatement(false);
                        cfg.MaxStatements(options.MaxStepsForTransformScript);
                        cfg.NullPropagation();
                    });

                    jint.Execute(string.Format(@"
                    function Transform(docInner){{
                        return ({0}).apply(this, [docInner]);
                    }};", options.TransformScript));
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Incorrect transform script", e);
                }

                HttpContext.Response.StatusCode = (int)HttpStatusCode.NoContent;
            }
        }

        [RavenAction("/databases/*/smuggler/export", "POST")]
        public async Task PostExport()
        {
            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            using (context.OpenReadTransaction())
            {
                var operationId = GetIntValueQueryString("operationId", required: false);

                var stream = TryGetRequestFormStream("DownloadOptions") ?? RequestBodyStream();

                var blittableJson = await context.ReadForMemoryAsync(stream, "DownloadOptions");
                var options = JsonDeserializationServer.DatabaseSmugglerOptions(blittableJson);

                var exporter = new SmugglerExporter(Database, options);
                var token = CreateOperationToken();

                var fileName = exporter.Options.FileName;
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = $"Dump of {context.DocumentDatabase.Name} {SystemTime.UtcNow.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)}";
                }

                var contentDisposition = "attachment; filename=" + Uri.EscapeDataString(fileName) + ".ravendbdump";
                HttpContext.Response.Headers["Content-Disposition"] = contentDisposition;

                if (operationId.HasValue)
                {
                    try
                    {
                        await
                            Database.Operations.AddOperation("Export database: " + Database.Name,
                                DatabaseOperations.PendingOperationType.DatabaseExport,
                                onProgress =>
                                    Task.Run(() => ExportDatabaseInternal(context, exporter, onProgress, token),
                                        token.Token), operationId.Value, token);
                    }
                    catch (Exception)
                    {
                        HttpContext.Abort();
                    }

                }
                else
                {
                    ExportDatabaseInternal(context, exporter, null, token);
                }
            }
        }

        private Stream TryGetRequestFormStream(string itemName)
        {
            if (HttpContext.Request.HasFormContentType == false)
                return null;

            StringValues value;
            if (HttpContext.Request.Form.TryGetValue(itemName, out value) == false)
                return null;

            if (value.Count == 0)
                return null;

            return new MemoryStream(Encoding.UTF8.GetBytes(value[0]));
        }

        private IOperationResult ExportDatabaseInternal(DocumentsOperationContext context, SmugglerExporter exporter, Action<IOperationProgress> onProgress, OperationCancelToken token)
        {
            try
            {
                return exporter.Export(context, ResponseBodyStream(), onProgress);
            }
            finally
            {
                token.Dispose();
            }
        }

        [RavenAction("/databases/*/smuggler/import-s3-dir", "GET")]
        public async Task PostImportFromS3Directory()
        {
            var sp = Stopwatch.StartNew();

            var url = GetQueryStringValueAndAssertIfSingleAndNotEmpty("url");
            using (var httpClient = new HttpClient())
            {
                var result = await httpClient.GetAsync(url);
                var dirTextXml = await result.Content.ReadAsStringAsync();
                var filesListing = XElement.Parse(dirTextXml);
                var ns = XNamespace.Get("http://s3.amazonaws.com/doc/2006-03-01/");
                var urls = from content in filesListing.Elements(ns + "Contents")
                           let requestUri = url.TrimEnd('/') + "/" + content.Element(ns + "Key").Value
                           select (Func<Task<Stream>>)(async () =>
                          {
                              var respone = await httpClient.GetAsync(requestUri);
                              if (respone.IsSuccessStatusCode == false)
                                  throw new InvalidOperationException("Request failed on " + requestUri + " with " +
                                                                      await respone.Content.ReadAsStreamAsync());
                              return await respone.Content.ReadAsStreamAsync();
                          });

                var files = new BlockingCollection<Func<Task<Stream>>>(new ConcurrentQueue<Func<Task<Stream>>>(urls));
                files.CompleteAdding();
                await BulkImport(files, sp, Path.GetTempPath());
            }
        }

        [RavenAction("/databases/*/smuggler/import-dir", "GET")]
        public async Task PostImportDirectory()
        {
            var sp = Stopwatch.StartNew();

            var directory = GetQueryStringValueAndAssertIfSingleAndNotEmpty("dir");
            var files = new BlockingCollection<Func<Task<Stream>>>(new ConcurrentQueue<Func<Task<Stream>>>(
                    Directory.GetFiles(directory, "*.dump")
                        .Select(x => (Func<Task<Stream>>)(() => Task.FromResult<Stream>(File.OpenRead(x)))))
            );
            files.CompleteAdding();
            await BulkImport(files, sp, directory);
        }

        private async Task BulkImport(BlockingCollection<Func<Task<Stream>>> files, Stopwatch sp, string directory)
        {
            var results = new ConcurrentQueue<ImportResult>();
            var tasks = new Task[Environment.ProcessorCount];

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    while (files.IsCompleted == false)
                    {
                        Func<Task<Stream>> getFile;
                        DocumentsOperationContext context;
                        try
                        {
                            getFile = files.Take();
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                        using (ContextPool.AllocateOperationContext(out context))
                        using (Stream file = await getFile())
                        using (var stream = new GZipStream(file, CompressionMode.Decompress))
                        {
                            var importer = new SmugglerImporter(Database);
                            var result = await importer.Import(context, stream);
                            results.Enqueue(result);
                        }
                    }
                });
            }
            await Task.WhenAll(tasks);

            var finalResult = new ImportResult();
            ImportResult importResult;
            while (results.TryDequeue(out importResult))
            {
                finalResult.DocumentsCount += importResult.DocumentsCount;
                finalResult.IdentitiesCount += importResult.IdentitiesCount;
                finalResult.IndexesCount += importResult.IndexesCount;
                finalResult.RevisionDocumentsCount += importResult.RevisionDocumentsCount;
                finalResult.TransformersCount += importResult.TransformersCount;
                finalResult.Warnings.AddRange(importResult.Warnings);
            }
            sp.Stop();

            DocumentsOperationContext finalContext;
            using (ContextPool.AllocateOperationContext(out finalContext))
            {
                var memoryStream = new MemoryStream();
                WriteImportResult(finalContext, sp, finalResult, memoryStream);
                memoryStream.Position = 0;
                try
                {
                    using (var output = File.Create(Path.Combine(directory, "smuggler.results.txt")))
                    {
                        memoryStream.CopyTo(output);
                    }
                }
                catch (Exception)
                {
                    // ignore any failure here
                }
                memoryStream.Position = 0;
                memoryStream.CopyTo(ResponseBodyStream());
            }
        }



        [RavenAction("/databases/*/smuggler/import", "GET")]
        public Task GetImport()
        {
            if (HttpContext.Request.Query.ContainsKey("file") == false &&
                HttpContext.Request.Query.ContainsKey("url") == false)
            {
                throw new ArgumentException("'file' or 'url' are mandatory when using GET /smuggler/import");
            }
            return PostImport();
        }

        [RavenAction("/databases/*/smuggler/import", "POST")]
        public async Task PostImport()
        {
            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            {
                var options = DatabaseSmugglerOptionsServerSide.Create(HttpContext, context);

                var tuple = await GetImportStream();
                using (tuple.Item2)
                using (var stream = new GZipStream(tuple.Item1, CompressionMode.Decompress))
                {
                    var sp = Stopwatch.StartNew();
                    var importer = new SmugglerImporter(Database, options);
                    var result = await importer.Import(context, stream);
                    sp.Stop();
                    WriteImportResult(context, sp, result, ResponseBodyStream());
                }
            }
        }

        [RavenAction("/databases/*/smuggler/import/async", "POST")]
        public async Task PostImportAsync()
        {
            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            {
                if (HttpContext.Request.HasFormContentType == false)
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest; // Bad request
                    using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
                    {
                        context.Write(writer, new DynamicJsonValue
                        {
                            ["Type"] = "Error",
                            ["Error"] = "This endpoint requires form content type"
                        });
                        return;
                    }
                }

                var operationId = GetLongQueryString("operationId", required: true) ?? -1;
                var token = CreateOperationToken();
                var sp = Stopwatch.StartNew();

                var result = new ImportResult();
                await Database.Operations.AddOperation("Import to: " + Database.Name,
                    DatabaseOperations.PendingOperationType.DatabaseImport,
                    onProgress =>
                    {
                        return Task.Run(async () =>
                        {
                            try
                            {
                                var boundary = MultipartRequestHelper.GetBoundary(
                                    MediaTypeHeaderValue.Parse(HttpContext.Request.ContentType),
                                    MultipartRequestHelper.MultipartBoundaryLengthLimit);
                                var reader = new MultipartReader(boundary, HttpContext.Request.Body);
                                DatabaseSmugglerOptions smugglerOptions = null;

                                while (true)
                                {
                                    var section = await reader.ReadNextSectionAsync().ConfigureAwait(false);
                                    if (section == null)
                                        break;

                                    ContentDispositionHeaderValue contentDisposition;
                                    var hasContentDispositionHeader = ContentDispositionHeaderValue.TryParse(section.ContentDisposition,
                                        out contentDisposition);

                                    if (hasContentDispositionHeader == false)
                                        continue;

                                    if (MultipartRequestHelper.HasFormDataContentDisposition(contentDisposition))
                                    {
                                        var key = HeaderUtilities.RemoveQuotes(contentDisposition.Name);
                                        if (key != "importOptions")
                                            continue;

                                        var blittableJson = await context.ReadForMemoryAsync(section.Body, "importOptions");
                                        smugglerOptions = JsonDeserializationServer.DatabaseSmugglerOptions(blittableJson);
                                        continue;
                                    }

                                    if (MultipartRequestHelper.HasFileContentDisposition(contentDisposition) == false)
                                        continue;

                                    var stream = new GZipStream(section.Body, CompressionMode.Decompress);
                                    var fileImport =
                                        await DoImportInternal(context, stream, smugglerOptions, onProgress).ConfigureAwait(false);
                                    result.DocumentsCount += fileImport.DocumentsCount;
                                    result.Warnings.AddRange(fileImport.Warnings);
                                    result.IdentitiesCount += fileImport.IdentitiesCount;
                                    result.Message = fileImport.Message;
                                    result.RevisionDocumentsCount += fileImport.RevisionDocumentsCount;
                                    result.IndexesCount += fileImport.IndexesCount;
                                    result.TransformersCount += fileImport.TransformersCount;
                                }
                            }
                            catch (Exception e)
                            {
                                result.Message = $"Error occured during export. Exception: {e.Message}";
                                result.Exception = e.ToString();
                                throw;
                            }

                            return (IOperationResult)result;
                        });
                    }, operationId, token).ConfigureAwait(false);

                WriteImportResult(context, sp, result, ResponseBodyStream());
            }
        }

        private async Task<ImportResult> DoImportInternal(DocumentsOperationContext context, Stream stream,
            DatabaseSmugglerOptions smugglerOptions, Action<IOperationProgress> onProgress)
        {
            try
            {
                var importer = new SmugglerImporter(Database, smugglerOptions);

                return await importer.Import(context, stream, onProgress);
            }
            finally
            {
                stream.Dispose();

            }

        }

        private async Task<Tuple<Stream, IDisposable>> GetImportStream()
        {
            var file = GetStringQueryString("file", required: false);
            if (string.IsNullOrEmpty(file) == false)
                return Tuple.Create<Stream, IDisposable>(File.OpenRead(file), null);

            var url = GetStringQueryString("url", required: false);
            if (string.IsNullOrEmpty(url) == false)
            {
                var httpClient = new HttpClient();

                var stream = await httpClient.GetStreamAsync(url);
                return Tuple.Create<Stream, IDisposable>(stream, httpClient);
            }

            return Tuple.Create<Stream, IDisposable>(HttpContext.Request.Body, null);
        }


        private void WriteImportResult(DocumentsOperationContext context, Stopwatch sp, ImportResult result, Stream stream)
        {
            using (var writer = new BlittableJsonTextWriter(context, stream))
            {
                var json = result.ToJson();
                json["ElapsedMilliseconds"] = sp.ElapsedMilliseconds;
                json["Elapsed"] = sp.Elapsed.ToString();
                context.Write(writer, json);
            }
        }
    }
}