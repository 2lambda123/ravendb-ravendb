﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Corax.Queries;
using Corax;
using FastTests.Voron;
using Sparrow.Server;
using Voron;
using Xunit.Abstractions;
using Xunit;
using Sparrow.Threading;


namespace FastTests.Corax
{
    public class CoraxQueries : StorageTest
    {
        private List<Entry> _entries;
        private const int IndexId = 0, LongValue = 1;
        private Dictionary<Slice, int> _knownFields;
        public CoraxQueries(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void UnaryMatchWithSequential()
        {
            PrepareData();
            IndexEntries();
            using var ctx = new ByteStringContext(SharedMultipleUseFlag.None);
            using var searcher = new IndexSearcher(Env);
            Slice.From(ctx, "3", out var three);
            var match0 = searcher.AllEntries();
            var match1 = searcher.GreaterThan(match0, LongValue, three);
            var expectedList = GetExpectedResult("3");
            expectedList.Sort();
            var outputList = FetchFromCorax(ref match1);
            outputList.Sort();
            Assert.Equal(expectedList.Count, outputList.Count);
            for (int i = 0; i < expectedList.Count; ++i) 
                Assert.Equal(expectedList[i], outputList[i]);
        }
        
        [Fact]
        public void UnaryMatchWithNumerical()
        {
            PrepareData();
            IndexEntries();
            using var ctx = new ByteStringContext(SharedMultipleUseFlag.None);
            using var searcher = new IndexSearcher(Env);
      
            var match0 = searcher.AllEntries();
            var match1 = searcher.GreaterThan(match0, LongValue, 3);
            var expectedList = _entries.Where(x => x.LongValue > 3).Select(x => x.Id).ToList();
            expectedList.Sort();
            var outputList = FetchFromCorax(ref match1);
            outputList.Sort();
            Assert.Equal(expectedList.Count, outputList.Count);
            for (int i = 0; i < expectedList.Count; ++i) 
                Assert.Equal(expectedList[i], outputList[i]);
        }
        
        private void IndexEntries()
        {
            using var ctx = new ByteStringContext(SharedMultipleUseFlag.None);

            _knownFields = CreateKnownFields(ctx);

            const int bufferSize = 4096;
            using var _ = ctx.Allocate(bufferSize, out ByteString buffer);

            {
                using var indexWriter = new IndexWriter(Env);
                foreach (var entry in _entries)
                {
                    var entryWriter = new IndexEntryWriter(buffer.ToSpan(), _knownFields);
                    var data = CreateIndexEntry(ref entryWriter, entry);
                    indexWriter.Index(entry.Id, data, _knownFields);
                }

                indexWriter.Commit();
            }
        }

        private Span<byte> CreateIndexEntry(ref IndexEntryWriter entryWriter, Entry entry)
        {
            entryWriter.Write(IndexId, Encoding.UTF8.GetBytes(entry.Id));
            entryWriter.Write(LongValue, Encoding.UTF8.GetBytes(entry.LongValue.ToString()), entry.LongValue, entry.LongValue);
            entryWriter.Finish(out var output);
            return output;
        }

        private Dictionary<Slice, int> CreateKnownFields(ByteStringContext ctx)
        {
            Slice.From(ctx, "Id", ByteStringType.Immutable, out Slice idSlice);
            Slice.From(ctx, "Content", ByteStringType.Immutable, out Slice longSlice);

            return new Dictionary<Slice, int> { [idSlice] = IndexId, [longSlice] = LongValue};
        }
        
        private void PrepareData(int size = 1000)
        {
            _entries ??= new();
            for (int i = 0; i < size; ++i)
            {
                _entries.Add(new Entry()
                {
                    Id = $"entries/{i}",
                    LongValue = i
                });
            }
        }

        private List<string> FetchFromCorax(ref UnaryMatch match)
        {
            using var indexSearcher = new IndexSearcher(Env);
            List<string> list = new();
            Span<long> ids = stackalloc long[256];
            int read = match.Fill(ids);
            while (read != 0)
            {
                for(int i = 0; i < read; ++i)
                    list.Add(indexSearcher.GetIdentityFor(ids[i]));
                read = match.Fill(ids);
            }

            return list;
        }
        
        private List<string> GetExpectedResult(string input)
        {
            return _entries.Where(entry => entry.LongValue.ToString().CompareTo(input) == 1).Select(x => x.Id).ToList();
        }
        
        private class Entry
        {
            public string Id { get; set; }
            
            public long LongValue { get; set; }
        }
    }
}
