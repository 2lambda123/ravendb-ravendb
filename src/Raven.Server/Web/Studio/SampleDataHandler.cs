﻿using System;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using Raven.Server.Documents;
using Raven.Server.Routing;
using Raven.Server.ServerWide.Context;
using Raven.Server.Smuggler.Documents;

namespace Raven.Server.Web.Studio
{
    public class SampleDataHandler : DatabaseRequestHandler
    {

        [RavenAction("/databases/*/studio/sample-data", "POST")]
        public async Task PostCreateSampleData()
        {
            DocumentsOperationContext context;
            using (ContextPool.AllocateOperationContext(out context))
            {
                using (context.OpenReadTransaction())
                {
                    foreach (var collection in Database.DocumentsStorage.GetCollections(context))
                    {
                        if (collection.Count > 0 && collection.Name != CollectionName.SystemCollection)
                        {
                            throw new InvalidOperationException("You cannot create sample data in a database that already contains documents");
                        }
                    }
                }

                using (var sampleData = typeof(SampleDataHandler).GetTypeInfo().Assembly.GetManifestResourceStream("Raven.Server.Web.Studio.EmbeddedData.Northwind_3.5.35168.ravendbdump"))
                {
                    using (var stream = new GZipStream(sampleData, CompressionMode.Decompress))
                    {
                        var importer = new SmugglerImporter(Database);

                        await importer.Import(context, stream);
                    }
                }
            }
        }

        [RavenAction("/databases/*/studio/sample-data/classes", "GET")]
        public async Task GetSampleDataClasses()
        {
            using (var sampleData = typeof(SampleDataHandler).GetTypeInfo().Assembly.GetManifestResourceStream("Raven.Server.Web.Studio.EmbeddedData.NorthwindModel.cs"))
            using (var responseStream = ResponseBodyStream())
            {
                HttpContext.Response.ContentType = "text/plain";
                await sampleData.CopyToAsync(responseStream);
            }
        }
    }
}