﻿using System;
using System.Collections.Generic;

namespace Raven.Server.Documents.Indexes.Persistence.Lucene.Documents
{
    public class DictionaryAccessor : IPropertyAccessor
    {
        private readonly Dictionary<string, DictionaryValueAccessor> _properties = new Dictionary<string, DictionaryValueAccessor>();

        private readonly List<KeyValuePair<string, DictionaryValueAccessor>> _propertiesInOrder =
            new List<KeyValuePair<string, DictionaryValueAccessor>>();

        private DictionaryAccessor(Dictionary<string, object> instance, List<IndexFieldBase> orderedMapFields = null, Dictionary<string, CompiledIndexField> groupByFields = null)
        {
            if (instance == null)
                throw new NotSupportedException("Indexed dictionary must be of type: Dictionary<string, object>");

            foreach (var key in instance.Keys)
            {
                var getMethod = new DictionaryValueAccessor(key);

                if (groupByFields != null)
                {
                    foreach (var groupByField in groupByFields.Values)
                    {
                        if (groupByField.IsMatch(key))
                        {
                            getMethod.GroupByField = groupByField;
                            getMethod.IsGroupByField = true;
                            break;
                        }
                    }
                }

                _properties.Add(key, getMethod);

                if (orderedMapFields == null)
                    _propertiesInOrder.Add(new KeyValuePair<string, DictionaryValueAccessor>(key, getMethod));
            }

            if (orderedMapFields != null)
            {
                foreach (var field in orderedMapFields)
                {
                    _propertiesInOrder.Add(new KeyValuePair<string, DictionaryValueAccessor>(field.Name, _properties[field.Name]));
                }
            }
        }

        internal static DictionaryAccessor Create(Dictionary<string, object> instance, List<IndexFieldBase> orderedMapFields = null, Dictionary<string, CompiledIndexField> groupByFields = null)
        {
            return new DictionaryAccessor(instance, orderedMapFields, groupByFields);
        }

        public IEnumerable<(string Key, object Value, CompiledIndexField GroupByField, bool IsGroupByField)> GetPropertiesInOrder(object target)
        {
            foreach ((var key, var value) in _propertiesInOrder)
            {
                yield return (key, value.GetValue(target), value.GroupByField, value.IsGroupByField);
            }
        }

        public object GetValue(string name, object target)
        {
            if (_properties.TryGetValue(name, out DictionaryValueAccessor accessor))
                return accessor.GetValue(target);

            throw new InvalidOperationException(string.Format("The {0} property was not found", name));
        }

        private class DictionaryValueAccessor : PropertyAccessor.Accessor
        {
            private readonly string _propertyName;

            public DictionaryValueAccessor(string propertyName)
            {
                _propertyName = propertyName;
            }

            public override object GetValue(object target)
            {
                return ((Dictionary<string, object>)target)[_propertyName];
            }
        }
    }
}
