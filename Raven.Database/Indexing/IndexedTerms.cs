﻿// -----------------------------------------------------------------------
//  <copyright file="CachedIndexedTerms.cs" company="Hibernating Rhinos LTD"> 
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Lucene.Net.Index;
using Lucene.Net.Util;
using Raven.Imports.Newtonsoft.Json.Linq;
using Raven.Json.Linq;

namespace Raven.Database.Indexing
{
    public class IndexedTerms
    {
        public static void ReadEntriesForFields(
            IndexSearcherHolder.IndexSearcherHoldingState state,
            HashSet<string> fieldsToRead,
            HashSet<int> docIds,
            Func<Term, double> convert,
            Action<Term, double, int> onTermFound)
        {
            IndexReader reader = state.IndexSearcher.IndexReader;

            var readFromCache = new Dictionary<string, HashSet<int>>();

            state.Lock.EnterReadLock();
            try
            {
                ReadFieldsFromCache(state, fieldsToRead, docIds, convert, onTermFound, readFromCache);
            }
            finally
            {
                state.Lock.ExitReadLock();
            }

            foreach (var kvp in readFromCache)
            {
                if (kvp.Value.Count == docIds.Count)
                {
                    fieldsToRead.Remove(kvp.Key); // already read all of it
                }
            }

            if (fieldsToRead.Count == 0)
                return;

            state.Lock.EnterWriteLock();
            try
            {
                readFromCache.Clear();
                ReadFieldsFromCache(state, fieldsToRead, docIds, convert, onTermFound, readFromCache);
                foreach (var kvp in readFromCache)
                {
                    if (kvp.Value.Count == docIds.Count)
                    {
                        fieldsToRead.Remove(kvp.Key); // already read all of it
                    }
                }

                if (fieldsToRead.Count == 0)
                    return;

                using (TermDocs termDocs = reader.TermDocs())
                {
                    foreach (string field in fieldsToRead)
                    {
                        HashSet<int> read = readFromCache[field];
                        var shouldReset = new HashSet<Tuple<string, int>>();
                        using (TermEnum termEnum = reader.Terms(new Term(field)))
                        {
                            do
                            {
                                if (termEnum.Term == null || field != termEnum.Term.Field)
                                    break;

                                if (LowPrecisionNumber(termEnum.Term))
                                    continue;


                                int totalDocCountIncludedDeletes = termEnum.DocFreq();
                                termDocs.Seek(termEnum.Term);

                                while (termDocs.Next() && totalDocCountIncludedDeletes > 0)
                                {
                                    totalDocCountIncludedDeletes -= 1;
                                    if (read.Contains(termDocs.Doc))
                                        continue;
                                    if (reader.IsDeleted(termDocs.Doc))
                                        continue;
                                    if (docIds.Contains(termDocs.Doc) == false)
                                        continue;

                                    if (shouldReset.Add(Tuple.Create(field, termDocs.Doc)))
                                    {
                                        state.ResetInCache(field, termDocs.Doc);
                                    }

                                    double d = convert(termEnum.Term);
                                    state.SetInCache(field, termDocs.Doc, termEnum.Term, d);
                                    onTermFound(termEnum.Term, d, termDocs.Doc);
                                }
                            } while (termEnum.Next());
                        }
                    }
                }
            }
            finally
            {
                state.Lock.ExitWriteLock();
            }
        }

        private static void ReadFieldsFromCache(IndexSearcherHolder.IndexSearcherHoldingState state,
            HashSet<string> fieldsToRead, HashSet<int> docIds,
            Func<Term, double> convert, Action<Term, double, int> onTermFound,
            Dictionary<string, HashSet<int>> readFromCache)
        {
            foreach (string field in fieldsToRead)
            {
                var read = new HashSet<int>();
                readFromCache[field] = read;
                foreach (int docId in docIds)
                {
                    foreach (
                        IndexSearcherHolder.IndexSearcherHoldingState.CacheVal val in state.GetFromCache(field, docId))
                    {
                        read.Add(docId);

                        double converted;
                        if (val.Val == null)
                        {
                            val.Val = converted = convert(val.Term);
                        }
                        else
                        {
                            converted = val.Val.Value;
                        }
                        onTermFound(val.Term, converted, docId);
                    }
                }
            }
        }


