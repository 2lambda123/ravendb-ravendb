﻿using System;
using System.Collections.Generic;
using Raven.Client;
using Raven.Client.Documents.Indexes;
using Raven.Server.Utils;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Documents.Indexes.Persistence.Lucene.Documents
{
    public class AnonymousLuceneDocumentConverter : LuceneDocumentConverterBase
    {
        private readonly bool _isMultiMap;
        private IPropertyAccessor _propertyAccessor;

        public AnonymousLuceneDocumentConverter(ICollection<IndexField> fields, bool isMultiMap, bool indexImplicitNull = false, bool indexEmptyEntries = false, bool storeValue = false, string storeValueFieldName = Constants.Documents.Indexing.Fields.ReduceKeyValueFieldName)
            : base(fields, indexImplicitNull, indexEmptyEntries, storeValue, storeValueFieldName)
        {
            _isMultiMap = isMultiMap;
        }

        protected override int GetFields<T>(T instance, LazyStringValue key, LazyStringValue sourceDocumentId, object document, JsonOperationContext indexContext)
        {
            int newFields = 0;
            if (key != null)
            {
                instance.Add(GetOrCreateKeyField(key));
                newFields++;
            }

            if (sourceDocumentId != null)
            {
                instance.Add(GetOrCreateSourceDocumentIdField(sourceDocumentId));
                newFields++;
            }

            var boostedValue = document as BoostedValue;
            var documentToProcess = boostedValue == null ? document : boostedValue.Value;

            IPropertyAccessor accessor;

            if (_isMultiMap == false)
                accessor = _propertyAccessor ?? (_propertyAccessor = PropertyAccessor.Create(documentToProcess.GetType(), documentToProcess));
            else
                accessor = TypeConverter.GetPropertyAccessor(documentToProcess);

            var storedValue = _storeValue ? new DynamicJsonValue() : null;

            foreach (var property in accessor.GetPropertiesInOrder(documentToProcess))
            {
                var value = property.Value;

                IndexField field;

                try
                {
                    field = _fields[property.Key];
                }
                catch (KeyNotFoundException e)
                {
                    throw new InvalidOperationException($"Field '{property.Key}' is not defined. Available fields: {string.Join(", ", _fields.Keys)}.", e);
                }

                var numberOfCreatedFields = GetRegularFields(instance, field, value, indexContext);

                newFields += numberOfCreatedFields;

                if (boostedValue != null)
                {
                    var fields = instance.GetFields();
                    for (int idx = fields.Count - 1; numberOfCreatedFields > 0; numberOfCreatedFields--, idx--)
                    {
                        var luceneField = fields[idx];
                        luceneField.Boost = boostedValue.Boost;
                        luceneField.OmitNorms = false;
                    }
                }

                if (storedValue != null && numberOfCreatedFields > 0)
                {
                    storedValue[property.Key] = TypeConverter.ToBlittableSupportedType(value, flattenArrays: true);
                }
            }

            if (_storeValue)
            {
                instance.Add(GetStoredValueField(Scope.CreateJson(storedValue, indexContext)));
                newFields++;
            }

            return newFields;
        }
    }
}
