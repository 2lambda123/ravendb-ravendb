﻿using System;
using System.Linq;
using Lucene.Net.Documents;
using Sparrow.Json.Parsing;
using Sparrow.Json;
using System.Collections.Generic;
using Raven.Client.Documents.Indexes;
using Raven.Server.Documents.Indexes.Persistence.Lucene.Documents;
using Raven.Server.Json;
using System.IO;
using Lucene.Net.Store;
using Raven.Client;
using Raven.Server.Documents.Includes;
using Raven.Server.Documents.Patch;
using Raven.Server.Documents.Queries.Parser;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow;

namespace Raven.Server.Documents.Queries.Results
{
    public abstract class QueryResultRetrieverBase : IQueryResultRetriever
    {
        private readonly DocumentDatabase _database;
        private readonly IndexQueryServerSide _query;
        private readonly JsonOperationContext _context;
        private readonly IncludeDocumentsCommand _includeDocumentsCommand;
        private readonly BlittableJsonTraverser _blittableTraverser;
        private Dictionary<string, Document> _loadedDocuments;
        private HashSet<string> _loadedDocumentIds;

        protected readonly DocumentsStorage DocumentsStorage;

        protected readonly FieldsToFetch FieldsToFetch;

        protected QueryResultRetrieverBase(DocumentDatabase database, IndexQueryServerSide query, FieldsToFetch fieldsToFetch, DocumentsStorage documentsStorage, JsonOperationContext context, bool reduceResults, IncludeDocumentsCommand includeDocumentsCommand)
        {
            _database = database;
            _query = query;
            _context = context;
            _includeDocumentsCommand = includeDocumentsCommand;
            DocumentsStorage = documentsStorage;

            FieldsToFetch = fieldsToFetch;
            _blittableTraverser = reduceResults ? BlittableJsonTraverser.FlatMapReduceResults : BlittableJsonTraverser.Default;
        }

        public abstract Document Get(Lucene.Net.Documents.Document input, float score, IState state);

        public abstract bool TryGetKey(Lucene.Net.Documents.Document document, IState state, out string key);

        protected abstract Document DirectGet(Lucene.Net.Documents.Document input, string id, IState state);

        protected abstract Document LoadDocument(string id);

        protected Document GetProjection(Lucene.Net.Documents.Document input, float score, string lowerId, IState state)
        {
            Document doc = null;
            if (FieldsToFetch.AnyExtractableFromIndex == false)
            {
                doc = DirectGet(input, lowerId, state);

                if (doc == null)
                    return null;

                return GetProjectionFromDocument(doc, input, score, FieldsToFetch, _context, state);
            }

            var documentLoaded = false;

            var result = new DynamicJsonValue();

            Dictionary<string, FieldsToFetch.FieldToFetch> fields = null;
            if (FieldsToFetch.ExtractAllFromIndex)
            {
                if (FieldsToFetch.ExtractAllFromIndex)
                {
                    fields = input.GetFields()
                        .Where(x => x.Name != Constants.Documents.Indexing.Fields.DocumentIdFieldName
                                    && x.Name != Constants.Documents.Indexing.Fields.ReduceKeyHashFieldName
                                    && x.Name != Constants.Documents.Indexing.Fields.ReduceKeyValueFieldName
                                    && FieldUtil.GetRangeTypeFromFieldName(x.Name) == RangeType.None)
                        .Distinct(UniqueFieldNames.Instance)
                        .ToDictionary(x => x.Name, x => new FieldsToFetch.FieldToFetch(x.Name, null, null, x.IsStored, isDocumentId: false));
                }
            }

            if (fields == null)
                fields = FieldsToFetch.Fields;
            else if (FieldsToFetch.Fields != null && FieldsToFetch.Fields.Count > 0)
            {
                foreach (var kvp in FieldsToFetch.Fields)
                {
                    if (fields.ContainsKey(kvp.Key))
                        continue;

                    fields[kvp.Key] = kvp.Value;
                }
            }

            foreach (var fieldToFetch in fields.Values)
            {
                if (TryExtractValueFromIndex(fieldToFetch, input, result, state))
                    continue;

                if (documentLoaded == false)
                {
                    doc = DirectGet(input, lowerId, state);

                    documentLoaded = true;
                }

                if (doc == null)
                    continue;

                if (TryGetValue(fieldToFetch, doc, input, state, out var fieldVal))
                {
                    if (FieldsToFetch.SingleBodyOrMethodWithNoAlias)
                    {
                        if (fieldVal is BlittableJsonReaderObject nested)
                            doc.Data = nested;
                        else
                            ThrowInvalidQueryBodyResponse(fieldVal);
                        doc.IndexScore = score;
                        return doc;
                    }
                    if (fieldVal is List<object> list)
                        fieldVal = new DynamicJsonArray(list);
                    result[fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value] = fieldVal;
                }
            }

            if (doc == null)
            {
                doc = new Document
                {
                    Id = _context.GetLazyString(lowerId)
                };
            }

            return ReturnProjection(result, doc, score, _context);
        }

