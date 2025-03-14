﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Raven.Client;
using Raven.Server.Documents.Indexes.Static;
using Sparrow;
using Sparrow.Json;

namespace Raven.Server.Documents
{
    public sealed class CollectionNameComparer : IEqualityComparer<CollectionName>
    {
        public static readonly CollectionNameComparer Instance = new CollectionNameComparer();


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(CollectionName x, CollectionName y)
        {
            if (x == y)
                return true;
            if (x == null || y == null)
                return false;
            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(CollectionName obj)
        {
            return obj.Name != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name) : 0;
        }
    }

    public sealed class CollectionName
    {
        public const string HiLoCollection = "@hilo";

        public static readonly StringSegment EmptyCollectionSegment;
        public static readonly StringSegment MetadataKeySegment;
        public static readonly StringSegment MetadataCollectionSegment;

        private readonly string _documents;
        private readonly string _tombstones;

        public readonly string Name;
        private readonly string _revisions;
        private readonly string _counters;
        private readonly string _counterGroups;
        private readonly string _counterTombstones;
        private readonly string _timeseries;
        private readonly string _deletedRanges;
        private readonly string _timeSeriesStats;


        private bool? _isHiLo;

        static CollectionName()
        {
            EmptyCollectionSegment = new StringSegment(Constants.Documents.Collections.EmptyCollection);
            MetadataKeySegment = new StringSegment(Constants.Documents.Metadata.Key);
            MetadataCollectionSegment = new StringSegment(Constants.Documents.Metadata.Collection);
        }

        public CollectionName(string name)
        {
            Name = name;

            _documents = GetName(CollectionTableType.Documents);
            _tombstones = GetName(CollectionTableType.Tombstones);
            _revisions = GetName(CollectionTableType.Revisions);
#pragma warning disable 618
            _counters = GetName(CollectionTableType.Counters);
#pragma warning restore 618
            _counterGroups = GetName(CollectionTableType.CounterGroups);
            _counterTombstones = GetName(CollectionTableType.CounterTombstones);
            _timeseries = GetName(CollectionTableType.TimeSeries);
            _deletedRanges = GetName(CollectionTableType.TimeSeriesDeletedRanges);
            _timeSeriesStats = GetName(CollectionTableType.TimeSeriesStats);

        }

        public bool IsHiLo => (bool)(_isHiLo ?? (_isHiLo = IsHiLoCollection(Name)));

        public string GetTableName(CollectionTableType type)
        {
            switch (type)
            {
                case CollectionTableType.Documents:
                    return _documents;
                case CollectionTableType.Tombstones:
                    return _tombstones;
                case CollectionTableType.Revisions:
                    return _revisions;
#pragma warning disable 618
                case CollectionTableType.Counters:
#pragma warning restore 618
                    return _counters;
                case CollectionTableType.CounterGroups:
                    return _counterGroups;
                case CollectionTableType.CounterTombstones:
                    return _counterTombstones;
                case CollectionTableType.TimeSeries:
                    return _timeseries;
                case CollectionTableType.TimeSeriesDeletedRanges:
                    return _deletedRanges;
                case CollectionTableType.TimeSeriesStats:
                    return _timeSeriesStats;
                default:
                    throw new NotSupportedException($"Collection table type '{type}' is not supported.");
            }
        }

        public override string ToString()
        {
            return $"Collection: '{Name}'";
        }

        private bool Equals(CollectionName other)
        {
            return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (obj.GetType() != GetType())
                return false;
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

        public static bool IsHiLoCollection(string name)
        {
            return string.Equals(name, HiLoCollection, StringComparison.OrdinalIgnoreCase);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool IsHiLoCollection(LazyStringValue name)
        {
            if (name == null)
                return false;
            return IsHiLoCollection(name.Buffer, name.Length);
        }

        public static unsafe bool IsHiLoCollection(byte* buffer, int length)
        {
            if (length != 5)
                return false;

            // case insensitive '@hilo' match without doing allocations

            if (buffer[0] != (byte)'@' ||
                buffer[1] != (byte)'h' && buffer[1] != (byte)'H' ||
                buffer[2] != (byte)'i' && buffer[2] != (byte)'I' ||
                buffer[3] != (byte)'l' && buffer[3] != (byte)'L' ||
                buffer[4] != (byte)'o' && buffer[4] != (byte)'O')
            {
                return false;
            }

            return true;
        }

        public static string GetCollectionName(DynamicBlittableJson document)
        {
            return GetCollectionName(document.BlittableJson);
        }

        public static string GetCollectionName(BlittableJsonReaderObject document)
        {
            if (document == null)
                return Constants.Documents.Collections.EmptyCollection;

            document.NoCache = true;
            if (document.TryGet(MetadataKeySegment, out BlittableJsonReaderObject metadata) == false ||
                metadata.TryGet(MetadataCollectionSegment, out string collectionName) == false ||
                collectionName == null)
            {
                collectionName = Constants.Documents.Collections.EmptyCollection;
            }

            return collectionName;
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
        Tombstones,
        Revisions,
        [Obsolete("For migration purposes only from versions where Counters were experimental feature (prior to 4.2)")]
        Counters,
        CounterGroups,
        CounterTombstones,
        TimeSeries,
        TimeSeriesDeletedRanges,
        TimeSeriesStats
    }
}
