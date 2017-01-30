﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Raven.Abstractions.Data;
using Raven.Server.Documents.Indexes.Static;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Voron;

namespace Raven.Server.Documents
{
    public class CollectionNameComparer : IEqualityComparer<CollectionName>
    {
        public static readonly CollectionNameComparer Instance = new CollectionNameComparer();


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CollectionName x, CollectionName y)
        {
            if (x == y) return true;
            if (x == null || y == null) return false;
            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(CollectionName obj)
        {
            return obj.Name != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name) : 0;
        }
    }

    public class CollectionName
    {
        public const string EmptyCollection = "@empty";
        public const string SystemCollection = "@system";

        private readonly string _documents;
        private readonly string _tombstones;

        public readonly string Name;
        public readonly bool IsSystem;

        public CollectionName(string name)
        {
            Name = name;
            IsSystem = IsSystemCollection(name);

            _documents = GetName(CollectionTableType.Documents);
            _tombstones = GetName(CollectionTableType.Tombstones);
        }

        public string GetTableName(CollectionTableType type)
        {
            switch (type)
            {
                case CollectionTableType.Documents:
                    return _documents;
                case CollectionTableType.Tombstones:
                    return _tombstones;
                default:
                    throw new NotSupportedException($"Collection table type '{type}' is not supported.");
            }
        }

        public override string ToString()
        {
            return $"Collection: '{Name}'";
        }

        protected bool Equals(CollectionName other)
        {
            return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((CollectionName)obj);
        }

        public override int GetHashCode()
        {
            return Name != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Name) : 0;
        }

        public static bool operator ==(CollectionName left, CollectionName right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(CollectionName left, CollectionName right)
        {
            return Equals(left, right) == false;
        }

        public static bool IsSystemCollection(string collection)
        {
            return string.Equals(collection, SystemCollection, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetCollectionName(Slice key, BlittableJsonReaderObject document)
        {
            if (key.Size >= 6)
            {
                if ((key[0] == (byte)'R' || key[0] == (byte)'r') &&
                    (key[1] == (byte)'A' || key[1] == (byte)'a') &&
                    (key[2] == (byte)'V' || key[2] == (byte)'v') &&
                    (key[3] == (byte)'E' || key[3] == (byte)'e') &&
                    (key[4] == (byte)'N' || key[4] == (byte)'n') &&
                    (key[5] == (byte)'/'))
                {
                    return SystemCollection;
                }
            }

            return GetCollectionName(document);
        }

        public static string GetCollectionName(string key, BlittableJsonReaderObject document)
        {
            if (key != null && key.StartsWith("Raven/", StringComparison.OrdinalIgnoreCase))
                return SystemCollection;

            return GetCollectionName(document);
        }

        public static string GetCollectionName(DynamicBlittableJson document)
        {
            dynamic dynamicDocument = document;
            string key = dynamicDocument.Id;

            if (key != null && key.StartsWith("Raven/", StringComparison.OrdinalIgnoreCase))
                return SystemCollection;

            return GetCollectionName(document.BlittableJson);
        }

        public static LazyStringValue GetLazyCollectionNameFrom(JsonOperationContext context,BlittableJsonReaderObject document)
        {
            BlittableJsonReaderObject metadata;
            LazyStringValue collectionName;
            if (document.TryGet(Constants.Metadata.Key, out metadata) == false ||
                metadata.TryGet(Constants.Metadata.Collection, out collectionName) == false)
            {
                return context.GetLazyStringForFieldWithCaching(EmptyCollection);
            }
            return collectionName;
        }

        public static string GetCollectionName(BlittableJsonReaderObject document)
        {
            string collectionName;
            BlittableJsonReaderObject metadata;
           
            if (document == null || 
                document.TryGet(Constants.Metadata.Key, out metadata) == false ||
                metadata.TryGet(Constants.Metadata.Collection, out collectionName) == false)
            {
                collectionName = EmptyCollection;
            }

            if (document?.Modifications?[Constants.Metadata.Key] == null)
                return collectionName;

            var collection = (DynamicJsonValue)document.Modifications[Constants.Metadata.Key];
            return (string)collection[Constants.Metadata.Collection];
        }

        public static string GetTablePrefix(CollectionTableType type)
        {
            return $"Collection.{type}.";
        }

        private string GetName(CollectionTableType type)
        {
            return $"{GetTablePrefix(type)}{Name.ToLowerInvariant()}";
        }
    }

    public enum CollectionTableType
    {
        Documents,
        Tombstones
    }
}