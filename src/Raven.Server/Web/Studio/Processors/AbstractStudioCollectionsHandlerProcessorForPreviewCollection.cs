﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Raven.Client;
using Raven.Server.Documents;
using Raven.Server.Json;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Web.Studio.Processors;

public abstract class AbstractStudioCollectionsHandlerProcessorForPreviewCollection<TRequestHandler> : IDisposable
    where TRequestHandler : RequestHandler
{
    private const int ColumnsSamplingLimit = 10;
    private const int StringLengthLimit = 255;

    private const string ObjectStubsKey = "$o";
    private const string ArrayStubsKey = "$a";
    private const string TrimmedValueKey = "$t";

    protected readonly TRequestHandler RequestHandler;

    protected readonly HttpContext HttpContext;

    protected string Collection;

    protected bool IsAllDocsCollection;

    private StringValues _bindings;

    private StringValues _fullBindings;

    protected AbstractStudioCollectionsHandlerProcessorForPreviewCollection([NotNull] TRequestHandler requestHandler)
    {
        RequestHandler = requestHandler ?? throw new ArgumentNullException(nameof(requestHandler));
        HttpContext = requestHandler.HttpContext;
    }

    protected virtual void Initialize()
    {
        Collection = RequestHandler.GetStringQueryString("collection", required: false);
        _bindings = RequestHandler.GetStringValuesQueryString("binding", required: false);
        _fullBindings = RequestHandler.GetStringValuesQueryString("fullBinding", required: false);

        IsAllDocsCollection = string.IsNullOrEmpty(Collection);
    }

    protected abstract JsonOperationContext GetContext();

    protected abstract long GetTotalResults();

    protected abstract bool NotModified(out string etag);

    protected abstract ValueTask<List<Document>> GetDocumentsAsync();

    protected abstract List<string> GetAvailableColumns(List<Document> documents);

    public async Task ExecuteAsync()
    {
        Initialize();

        if (NotModified(out var etag))
            return;

        if (etag != null)
            HttpContext.Response.Headers["ETag"] = "\"" + etag + "\"";

        var documents = await GetDocumentsAsync();
        var availableColumns = GetAvailableColumns(documents);

        var propertiesPreviewToSend = IsAllDocsCollection
            ? _bindings.Count > 0 ? new HashSet<string>(_bindings) : new HashSet<string>()
            : _bindings.Count > 0 ? new HashSet<string>(_bindings) : availableColumns.Take(ColumnsSamplingLimit).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet();

        var fullPropertiesToSend = new HashSet<string>(_fullBindings);

        var context = GetContext();

        await using (var writer = new AsyncBlittableJsonTextWriter(context, RequestHandler.ResponseBodyStream()))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Results");

            writer.WriteStartArray();

            var first = true;
            foreach (var document in documents)
            {
                if (first == false)
                    writer.WriteComma();
                first = false;

                using (document.Data)
                {
                    WriteDocument(writer, context, document, propertiesPreviewToSend, fullPropertiesToSend);
                }
            }

            writer.WriteEndArray();

            writer.WriteComma();

            writer.WritePropertyName("TotalResults");
            writer.WriteInteger(GetTotalResults());

            writer.WriteComma();

            writer.WriteArray("AvailableColumns", availableColumns);

            writer.WriteEndObject();
        }
    }

    private static void WriteDocument(AsyncBlittableJsonTextWriter writer, JsonOperationContext context, Document document, HashSet<string> propertiesPreviewToSend, HashSet<string> fullPropertiesToSend)
    {
        writer.WriteStartObject();

        document.Data.TryGet(Constants.Documents.Metadata.Key, out BlittableJsonReaderObject metadata);

        bool first = true;

        var arrayStubsJson = new DynamicJsonValue();
        var objectStubsJson = new DynamicJsonValue();
        var trimmedValue = new HashSet<LazyStringValue>();

        var prop = new BlittableJsonReaderObject.PropertyDetails();

        using (var buffers = document.Data.GetPropertiesByInsertionOrder())
        {
            for (int i = 0; i < buffers.Size; i++)
            {
                unsafe
                {
                    document.Data.GetPropertyByIndex(buffers.Properties[i], ref prop);
                }

                var sendFull = fullPropertiesToSend.Contains(prop.Name);
                if (sendFull || propertiesPreviewToSend.Contains(prop.Name))
                {
                    var strategy = sendFull ? ValueWriteStrategy.Passthrough : FindWriteStrategy(prop.Token & BlittableJsonReaderBase.TypesMask, prop.Value);

                    if (strategy == ValueWriteStrategy.Passthrough || strategy == ValueWriteStrategy.Trim)
                    {
                        if (first == false)
                        {
                            writer.WriteComma();
                        }

                        first = false;
                    }

                    switch (strategy)
                    {
                        case ValueWriteStrategy.Passthrough:
                            writer.WritePropertyName(prop.Name);
                            writer.WriteValue(prop.Token & BlittableJsonReaderBase.TypesMask, prop.Value);
                            break;

                        case ValueWriteStrategy.SubstituteWithArrayStub:
                            arrayStubsJson[prop.Name] = ((BlittableJsonReaderArray)prop.Value).Length;
                            break;

                        case ValueWriteStrategy.SubstituteWithObjectStub:
                            objectStubsJson[prop.Name] = ((BlittableJsonReaderObject)prop.Value).Count;
                            break;

                        case ValueWriteStrategy.Trim:
                            writer.WritePropertyName(prop.Name);
                            WriteTrimmedValue(writer, prop.Token & BlittableJsonReaderBase.TypesMask, prop.Value);
                            trimmedValue.Add(prop.Name);
                            break;
                    }
                }
            }
        }

        if (first == false)
            writer.WriteComma();

        var extraMetadataProperties = new DynamicJsonValue(metadata)
        {
            [ObjectStubsKey] = objectStubsJson,
            [ArrayStubsKey] = arrayStubsJson,
            [TrimmedValueKey] = new DynamicJsonArray(trimmedValue)
        };

        if (metadata != null)
        {
            metadata.Modifications = extraMetadataProperties;

            if (document.Flags.Contain(DocumentFlags.HasCounters) || document.Flags.Contain(DocumentFlags.HasAttachments) || document.Flags.Contain(DocumentFlags.HasTimeSeries))
            {
                metadata.Modifications.Remove(Constants.Documents.Metadata.Counters);
                metadata.Modifications.Remove(Constants.Documents.Metadata.Attachments);
                metadata.Modifications.Remove(Constants.Documents.Metadata.TimeSeries);
            }

            using (var old = metadata)
            {
                metadata = context.ReadObject(metadata, document.Id);
            }
        }
        else
        {
            metadata = context.ReadObject(extraMetadataProperties, document.Id);
        }

        writer.WriteMetadata(document, metadata);
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteTrimmedValue(AsyncBlittableJsonTextWriter writer, BlittableJsonToken token, object val)
    {
        switch (token)
        {
            case BlittableJsonToken.String:
                var lazyString = (LazyStringValue)val;
                writer.WriteString(lazyString?.Substring(0,
                    Math.Min(lazyString.Length, StringLengthLimit)));
                break;

            case BlittableJsonToken.CompressedString:
                var lazyCompressedString = (LazyCompressedStringValue)val;
                string actualString = lazyCompressedString.ToString();
                writer.WriteString(actualString.Substring(0, Math.Min(actualString.Length, StringLengthLimit)));
                break;

            default:
                throw new DataMisalignedException($"Unidentified Type {token}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ValueWriteStrategy FindWriteStrategy(BlittableJsonToken token, object val)
    {
        switch (token)
        {
            case BlittableJsonToken.String:
                var lazyString = (LazyStringValue)val;
                return lazyString.Length > StringLengthLimit ? ValueWriteStrategy.Trim : ValueWriteStrategy.Passthrough;

            case BlittableJsonToken.Integer:
                return ValueWriteStrategy.Passthrough;

            case BlittableJsonToken.StartArray:
                return ValueWriteStrategy.SubstituteWithArrayStub;

            case BlittableJsonToken.EmbeddedBlittable:
            case BlittableJsonToken.StartObject:
                return ValueWriteStrategy.SubstituteWithObjectStub;

            case BlittableJsonToken.CompressedString:
                var lazyCompressedString = (LazyCompressedStringValue)val;
                return lazyCompressedString.UncompressedSize > StringLengthLimit ? ValueWriteStrategy.Trim : ValueWriteStrategy.Passthrough;

            case BlittableJsonToken.LazyNumber:
            case BlittableJsonToken.Boolean:
            case BlittableJsonToken.Null:
                return ValueWriteStrategy.Passthrough;

            default:
                throw new DataMisalignedException($"Unidentified Type {token}");
        }
    }

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    private enum ValueWriteStrategy
    {
        Passthrough,
        Trim,
        SubstituteWithObjectStub,
        SubstituteWithArrayStub
    }
}
