using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Client.Smuggler;
using Raven.Server.Documents;
using Raven.Server.Documents.Patch;
using Raven.Server.Documents.Versioning;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Smuggler.Documents.Data;
using Raven.Server.Smuggler.Documents.Processors;
using Sparrow;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Smuggler.Documents
{
    public class SmugglerImporter
    {
        private readonly DocumentDatabase _database;

        public DatabaseSmugglerOptions Options;

        public SmugglerImporter(DocumentDatabase database, DatabaseSmugglerOptions options = null)
        {
            _database = database;
            _batchPutCommand = new MergedBatchPutCommand(_database, 0);
            Options = options ?? new DatabaseSmugglerOptions();
        }

        private MergedBatchPutCommand _batchPutCommand;
        private MergedBatchPutCommand _prevCommand;
        private Task _prevCommandTask;

        public async Task<ImportResult> Import(DocumentsOperationContext context, Stream stream)
        {
            var result = new ImportResult();

            var state = new JsonParserState();

            JsonOperationContext.ManagedPinnedBuffer buffer;
            using (context.GetManagedBuffer(out buffer))
            using (var parser = new UnmanagedJsonParser(context, state, "fileName"))
            {
                var operateOnType = "__top_start_object";
                var buildVersion = 0L;
                var identities = new Dictionary<string, long>();
                VersioningStorage versioningStorage = null;

                while (true)
                {
                    if (parser.Read() == false)
                    {
                        var read = await stream.ReadAsync(buffer.Buffer.Array, buffer.Buffer.Offset, buffer.Length);
                        if (read == 0)
                        {
                            if (state.CurrentTokenType != JsonParserToken.EndObject)
                                throw new EndOfStreamException("Stream ended without reaching end of json content");
                            break;
                        }
                        parser.SetBuffer(buffer, read);
                        continue;
                    }

                    switch (state.CurrentTokenType)
                    {
                        case JsonParserToken.String:
                            unsafe
                            {
                                operateOnType =
                                    new LazyStringValue(null, state.StringBuffer, state.StringSize, context).ToString();
                            }
                            break;
                        case JsonParserToken.Integer:
                            switch (operateOnType)
                            {
                                case "BuildVersion":
                                    buildVersion = state.Long;
                                    break;
                            }
                            break;
                        case JsonParserToken.StartObject:
                            if (operateOnType == "__top_start_object")
                            {
                                operateOnType = null;
                                break;
                            }

                            var builder = new BlittableJsonDocumentBuilder(context, BlittableJsonDocumentBuilder.UsageMode.ToDisk, "ImportObject", parser, state);
                            builder.ReadNestedObject();
                            while (builder.Read() == false)
                            {
                                var read = await stream.ReadAsync(buffer.Buffer.Array, buffer.Buffer.Offset, buffer.Length);
                                if (read == 0)
                                    throw new EndOfStreamException("Stream ended without reaching end of json content");
                                parser.SetBuffer(buffer, read);
                            }
                            builder.FinalizeDocument();

                            if (operateOnType == "Docs" && Options.OperateOnTypes.HasFlag(DatabaseItemType.Documents))
                            {

                                PatchDocument patch = null;
                                PatchRequest patchRequest = null;
                                if (string.IsNullOrWhiteSpace(Options.TransformScript) == false)
                                {
                                    patch = new PatchDocument(context.DocumentDatabase);
                                    patchRequest = new PatchRequest
                                    {
                                        Script = Options.TransformScript
                                    };
                                }

                                result.DocumentsCount++;
                                using (var reader = builder.CreateReader())
                                {
                                    var document = new Document
                                    {
                                        Data = reader,
                                    };

                                    if (!Options.IncludeExpired && document.Expired())
                                        continue;

                                    TransformScriptOrDisableVersioningIfNeeded(context, patch, reader, document, patchRequest);

                                    _batchPutCommand.Add(document.Data);
                                }
                                
                                await HandleBatchOfDocuments(context, parser, buildVersion);
                            }
                            else if (operateOnType == "RevisionDocuments" &&
                                     Options.OperateOnTypes.HasFlag(DatabaseItemType.RevisionDocuments))
                            {
                                if (versioningStorage == null)
                                    break;

                                result.RevisionDocumentsCount++;
                                using (var reader = builder.CreateReader())
                                    _batchPutCommand.Add(reader);
                                await HandleBatchOfDocuments(context, parser, buildVersion);
                            }
                            else
                            {
                                using (builder)
                                {
                                    switch (operateOnType)
                                    {
                                        case "Attachments":
                                            result.Warnings.Add("Attachments are not supported anymore. Use RavenFS isntead. Skipping.");
                                            break;
                                        case "Indexes":
                                            if (Options.OperateOnTypes.HasFlag(DatabaseItemType.Indexes) == false)
                                                continue;

                                            result.IndexesCount++;
                                            try
                                            {
                                                IndexProcessor.Import(builder, _database, buildVersion, Options.RemoveAnalyzers);
                                            }
                                            catch (Exception e)
                                            {
                                                result.Warnings.Add($"Could not import index. Message: {e.Message}");
                                            }

                                            break;
                                        case "Transformers":
                                            if (Options.OperateOnTypes.HasFlag(DatabaseItemType.Transformers) == false)
                                                continue;

                                            result.TransformersCount++;

                                            try
                                            {
                                                TransformerProcessor.Import(builder, _database, buildVersion);
                                            }
                                            catch (Exception e)
                                            {
                                                result.Warnings.Add($"Could not import transformer. Message: {e.Message}");
                                            }
                                            break;
                                        case "Identities":
                                            if (Options.OperateOnTypes.HasFlag(DatabaseItemType.Identities))
                                            {
                                                result.IdentitiesCount++;

                                                using (var reader = builder.CreateReader())
                                                {
                                                    try
                                                    {
                                                        string identityKey, identityValueString;
                                                        long identityValue;
                                                        if (reader.TryGet("Key", out identityKey) == false || reader.TryGet("Value", out identityValueString) == false || long.TryParse(identityValueString, out identityValue) == false)
                                                        {
                                                            result.Warnings.Add($"Cannot import the following identity: '{reader}'. Skipping.");
                                                        }
                                                        else
                                                        {
                                                            identities[identityKey] = identityValue;
                                                        }
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        result.Warnings.Add($"Cannot import the following identity: '{reader}'. Error: {e}. Skipping.");
                                                    }
                                                }
                                            }
                                            break;
                                        default:
                                            result.Warnings.Add(
                                                $"The following type is not recognized: '{operateOnType}'. Skipping.");
                                            break;
                                    }
                                }
                            }
                            break;
                        case JsonParserToken.StartArray:
                            switch (operateOnType)
                            {
                                case "RevisionDocuments":
                                    // We are taking a reference here since the documents import can activate or disable the versioning.
                                    // We hold a local copy because the user can disable the bundle during the import process, exteranly.
                                    // In this case we want to continue to import the revisions documents.
                                    versioningStorage = _database.BundleLoader.VersioningStorage;
                                    _batchPutCommand.IsRevision = true;
                                    break;
                            }
                            break;
                        case JsonParserToken.EndArray:
                            switch (operateOnType)
                            {
                                case "Docs":
                                    await FinishBatchOfDocuments();
                                    _batchPutCommand = new MergedBatchPutCommand(_database, buildVersion);
                                    break;
                                case "RevisionDocuments":
                                    await FinishBatchOfDocuments();
                                    break;
                                case "Identities":
                                    if (identities.Count > 0)
                                    {
                                        using (var tx = context.OpenWriteTransaction())
                                        {
                                            _database.DocumentsStorage.UpdateIdentities(context, identities);
                                            tx.Commit();
                                        }
                                    }
                                    identities = null;
                                    break;
                            }
                            break;
                    }
                }
            }

            return result;
        }

        private void TransformScriptOrDisableVersioningIfNeeded(DocumentsOperationContext context, 
            PatchDocument patch, BlittableJsonReaderObject reader, Document document, PatchRequest patchRequest)
        {
            if (patch == null && Options.DisableVersioningBundle == false)
                return;

            BlittableJsonReaderObject newMetadata;
            reader.TryGet(Constants.Metadata.Key, out newMetadata);

            if (patch != null)
            {
                LazyStringValue key;
                if (newMetadata != null)
                    if (newMetadata.TryGet(Constants.Metadata.Id, out key))
                        document.Key = key;

                var patchResult = patch.Apply(context, document, patchRequest);
                if (patchResult != null && patchResult.ModifiedDocument.Equals(document.Data) == false)
                {
                    document.Data = patchResult.ModifiedDocument;
                }
            }

            if (Options.DisableVersioningBundle == false || newMetadata == null)
                return;

            newMetadata.Modifications = new DynamicJsonValue(newMetadata)
            {
                [Constants.Versioning.RavenDisableVersioning] = false
            };
        }

        private async Task FinishBatchOfDocuments()
        {
            if (_prevCommand != null)
            {
                using (_prevCommand)
                {
                    await _prevCommandTask;
                }
                _prevCommand = null;
            }

            if (_batchPutCommand.Documents.Count > 0)
            {
                using (_batchPutCommand)
                {
                    await _database.TxMerger.Enqueue(_batchPutCommand);
                }
            }
            _batchPutCommand = null;
        }

        private async Task HandleBatchOfDocuments(DocumentsOperationContext context, UnmanagedJsonParser parser, long buildVersion)
        {
            if (_batchPutCommand.TotalSize >= 16 * Voron.Global.Constants.Size.Megabyte)
            {
                if (_prevCommand != null)
                {
                    using (_prevCommand)
                    {
                        await _prevCommandTask;
                        ResetContextAndParser(context, parser);
                    }
                }
                _prevCommandTask = _database.TxMerger.Enqueue(_batchPutCommand);
                _prevCommand = _batchPutCommand;
                _batchPutCommand = new MergedBatchPutCommand(_database, buildVersion);
            }
        }

        private static void ResetContextAndParser(DocumentsOperationContext context, UnmanagedJsonParser parser)
        {
            parser.ResetStream();
            context.ResetAndRenew();
            parser.SetStream();
        }

        private class MergedBatchPutCommand : TransactionOperationsMerger.MergedTransactionCommand, IDisposable
        {
            public bool IsRevision;

            private readonly DocumentDatabase _database;
            private readonly long _buildVersion;

            public long TotalSize;
            public readonly List<BlittableJsonReaderObject> Documents = new List<BlittableJsonReaderObject>();
            private readonly IDisposable _resetContext;
            private readonly DocumentsOperationContext _context;

            public MergedBatchPutCommand(DocumentDatabase database, long buildVersion)
            {
                _database = database;
                _buildVersion = buildVersion;
                _resetContext = _database.DocumentsStorage.ContextPool.AllocateOperationContext(out _context);
            }

            public override void Execute(DocumentsOperationContext context, RavenTransaction tx)
            {
                foreach (var document in Documents)
                {
                    BlittableJsonReaderObject metadata;
                    if (document.TryGet(Constants.Metadata.Key, out metadata) == false)
                        throw new InvalidOperationException("A document must have a metadata");
                    // We are using the id term here and not key in order to be backward compatiable with old export files.
                    string key;
                    if (metadata.TryGet(Constants.Metadata.Id, out key) == false)
                        throw new InvalidOperationException("Document's metadata must include the document's key.");

                    DynamicJsonValue mutatedMetadata;
                    metadata.Modifications = mutatedMetadata = new DynamicJsonValue(metadata);
                    mutatedMetadata.Remove(Constants.Metadata.Id);
                    mutatedMetadata.Remove(Constants.Metadata.Etag);

                    if (IsRevision)
                    {
                        long etag;
                        if (metadata.TryGet(Constants.Metadata.Etag, out etag) == false)
                            throw new InvalidOperationException("Document's metadata must include the document's key.");

                        _database.BundleLoader.VersioningStorage.PutDirect(context, key, etag, document);
                    }
                    else if (_buildVersion < 4000 && key.Contains("/revisions/"))
                    {
                        long etag;
                        if (metadata.TryGet(Constants.Metadata.Etag, out etag) == false)
                            throw new InvalidOperationException("Document's metadata must include the document's key.");

                        var endIndex = key.IndexOf("/revisions/", StringComparison.OrdinalIgnoreCase);
                        key = key.Substring(0, endIndex);

                        _database.BundleLoader.VersioningStorage.PutDirect(context, key, etag, document);
                    }
                    else
                    {
                        _database.DocumentsStorage.Put(context, key, null, document);
                    }
                }
            }

            public void Dispose()
            {
                _resetContext.Dispose();
            }

            public unsafe void Add(BlittableJsonReaderObject doc)
            {
                var mem = _context.GetMemory(doc.Size);
                Memory.Copy((byte*)mem.Address, doc.BasePointer, doc.Size);
                Documents.Add(new BlittableJsonReaderObject((byte*)mem.Address, doc.Size, _context));
                TotalSize += doc.Size;
            }
        }
    }
}