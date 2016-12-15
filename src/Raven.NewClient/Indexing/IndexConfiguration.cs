using System;
using System.Collections.Generic;
using Raven.NewClient.Abstractions.Data;
using Raven.NewClient.Abstractions.Extensions;
using Sparrow.Json;

namespace Raven.NewClient.Client.Indexing
{
    public class IndexConfiguration : Dictionary<string, string>, IFillFromBlittableJson
    {
        /// <summary>
        /// Index specific setting that limits the number of map outputs that an index is allowed to create for a one source document. If a map operation applied to
        /// the one document produces more outputs than this number then an index definition will be considered as a suspicious, the indexing of this document 
        /// will be skipped and the appropriate error message will be added to the indexing errors.
        /// <para>Default value: null means that the global value from Raven configuration will be taken to detect if number of outputs was exceeded.</para>
        /// </summary>
        [JsonIgnore]
        public int? MaxIndexOutputsPerDocument
        {
            get
            {
                var value = GetValue(Constants.Configuration.MaxIndexOutputsPerDocument);
                if (value == null)
                    return null;

                int valueAsInt;
                if (int.TryParse(value, out valueAsInt) == false)
                    return null;

                return valueAsInt;
            }

            set
            {
                Add(Constants.Configuration.MaxIndexOutputsPerDocument, value?.ToInvariantString());
            }
        }

        public new void Add(string key, string value)
        {
            if (string.Equals(key, Constants.Configuration.MaxMapReduceIndexOutputsPerDocument, StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, Constants.Configuration.MaxMapReduceIndexOutputsPerDocument, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cannot set '{key}' key. Use '{Constants.Configuration.MaxIndexOutputsPerDocument}' instead.");

            base[key] = value;
        }

        public new string this[string key]
        {
            get { return base[key]; }
            set { Add(key, value); }
        }

        public string GetValue(string key)
        {
            string value;
            if (TryGetValue(key, out value) == false)
                return null;

            return value;
        }

        protected bool Equals(IndexConfiguration other)
        {
            return DictionaryExtensions.ContentEquals(this, other);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((IndexConfiguration)obj);
        }

        public override int GetHashCode()
        {
            return Count;
        }

        public void FillFromBlittableJson(BlittableJsonReaderObject json)
        {
            if (json == null)
                return;

            foreach (var propertyName in json.GetPropertyNames())
                this[propertyName] = json[propertyName].ToString();
        }
    }
}