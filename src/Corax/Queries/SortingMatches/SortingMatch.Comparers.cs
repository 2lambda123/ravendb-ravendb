﻿using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Corax.Utils;
using Corax.Utils.Spatial;
using Sparrow;
using Sparrow.Server;
using Sparrow.Server.Utils.VxSort;
using Voron;
using Voron.Data.Containers;
using Voron.Data.Lookups;
using Voron.Impl;

namespace Corax.Queries.SortingMatches;

 unsafe partial struct SortingMatch<TInner> 
 {
     private interface IEntryComparer
     {
         void Init(ref SortingMatch<TInner> match);

         Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
             UnmanagedSpan* batchTerms,
             bool descending = false);
     }
     
     
    private struct Descending<TInnerCmp> : IEntryComparer, IComparer<UnmanagedSpan> 
        where TInnerCmp : struct,  IEntryComparer, IComparer<UnmanagedSpan>
    {
        private TInnerCmp cmp;

        public Descending(TInnerCmp cmp)
        {
            this.cmp = cmp;
        }

        public Descending()
        {
            cmp = new();
        }

        public void Init(ref SortingMatch<TInner> match)
        {
            cmp.Init(ref match);
        }

        public Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
            UnmanagedSpan* batchTerms,
            bool descending = false)
        {
            return cmp.SortBatch(match: ref match, llt: llt, pageLocator: pageLocator, batchResults: batchResults, batchTermIds: batchTermIds, batchTerms: batchTerms, descending: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(UnmanagedSpan x, UnmanagedSpan y)
        {
            return cmp.Compare(y, x); // note the revered args
        }
    }

    private struct EntryComparerByScore : IEntryComparer, IComparer<UnmanagedSpan>
    {
        public void Init(ref SortingMatch<TInner> match)
        {
        }

        public Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
            UnmanagedSpan* batchTerms,
            bool descending = false)
        {
            var readScores = MemoryMarshal.Cast<long, float>(batchTermIds)[..batchResults.Length];

            // We have to initialize the score buffer with a positive number to ensure that multiplication (document-boosting) is taken into account when BM25 relevance returns 0 (for example, with AllEntriesMatch).
            readScores.Fill(Bm25Relevance.InitialScoreValue);

            // We perform the scoring process. 
            match._inner.Score(batchResults, readScores, 1f);

            // If we need to do documents boosting then we need to modify the based on documents stored score. 
            if (match._searcher.DocumentsAreBoosted)
            {
                // We get the boosting tree and go to check every document. 
                BoostDocuments(match, batchResults, readScores);
            }
                
            // Note! readScores & indexes are aliased and same as batchTermIds
            var indexes = MemoryMarshal.Cast<long, int>(batchTermIds)[..(batchTermIds.Length)];
            for (int i = 0; i < batchTermIds.Length; i++)
            {
                batchTerms[i] = new UnmanagedSpan(readScores[i]);
                indexes[i] = i;
            }

            EntryComparerHelper.IndirectSort<EntryComparerByScore>(indexes, batchTerms, descending);
                
            return indexes;
        }

        private static void BoostDocuments(SortingMatch<TInner> match, Span<long> batchResults, Span<float> readScores)
        {
            var tree = match._searcher.GetDocumentBoostTree();
            if (tree is { NumberOfEntries: > 0 })
            {
                // We are going to read from the boosting tree all the boosting values and apply that to the scores array.
                ref var scoresRef = ref MemoryMarshal.GetReference(readScores);
                ref var matchesRef = ref MemoryMarshal.GetReference(batchResults);
                for (int idx = 0; idx < batchResults.Length; idx++)
                {
                    var ptr = (float*)tree.ReadPtr(Unsafe.Add(ref matchesRef, idx), out var _);
                    if (ptr == null)
                        continue;

                    ref var scoresIdx = ref Unsafe.Add(ref scoresRef, idx);
                    scoresIdx *= *ptr;
                }
            }
        }

        public int Compare(UnmanagedSpan x, UnmanagedSpan y)
        {
            // Note, for scores, we go *descending* by default!
            return y.Double.CompareTo(x.Double);
        }
    }

    private struct CompactKeyComparer : IComparer<UnmanagedSpan>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(UnmanagedSpan xItem, UnmanagedSpan yItem)
        {
            if (yItem.Address == null)
            {
                return xItem.Address == null ? 0 : 1;
            }

            if (xItem.Address == null)
                return -1;
            var match = AdvMemory.Compare(xItem.Address + 1, yItem.Address + 1, Math.Min(xItem.Length - 1, yItem.Length - 1));
            if (match != 0)
                return match;

            var xItemLengthInBits = (xItem.Length - 1) * 8 - (xItem.Address[0] >> 4);
            var yItemLengthInBits = (yItem.Length - 1) * 8 - (yItem.Address[0] >> 4);
            return xItemLengthInBits - yItemLengthInBits;
        }
    }

    private struct EntryComparerByTerm : IEntryComparer, IComparer<UnmanagedSpan>
    {
        private CompactKeyComparer _cmpTerm;
        private Lookup<long> _lookup;

        public void Init(ref SortingMatch<TInner> match)
        {
            _lookup = match._searcher.TermsIdReaderFor(match._orderMetadata.Field.FieldName);
        }

        public Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
            UnmanagedSpan* batchTerms,
            bool descending)
        {
            _lookup.GetFor(batchResults, batchTermIds, long.MinValue);
            Container.GetAll(llt, batchTermIds, batchTerms, long.MinValue, pageLocator);
            var indirectComparer = new IndirectComparer<CompactKeyComparer>(batchTerms, new CompactKeyComparer());
            return SortByTerms(batchTermIds, batchTerms, descending, indirectComparer);
        }

        private static void MaybeBreakTies<TComparer>(Span<long> buffer, TComparer tieBreaker) where TComparer : struct, IComparer<long>
        {
            // We may have ties, have to resolve that before we can continue
            for (int i = 1; i < buffer.Length; i++)
            {
                var x = buffer[i - 1] >> 15;
                var y = buffer[i] >> 15;
                if (x != y)
                    continue;

                // we have a match on the prefix, need to figure out where it ends hopefully this is rare
                int end = i;
                for (; end < buffer.Length; end++)
                {
                    if (x != (buffer[end] >> 15))
                        break;
                }

                buffer[(i - 1)..end].Sort(tieBreaker);
                i = end;
            }
        }

        private static long CopyTermPrefix(UnmanagedSpan item)
        {
            long l = 0;
            Memory.Copy(&l, item.Address + 1 /* skip metadata byte */, Math.Min(6, item.Length - 1));
            l = BinaryPrimitives.ReverseEndianness(l) >>> 1;
            return l;
        }

        private static Span<int> SortByTerms<TComparer>(Span<long> buffer, UnmanagedSpan* batchTerms, bool isDescending, TComparer tieBreaker)
            where TComparer : struct, IComparer<long>
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                long sortKey = CopyTermPrefix(batchTerms[i]) | (uint)i;
                if (isDescending)
                    sortKey = -sortKey;
                buffer[i] = sortKey;
            }


            Sort.Run(buffer);

            MaybeBreakTies(buffer, tieBreaker);

            return ExtractIndexes(buffer, isDescending);
        }

        private static Span<int> ExtractIndexes(Span<long> buffer, bool isDescending)
        {
            // note - we reuse the memory
            var indexes = MemoryMarshal.Cast<long, int>(buffer)[..(buffer.Length)];
            for (int i = 0; i < buffer.Length; i++)
            {
                var sortKey = buffer[i];
                if (isDescending)
                    sortKey = -sortKey;
                var idx = (ushort)sortKey & 0x7FFF;
                indexes[i] = idx;
            }

            return indexes;
        }

        public int Compare(UnmanagedSpan x, UnmanagedSpan y)
        {
            return _cmpTerm.Compare(x, y);
        }
    }
    
    

    private struct EntryComparerHelper
    {
        public static Span<int> NumericSortBatch<TCmp>(Span<long> batchTermIds, UnmanagedSpan* batchTerms, bool descending = false) 
            where TCmp : struct, IComparer<UnmanagedSpan>, IEntryComparer
        {
            var indexes = MemoryMarshal.Cast<long, int>(batchTermIds)[..(batchTermIds.Length)];
            for (int i = 0; i < batchTermIds.Length; i++)
            {
                batchTerms[i] = new UnmanagedSpan(batchTermIds[i]);
                indexes[i] = i;
            }

            IndirectSort<TCmp>(indexes, batchTerms, descending);
                
            return indexes;
        }

        public static void IndirectSort<TCmp>(Span<int> indexes, UnmanagedSpan* batchTerms, bool descending, TCmp cmp = default) 
            where TCmp : struct, IComparer<UnmanagedSpan>, IEntryComparer
        {
            if (descending)
            {
                indexes.Sort(new IndirectComparer<Descending<TCmp>>(batchTerms, new (cmp)));
            }
            else
            {
                indexes.Sort(new IndirectComparer<TCmp>(batchTerms, cmp));
            }
        }
    }

    private struct EntryComparerByLong : IEntryComparer, IComparer<UnmanagedSpan>
    {
        private Lookup<long> _lookup;

        public void Init(ref SortingMatch<TInner> match)
        {
            _lookup = match._searcher.LongReader(match._orderMetadata.Field.FieldName);
        }

        public Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
            UnmanagedSpan* batchTerms,
            bool descending = false)
        {
            _lookup.GetFor(batchResults, batchTermIds, long.MinValue);
            return EntryComparerHelper.NumericSortBatch<EntryComparerByLong>(batchTermIds, batchTerms, descending);
        }

        public int Compare(UnmanagedSpan x, UnmanagedSpan y)
        {
            return x.Long.CompareTo(y.Long);
        }
    }
        
    private struct EntryComparerByDouble : IEntryComparer, IComparer<UnmanagedSpan>
    {
        private Lookup<long> _lookup;

        public Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
            UnmanagedSpan* batchTerms,
            bool descending = false)
        {                _lookup.GetFor(batchResults, batchTermIds, BitConverter.DoubleToInt64Bits(double.MinValue));

            return EntryComparerHelper.NumericSortBatch<EntryComparerByDouble>(batchTermIds, batchTerms, descending);
        }
        public void Init(ref SortingMatch<TInner> match)
        {
            _lookup = match._searcher.DoubleReader(match._orderMetadata.Field.FieldName);
        }

        public int Compare(UnmanagedSpan x, UnmanagedSpan y)
        {
            return x.Double.CompareTo(y.Double);
        }

    }

    private struct EntryComparerByTermAlphaNumeric : IEntryComparer, IComparer<UnmanagedSpan>
    {
        private TermsReader _reader;
        private long _dictionaryId;
        private Lookup<long> _lookup;

        public void Init(ref SortingMatch<TInner> match)
        {
            _reader = match._searcher.TermsReaderFor(match._orderMetadata.Field.FieldName);
            _dictionaryId = match._searcher.GetDictionaryIdFor(match._orderMetadata.Field.FieldName);
            _lookup = match._searcher.TermsIdReaderFor(match._orderMetadata.Field.FieldName);
        }

        public Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
            UnmanagedSpan* batchTerms,
            bool descending = false)
        {
            _lookup.GetFor(batchResults, batchTermIds, long.MinValue);
            Container.GetAll(llt, batchTermIds, batchTerms, long.MinValue, pageLocator);
            var indexes = MemoryMarshal.Cast<long, int>(batchTermIds)[..(batchTermIds.Length)];
            for (int i = 0; i < batchTermIds.Length; i++)
            {
                indexes[i] = i;
            }
            EntryComparerHelper.IndirectSort(indexes, batchTerms, descending, this);
            return indexes;
        }


        public int Compare(UnmanagedSpan x, UnmanagedSpan y)
        {
            _reader.GetDecodedTerms(_dictionaryId, x, out var xTerm, y, out var yTerm);
            return Comparers.LegacySortingMatch.BasicComparers.CompareAlphanumericAscending(xTerm, yTerm);
        }
    }
        
    private struct EntryComparerBySpatial : IEntryComparer, IComparer<UnmanagedSpan>
    {
        private SpatialReader _reader;
        private (double X, double Y) _center;
        private SpatialUnits _units;
        private double _round;

        public void Init(ref SortingMatch<TInner> match)
        {
            _center = (match._orderMetadata.Point.X, match._orderMetadata.Point.Y);
            _units = match._orderMetadata.Units;
            _round = match._orderMetadata.Round;
            _reader = match._searcher.SpatialReader(match._orderMetadata.Field.FieldName);
        }

        public Span<int> SortBatch(ref SortingMatch<TInner> match, LowLevelTransaction llt, PageLocator pageLocator, Span<long> batchResults, Span<long> batchTermIds,
            UnmanagedSpan* batchTerms,
            bool descending = false)
        {
            var indexes = MemoryMarshal.Cast<long, int>(batchTermIds)[..(batchTermIds.Length)];
            for (int i = 0; i < batchResults.Length; i++)
            {
                double distance;
                if (_reader.TryGetSpatialPoint(batchResults[i], out var coords) == false)
                {
                    // always at the bottom, then, desc & asc
                    distance = descending ? double.MinValue : double.MaxValue;
                }
                else
                {
                    distance = SpatialUtils.GetGeoDistance(coords, _center, _round, _units);
                }

                batchTerms[i] = new UnmanagedSpan(distance);
                indexes[i] = i;
            }

            EntryComparerHelper.IndirectSort<EntryComparerByDouble>(indexes, batchTerms, descending);
            return indexes;
        }

        public int Compare(UnmanagedSpan x, UnmanagedSpan y)
        {
            return x.Double.CompareTo(y.Double);
        }
    }


    private readonly struct IndirectComparer<TComparer> : IComparer<long>, IComparer<int>
        where TComparer : struct, IComparer<UnmanagedSpan>
    {
        private readonly UnmanagedSpan* _terms;
        private readonly TComparer _inner;

        public IndirectComparer(UnmanagedSpan* terms, TComparer entryComparer)
        {
            _terms = terms;
            _inner = entryComparer;
        }

        public int Compare(long x, long y)
        {
            var xIdx = (ushort)x & 0X7FFF;
            var yIdx = (ushort)y & 0X7FFF;
            Debug.Assert(yIdx < SortBatchSize && xIdx < SortBatchSize);
            return _inner.Compare(_terms[xIdx], _terms[yIdx]);
        }

        public int Compare(int x, int y)
        {
            return _inner.Compare(_terms[x], _terms[y]);
        }
    }
}
