using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Corax;
using FastTests.Voron;
using Sparrow;
using Sparrow.Extensions;
using Sparrow.Server;
using Voron;
using Xunit;
using Xunit.Abstractions;

namespace FastTests.Corax
{
    public class IndexEntryWriterTest : StorageTest
    {
        public IndexEntryWriterTest(ITestOutputHelper output) : base(output)
        {
        }

        private readonly struct StringArrayIterator : IReadOnlySpanEnumerator
        {
            private readonly string[] _values;

            public StringArrayIterator(string[] values)
            {
                _values = values;
            }

            public int Length => _values.Length;

            public ReadOnlySpan<byte> this[int i] => Encoding.UTF8.GetBytes(_values[i]);
        }

        [Fact]
        public void SimpleWrites()
        {
            Span<byte> buffer = new byte[32000];

            using var _ = StorageEnvironment.GetStaticContext(out var ctx);
            Slice.From(ctx, "A", ByteStringType.Immutable, out Slice aSlice);
            Slice.From(ctx, "B", ByteStringType.Immutable, out Slice bSlice);
            Slice.From(ctx, "C", ByteStringType.Immutable, out Slice cSlice);
            Slice.From(ctx, "D", ByteStringType.Immutable, out Slice dSlice);

            // The idea is that GetField will return an struct we can use later on a loop (we just get it once).
            var knownFields = new Dictionary<Slice, int>()
            {
                [aSlice] = 0,
                [bSlice] = 1,
                [cSlice] = 2,
                [dSlice] = 3
            };

            var writer = new IndexEntryWriter(buffer, knownFields);
            writer.Write(0, Encoding.UTF8.GetBytes("1.001"), 1, 1.001);
            writer.Write(1, new StringArrayIterator(new[] { "AAA", "BF", "CE" }));
            writer.Write(2, Encoding.UTF8.GetBytes("CCCC"));
            writer.Write(3, Encoding.UTF8.GetBytes("DDDDDDDDDD"));
            var length = writer.Finish(out var element);

            var reader = new IndexEntryReader(element);
            reader.Read(0, out long longValue);
            Assert.Equal(1, longValue);
            reader.Read(0, out int intValue);
            Assert.Equal(1, intValue);
            
            Assert.True(reader.GetFieldType(0).HasFlag(IndexEntryFieldType.Tuple));
            Assert.False(reader.GetFieldType(0).HasFlag(IndexEntryFieldType.List));
            Assert.True(reader.GetFieldType(1).HasFlag(IndexEntryFieldType.List));
            Assert.False(reader.GetFieldType(1).HasFlag(IndexEntryFieldType.Tuple));
            Assert.True(reader.GetFieldType(2).HasFlag(IndexEntryFieldType.None));
            Assert.True(reader.GetFieldType(3).HasFlag(IndexEntryFieldType.None));

            reader.Read(0, out double doubleValue);
            Assert.True(doubleValue.AlmostEquals(1.001));
            reader.Read(0, out double floatValue);
            Assert.True(floatValue.AlmostEquals(1.001));

            reader.Read(0, out longValue, out doubleValue, out var sequenceValue);
            Assert.True(doubleValue.AlmostEquals(1.001));
            Assert.Equal(1, longValue);
            Assert.True(sequenceValue.SequenceCompareTo(Encoding.UTF8.GetBytes("1.001").AsSpan()) == 0);

            reader.Read(2, value: out sequenceValue);
            Assert.True(sequenceValue.SequenceCompareTo(Encoding.UTF8.GetBytes("CCCC").AsSpan()) == 0);
            reader.Read(3, value: out sequenceValue);
            Assert.True(sequenceValue.SequenceCompareTo(Encoding.UTF8.GetBytes("DDDDDDDDDD").AsSpan()) == 0);

            reader.Read(1, value: out sequenceValue, elementIdx: 0);
            Assert.True(sequenceValue.SequenceCompareTo(Encoding.UTF8.GetBytes("AAA").AsSpan()) == 0);
            reader.Read(1, value: out sequenceValue, elementIdx: 2);
            Assert.True(sequenceValue.SequenceCompareTo(Encoding.UTF8.GetBytes("CE").AsSpan()) == 0);
        }


        [Fact]
        public void IterationReads()
        {
            Span<byte> buffer = new byte[64000];

            using var _ = StorageEnvironment.GetStaticContext(out var ctx);
            Slice.From(ctx, "A", ByteStringType.Immutable, out Slice aSlice);
            Slice.From(ctx, "B", ByteStringType.Immutable, out Slice bSlice);
            Slice.From(ctx, "C", ByteStringType.Immutable, out Slice cSlice);
            Slice.From(ctx, "D", ByteStringType.Immutable, out Slice dSlice);

            // The idea is that GetField will return an struct we can use later on a loop (we just get it once).
            var knownFields = new Dictionary<Slice, int>()
            {
                [aSlice] = 0,
                [bSlice] = 1,
                [cSlice] = 2,
                [dSlice] = 3
            };

            string[] values =
            {
                "A",
                "BB",
                "CCC",
                "DDDD",
                "EEEEE"
            };

            Span<long> longValues = new long[] { 1, 2, 3, 4, 5 };
            Span<double> doubleValues = new[] { 1.01, 2.01, 3.01, 4.01, 5.01 };

            var writer = new IndexEntryWriter(buffer, knownFields);
            writer.Write(0, new StringArrayIterator(values));
            writer.Write(1, new StringArrayIterator(values), longValues, doubleValues);
            writer.Write(2, Encoding.UTF8.GetBytes(values[3]));
            var length = writer.Finish(out var element);

            var reader = new IndexEntryReader(element);

            // Get the first
            reader.Read(1, out var longValue, out var doubleValue, out var sequenceValue);
            Assert.True(doubleValue.AlmostEquals(1.01));
            Assert.Equal(1, longValue);
            Assert.True(sequenceValue.SequenceCompareTo(Encoding.UTF8.GetBytes(values[0]).AsSpan()) == 0);

            Assert.False(reader.TryReadMany(2, out var fieldIterator));

            fieldIterator = reader.ReadMany(1);
            Assert.Equal(5, fieldIterator.Count);

            int i = 0;
            while (fieldIterator.ReadNext())
            {
                Assert.True(fieldIterator.Double.AlmostEquals(i + 1.01));
                Assert.Equal(1 + i, fieldIterator.Long);
                Assert.True(fieldIterator.Sequence.SequenceCompareTo(Encoding.UTF8.GetBytes(values[i]).AsSpan()) == 0);
                i++;
            }

            try { var __ = fieldIterator.Double; } catch (IndexOutOfRangeException) {}
            try { var __ = fieldIterator.Long; } catch (IndexOutOfRangeException) {}
            try { var __ = fieldIterator.Sequence; } catch (IndexOutOfRangeException) { }

            fieldIterator = reader.ReadMany(0);
            Assert.Equal(5, fieldIterator.Count);

            i = 0;
            while (fieldIterator.ReadNext())
            {
                try { var __ = fieldIterator.Double; } catch (InvalidOperationException) { }
                try { var __ = fieldIterator.Long; } catch (InvalidOperationException) { }
                Assert.True(fieldIterator.Sequence.SequenceCompareTo(Encoding.UTF8.GetBytes(values[i]).AsSpan()) == 0);
                i++;
            }
        }
    }
}
