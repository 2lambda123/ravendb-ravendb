﻿using FastTests;
using SlowTests.Utils;
using SlowTests.Voron;
using Xunit;

namespace StressTests.Voron
{
    public class LargeFixedSizeTreesStressCases : NoDisposalNeeded
    {
        [Theory]
        [InlineDataWithRandomSeed(94000)]
        [InlineDataWithRandomSeed(300000)]
        public void CanDeleteRange_TryToFindABranchNextToLeaf(int count, int seed)
        {
            using (var test = new LargeFixedSizeTrees())
            {
                test.CanDeleteRange_TryToFindABranchNextToLeaf(count, seed);
            }
        }

        [Theory]
        [InlineDataWithRandomSeed(1000000)]
        [InlineDataWithRandomSeed(2000000)]
        public void CanDeleteRange_RandomRanges(int count, int seed)
        {
            using (var test = new LargeFixedSizeTrees())
            {
                test.CanDeleteRange_RandomRanges(count, seed);
            }
        }

        [Theory]
        [InlineDataWithRandomSeed(300000)]
        public void CanDeleteRange_RandomRanges_WithGaps(int count, int seed)
        {
            using (var test = new LargeFixedSizeTrees())
            {
                test.CanDeleteRange_RandomRanges_WithGaps(count, seed);
            }
        }
    }
}