//-----------------------------------------------------------------------
// <copyright file="Expiration.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using FastTests;
using FastTests.Server.Documents.Expiration;
using Xunit;

namespace StressTests.Core.Bundle
{
    public class ExpirationStressTest : NoDisposalNeeded
    {
        [Theory]
        [InlineData(1000)]
        [InlineData(10000)]
        public async Task CanAddALotOfEntitiesWithSameExpiry_ThenReadItBeforeItExpires_ButWillNotBeAbleToReadItAfterExpiry(int count)
        {
            using (var expiration = new Expiration())
            {
                await expiration.CanAddALotOfEntitiesWithSameExpiry_ThenReadItBeforeItExpires_ButWillNotBeAbleToReadItAfterExpiry(count);
            }
        }
    }
}