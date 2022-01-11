﻿using System;
using System.Collections.Generic;
using Sparrow.Server;
using Voron;
using Voron.Data.CompactTrees;

namespace Corax.Queries
{
    public unsafe struct ContainsTermProvider : ITermProvider
    {
        private readonly CompactTree _tree;
        private readonly IndexSearcher _searcher;
        private readonly string _field;
        private readonly Slice _term;

        private CompactTree.Iterator _iterator;

        public ContainsTermProvider(IndexSearcher searcher, ByteStringContext context, CompactTree tree, string field, string term, int fieldId)
        {
            _tree = tree;
            _searcher = searcher;
            _field = field;
            _iterator = tree.Iterate();
            _iterator.Reset();

            Slice.From(context, _searcher.EncodeTerm(term, fieldId), out _term);
           
        }

        public void Reset()
        {            
            _iterator = _tree.Iterate();
            _iterator.Reset();
        }

        public bool Next(out TermMatch term)
        {
            var contains = _term;
            while (_iterator.MoveNext(out Slice termSlice, out var _))
            {
                if (!termSlice.Contains(contains))
                    continue;

                term = _searcher.TermQuery(_field, termSlice.ToString());
                return true;
            }

            term = TermMatch.CreateEmpty();
            return false;
        }

        public QueryInspectionNode Inspect()
        {
            return new QueryInspectionNode($"{nameof(ContainsTermProvider)}",
                            parameters: new Dictionary<string, string>()
                            {
                                { "Field", _field },
                                { "Term", _term.ToString()}
                            });
        }
    }
}