        public Document GetProjectionFromDocument(Document doc, Lucene.Net.Documents.Document luceneDoc, float score, FieldsToFetch fieldsToFetch, JsonOperationContext context, IState state)
        {
            var result = new DynamicJsonValue();

            foreach (var fieldToFetch in fieldsToFetch.Fields.Values)
            {
                if (TryGetValue(fieldToFetch, doc, luceneDoc, state, out var fieldVal))
                {
                    if (fieldsToFetch.SingleBodyOrMethodWithNoAlias)
                    {
                        if (fieldVal is BlittableJsonReaderObject nested)
                            doc.Data = nested;
                        else
                            ThrowInvalidQueryBodyResponse(fieldVal);

                        doc.IndexScore = score;
                        return doc;
                    }
                    if (fieldVal is List<object> list)
                        fieldVal = new DynamicJsonArray(list);
                    var key = fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value;
                    result[key] = fieldVal;
                }
            }

            return ReturnProjection(result, doc, score, context);
        }

        private static void ThrowInvalidQueryBodyResponse(object fieldVal)
        {
            throw new InvalidOperationException("Query returning a single function call result must return an object, but got: " + (fieldVal ?? "null"));
        }

        private static Document ReturnProjection(DynamicJsonValue result, Document doc, float score, JsonOperationContext context)
        {
            var newData = context.ReadObject(result, "projection result");

            try
            {
                doc.Data?.Dispose();
            }
            catch (Exception)
            {
                newData.Dispose();
                throw;
            }

            doc.Data = newData;
            doc.IndexScore = score;

            return doc;
        }

        private bool TryExtractValueFromIndex(FieldsToFetch.FieldToFetch fieldToFetch, Lucene.Net.Documents.Document indexDocument, DynamicJsonValue toFill, IState state)
        {
            if (fieldToFetch.CanExtractFromIndex == false)
                return false;

            var name = fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value;

            DynamicJsonArray array = null;
            FieldType fieldType = null;
            var anyExtracted = false;
            foreach (var field in indexDocument.GetFields(fieldToFetch.Name))
            {
                if (fieldType == null)
                    fieldType = GetFieldType(field.Name, indexDocument);

                var fieldValue = ConvertType(_context, field, fieldType, state);

                if (fieldType.IsArray)
                {
                    if (array == null)
                    {
                        array = new DynamicJsonArray();
                        toFill[name] = array;
                    }

                    array.Add(fieldValue);
                    anyExtracted = true;
                    continue;
                }

                toFill[name] = fieldValue;
                anyExtracted = true;
            }

            return anyExtracted;
        }

        internal static FieldType GetFieldType(string field, Lucene.Net.Documents.Document indexDocument)
        {
            return new FieldType
            {
                IsArray = indexDocument.GetField(field + LuceneDocumentConverterBase.IsArrayFieldSuffix) != null,
                IsJson = indexDocument.GetField(field + LuceneDocumentConverterBase.ConvertToJsonSuffix) != null
            };
        }

        internal class FieldType
        {
            public bool IsArray;
            public bool IsJson;
        }

        private static object ConvertType(JsonOperationContext context, IFieldable field, FieldType fieldType, IState state)
        {
            if (field.IsBinary)
                ThrowBinaryValuesNotSupported();

            var stringValue = field.StringValue(state);
            if (stringValue == Constants.Documents.Indexing.Fields.NullValue || stringValue == null)
                return null;
            if (stringValue == Constants.Documents.Indexing.Fields.EmptyString || stringValue == string.Empty)
                return string.Empty;

            if (fieldType.IsJson == false)
                return stringValue;

            var bytes = Encodings.Utf8.GetBytes(stringValue);
            var ms = new MemoryStream(bytes);
            return context.ReadForMemory(ms, field.Name);
        }

        private static void ThrowBinaryValuesNotSupported()
        {
            throw new NotSupportedException("Cannot convert binary values");
        }

