﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Corax.Querying.Matches.Meta;
using Voron.Data.Lookups;
using Voron.Impl;

namespace Corax.Querying.Matches
{
    [DebuggerDisplay("{DebugView,nq}")]
    public struct AllEntriesMatch : IQueryMatch
    {
        private readonly long _count;
        private Lookup<Int64LookupKey>.ForwardIterator _entriesPagesIt;

        public SkipSortingResult AttemptToSkipSorting()
        {
            //we are already returning in sorted order
            return SkipSortingResult.ResultsNativelySorted;
        }

        public AllEntriesMatch(IndexSearcher searcher, Transaction tx)
        {
            _count = searcher.NumberOfEntries;
            if (_count == 0)
            {
                _entriesPagesIt = new Lookup<Int64LookupKey>.ForwardIterator();
                return;
            }
            _entriesPagesIt = tx.LookupFor<Int64LookupKey>(Constants.IndexWriter.EntryIdToLocationSlice).Iterate();
            _entriesPagesIt.Reset();
        }

        public bool IsBoosting => false;
        public long Count => _count;
        public QueryCountConfidence Confidence => QueryCountConfidence.High;

        public int Fill(Span<long> matches)
        {
            return _entriesPagesIt.FillKeys(matches);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AndWith(Span<long> buffer, int matches)
        {
            // this match *everything*, so ands with everything 
            return matches;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Score(Span<long> matches, Span<float> scores, float boostFactor)
        {
            //there is no sense to add anything here because this would add same value to all items in collection.
        }

        public QueryInspectionNode Inspect()
        {
            return new QueryInspectionNode(nameof(AllEntriesMatch),
                parameters: new Dictionary<string, string>()
                {
                    { Constants.QueryInspectionNode.IsBoosting, IsBoosting.ToString() },
                    { Constants.QueryInspectionNode.Count, Count.ToString()},
                    { Constants.QueryInspectionNode.CountConfidence, Confidence.ToString()},
                });
        }

        string DebugView => Inspect().ToString();
    }
}
