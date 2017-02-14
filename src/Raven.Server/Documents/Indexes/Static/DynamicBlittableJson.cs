using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Raven.Client;
using Raven.Server.Utils;
using Sparrow.Json;

namespace Raven.Server.Documents.Indexes.Static
{
    public class DynamicBlittableJson : DynamicObject, IEnumerable<object>, IBlittableJsonContainer
    {
        private Document _doc;
        public BlittableJsonReaderObject BlittableJson { get; private set; }

        public void EnsureMetadata()
        {
            _doc?.EnsureMetadata();
        }

        public DynamicBlittableJson(Document document)
        {
            Set(document);
        }

        public DynamicBlittableJson(BlittableJsonReaderObject blittableJson)
        {
            BlittableJson = blittableJson;
        }

        public void Set(Document document)
        {
            _doc = document;
            BlittableJson = document.Data;
        }

        public bool ContainsKey(string key)
        {
            return BlittableJson.GetPropertyNames().Contains(key);
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var name = binder.Name;
            return TryGetByName(name, out result);
        }

        public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
        {
            return TryGetByName((string)indexes[0], out result);
        }

        private bool TryGetByName(string name, out object result)
        {
            // Using ordinal ignore case versions to avoid the cast of calling String.Equals with non interned values.
            if (string.Compare(name, Constants.Indexing.Fields.DocumentIdFieldName, StringComparison.Ordinal) == 0 || 
                string.Compare(name, "Id", StringComparison.Ordinal) == 0 )
            {
                if (BlittableJson.TryGetMember(name, out result))
                    return true;

                if (_doc == null)
                {
                    result = DynamicNullObject.Null;
                    return true;
                }

                result = _doc.Key;
                return true;
            }

            var getResult = BlittableJson.TryGetMember(name, out result);

            if (getResult == false && _doc != null)
            {
                switch (name)
                {
                    case Constants.Metadata.Id:
                        result = _doc.Key;
                        getResult = true;
                        break;
                    case Constants.Metadata.Etag:
                        result = _doc.Etag;
                        getResult = true;
                        break;
                    case Constants.Metadata.LastModified:
                        result = _doc.LastModified;
                        getResult = true;
                        break;
                }
            }

            if (result == null && string.Compare(name, "HasValue", StringComparison.Ordinal) == 0 )
            {
                result = getResult;
                return true;
            }

            if (getResult && result == null)
            {
                result = DynamicNullObject.ExplicitNull;
                return true;
            }

            if (getResult == false)
            {
                result = DynamicNullObject.Null;
                return true;
            }

            result = TypeConverter.ToDynamicType(result);

            if (string.Compare(name,Constants.Metadata.Key, StringComparison.Ordinal) == 0)
            {
                ((DynamicBlittableJson) result)._doc = _doc;
            }

            return true;
        }

        public object this[string key]
        {
            get
            {
                object result;
                if (TryGetByName(key, out result) == false)
                    throw new InvalidOperationException($"Could not get '{key}' value of dynamic object");

                return result;
            }
        }

        public T Value<T>(string key)
        {
            return TypeConverter.Convert<T>(this[key], false);
        }

        public IEnumerator<object> GetEnumerator()
        {
            foreach (var propertyName in BlittableJson.GetPropertyNames())
            {
                yield return new KeyValuePair<object, object>(propertyName, TypeConverter.ToDynamicType(BlittableJson[propertyName]));
            }
        }

        public IEnumerable<object> Select(Func<object, object> func)
        {
            return new DynamicArray(Enumerable.Select(this, func));
        }

        public IEnumerable<object> OrderBy(Func<object, object> func)
        {
            return new DynamicArray(Enumerable.OrderBy(this, func));
        }

        public override string ToString()
        {
            return BlittableJson.ToString();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;

            return Equals((DynamicBlittableJson)obj);
        }

        protected bool Equals(DynamicBlittableJson other)
        {
            return Equals(BlittableJson, other.BlittableJson);
        }

        public override int GetHashCode()
        {
            return BlittableJson?.GetHashCode() ?? 0;
        }
    }
}