        private bool TryGetValue(FieldsToFetch.FieldToFetch fieldToFetch, Document document, Lucene.Net.Documents.Document luceneDoc, IState state, out object value)
        {
            if (fieldToFetch.QueryField == null)
            {
                return TryGetFieldValueFromDocument(document, fieldToFetch, out value);
            }

            if (fieldToFetch.QueryField.Function != null)
            {
                var args = new object[fieldToFetch.QueryField.FunctionArgs.Length + 1];
                for (int i = 0; i < fieldToFetch.FunctionArgs.Length; i++)
                {
                    TryGetValue(fieldToFetch.FunctionArgs[i], document, luceneDoc, state, out args[i]);
                    if (ReferenceEquals(args[i], document))
                    {
                        args[i] = Tuple.Create(document, luceneDoc, state);
                    }
                }

                args[args.Length - 1] = _query.QueryParameters;

                value = InvokeFunction(
                    fieldToFetch.QueryField.Name,
                    _query.Metadata.Query,
                    args);
                return true;
            }

            if (fieldToFetch.QueryField.ValueTokenType != null)
            {
                var val = fieldToFetch.QueryField.Value;
                if (fieldToFetch.QueryField.ValueTokenType.Value == ValueTokenType.Parameter)
                {
                    if (_query == null)
                    {
                        value = null;
                        return false; // only happens for debug endpoints and more like this
                    }
                    _query.QueryParameters.TryGet((string)val, out val);
                }
                value = val;
                return true;
            }

            if (fieldToFetch.QueryField.HasSourceAlias == false)
            {
                return TryGetFieldValueFromDocument(document, fieldToFetch, out value);
            }
            if (_loadedDocumentIds == null)
            {
                _loadedDocumentIds = new HashSet<string>();
                _loadedDocuments = new Dictionary<string, Document>();
            }
            _loadedDocumentIds.Clear();

            //_loadedDocuments.Clear(); - explicitly not clearing this, we want to cahce this for the duration of the query


            _loadedDocuments[document.Id ?? string.Empty] = document;
            if (fieldToFetch.QueryField.SourceAlias != null)
                IncludeUtil.GetDocIdFromInclude(document.Data, fieldToFetch.QueryField.SourceAlias, _loadedDocumentIds);
            else
                _loadedDocumentIds.Add(document.Id ?? string.Empty); // null source alias is the root doc

            if (_loadedDocumentIds.Count == 0)
            {
                value = null;
                return false;
            }

            var buffer = new List<object>();

            foreach (var docId in _loadedDocumentIds)
            {
                if (docId == null)
                    continue;

                if (_loadedDocuments.TryGetValue(docId, out var doc) == false)
                {
                    _loadedDocuments[docId] = doc = LoadDocument(docId);
                }
                if (doc == null)
                    continue;
                if (string.IsNullOrEmpty(fieldToFetch.Name)) // we need the whole document here
                {
                    buffer.Add(doc);
                    continue;
                }
                if (TryGetFieldValueFromDocument(doc, fieldToFetch, out var val))
                    buffer.Add(val);
            }

            if (fieldToFetch.QueryField.SourceIsArray)
            {
                value = buffer;
                return true;
            }
            if (buffer.Count > 0)
            {
                if (buffer.Count > 1)
                {
                    ThrowOnlyArrayFieldCanHaveMultipleValues(fieldToFetch);
                }
                value = buffer[0];
                return true;
            }
            value = null;
            return false;
        }

        private class QueryKey : ScriptRunnerCache.Key
        {
            private readonly Dictionary<StringSegment, string> _functions;

            private bool Equals(QueryKey other)
            {
                if (_functions.Count != other._functions.Count)
                    return false;

                foreach (var function in _functions)
                {
                    if (other._functions.TryGetValue(function.Key, out var otherVal) == false
                        || function.Value != otherVal)
                        return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                if (ReferenceEquals(this, obj))
                    return true;
                if (obj.GetType() != GetType())
                    return false;
                return Equals((QueryKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 0;
                    foreach (var function in _functions)
                    {
                        hashCode = (hashCode * 397) ^ (function.Value.GetHashCode());
                    }
                    return hashCode;
                }
            }

            public QueryKey(Dictionary<StringSegment, string> functions)
            {
                _functions = functions;
            }

            public override void GenerateScript(ScriptRunner runner)
            {
                foreach (var kvp in _functions)
                {
                    runner.AddScript(kvp.Value);
                }
            }
        }

        private object InvokeFunction(string methodName, Query query, object[] args)
        {
            var key = new QueryKey(query.DeclaredFunctions);
            using (_database.Scripts.GetScriptRunner(key, readOnly: true, patchRun: out var run))
            using (var result = run.Run(_context as DocumentsOperationContext, methodName, args))
            {
                _includeDocumentsCommand?.AddRange(run.Includes);

                if (result.IsNull)
                    return null;

                return run.Translate(result, _context);
            }
        }

        private bool TryGetFieldValueFromDocument(Document document, FieldsToFetch.FieldToFetch field, out object value)
        {
            if (field.IsDocumentId)
            {
                value = document.Id;
            }
            else if (field.IsCompositeField == false)
            {
                if (BlittableJsonTraverserHelper.TryRead(_blittableTraverser, document, field.Name, out value) == false)
                    return false;
            }
            else
            {
                var component = new DynamicJsonValue();

                foreach (var componentField in field.Components)
                {
                    if (BlittableJsonTraverserHelper.TryRead(_blittableTraverser, document, componentField, out var componentValue))
                        component[componentField] = componentValue;
                }

                value = component;
            }
            return true;
        }

        private static void ThrowOnlyArrayFieldCanHaveMultipleValues(FieldsToFetch.FieldToFetch fieldToFetch)
        {
            throw new NotSupportedException(
                $"Attempted to read multiple values in field {fieldToFetch.ProjectedName ?? fieldToFetch.Name.Value}, but it isn't an array and should have only a single value, did you forget '[]' ?");
        }

        private class UniqueFieldNames : IEqualityComparer<IFieldable>
        {
            public static readonly UniqueFieldNames Instance = new UniqueFieldNames();

            public bool Equals(IFieldable x, IFieldable y)
            {
                return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(IFieldable obj)
            {
                return obj.Name.GetHashCode();
            }
        }
    }
}