        public static void ReadEntriesForFields(
            IndexSearcherHolder.IndexSearcherHoldingState state,
            HashSet<string> fieldsToRead,
            HashSet<int> docIds,
            Action<Term, int> onTermFound)
        {
            IndexReader reader = state.IndexSearcher.IndexReader;

            var readFromCache = new Dictionary<string, HashSet<int>>();

            state.Lock.EnterReadLock();
            try
            {
                ReadFieldsFromCache(state, fieldsToRead, docIds, onTermFound, readFromCache);
            }
            finally
            {
                state.Lock.ExitReadLock();
            }

            foreach (var kvp in readFromCache)
            {
                if (kvp.Value.Count == docIds.Count)
                {
                    fieldsToRead.Remove(kvp.Key); // already read all of it
                }
            }

            if (fieldsToRead.Count == 0)
                return;

            state.Lock.EnterWriteLock();
            try
            {
                readFromCache.Clear();
                ReadFieldsFromCache(state, fieldsToRead, docIds, onTermFound, readFromCache);
                foreach (var kvp in readFromCache)
                {
                    if (kvp.Value.Count == docIds.Count)
                    {
                        fieldsToRead.Remove(kvp.Key); // already read all of it
                    }
                }
                if (fieldsToRead.Count == 0)
                    return;

                using (TermDocs termDocs = reader.TermDocs())
                {
                    foreach (string field in fieldsToRead)
                    {
                        HashSet<int> read = readFromCache[field];
                        var shouldReset = new HashSet<Tuple<string, int>>();
                        using (TermEnum termEnum = reader.Terms(new Term(field)))
                        {
                            do
                            {
                                if (termEnum.Term == null || field != termEnum.Term.Field)
                                    break;

                                if (LowPrecisionNumber(termEnum.Term))
                                    continue;


                                int totalDocCountIncludedDeletes = termEnum.DocFreq();
                                termDocs.Seek(termEnum.Term);

                                while (termDocs.Next() && totalDocCountIncludedDeletes > 0)
                                {
                                    totalDocCountIncludedDeletes -= 1;
                                    if (read.Contains(termDocs.Doc))
                                        continue;
                                    if (reader.IsDeleted(termDocs.Doc))
                                        continue;
                                    if (docIds.Contains(termDocs.Doc) == false)
                                        continue;

                                    if (shouldReset.Add(Tuple.Create(field, termDocs.Doc)))
                                    {
                                        state.ResetInCache(field, termDocs.Doc);
                                    }

                                    state.SetInCache(field, termDocs.Doc, termEnum.Term);
                                    onTermFound(termEnum.Term, termDocs.Doc);
                                }
                            } while (termEnum.Next());
                        }
                    }
                }
            }
            finally
            {
                state.Lock.ExitWriteLock();
            }
        }

        private static void ReadFieldsFromCache(IndexSearcherHolder.IndexSearcherHoldingState state,
            HashSet<string> fieldsToRead, HashSet<int> docIds,
            Action<Term, int> onTermFound, Dictionary<string, HashSet<int>> readFromCache)
        {
            foreach (string field in fieldsToRead)
            {
                var read = new HashSet<int>();
                readFromCache[field] = read;
                foreach (int docId in docIds)
                {
                    foreach (Term term in state.GetTermsFromCache(field, docId))
                    {
                        read.Add(docId);
                        onTermFound(term, docId);
                    }
                }
            }
        }

        private static bool LowPrecisionNumber(Term term)
        {
            if (term.Field.EndsWith("_Range") == false)
                return false;

            if (string.IsNullOrEmpty(term.Text))
                return false;

            return term.Text[0] - NumericUtils.SHIFT_START_INT != 0 &&
                   term.Text[0] - NumericUtils.SHIFT_START_LONG != 0;
        }

        public static RavenJObject[] ReadAllEntriesFromIndex(IndexReader reader)
        {
            if (reader.MaxDoc > 128*1024)
            {
                throw new InvalidOperationException("Refusing to extract all index entires from an index with " +
                                                    reader.MaxDoc +
                                                    " entries, because of the probable time / memory costs associated with that." +
                                                    Environment.NewLine +
                                                    "Viewing Index Entries are a debug tool, and should not be used on indexes of this size. You might want to try Luke, instead.");
            }
            var results = new RavenJObject[reader.MaxDoc];
            using (TermDocs termDocs = reader.TermDocs())
            using (TermEnum termEnum = reader.Terms())
            {
                while (termEnum.Next())
                {
                    Term term = termEnum.Term;
                    if (term == null)
                        break;

                    string text = term.Text;

                    termDocs.Seek(termEnum);
                    for (int i = 0; i < termEnum.DocFreq() && termDocs.Next(); i++)
                    {
                        RavenJObject result = results[termDocs.Doc];
                        if (result == null)
                            results[termDocs.Doc] = result = new RavenJObject();
                        string propertyName = term.Field;
                        if (propertyName.EndsWith("_ConvertToJson") ||
                            propertyName.EndsWith("_IsArray"))
                            continue;
                        if (result.ContainsKey(propertyName))
                        {
                            switch (result[propertyName].Type)
                            {
                                case JTokenType.Array:
                                    ((RavenJArray) result[propertyName]).Add(text);
                                    break;
                                case JTokenType.String:
                                    result[propertyName] = new RavenJArray
                                    {
                                        result[propertyName],
                                        text
                                    };
                                    break;
                                default:
                                    throw new ArgumentException("No idea how to handle " + result[propertyName].Type);
                            }
                        }
                        else
                        {
                            result[propertyName] = text;
                        }
                    }
                }
            }
            return results;
        }
    }
}