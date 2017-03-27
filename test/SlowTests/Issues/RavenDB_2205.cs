// -----------------------------------------------------------------------
//  <copyright file="RavenDB_2205.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Globalization;
using FastTests;
using Raven.Client.Json.Converters;
using Xunit;

namespace SlowTests.Issues
{
    public class RavenDB_2205 : NoDisposalNeeded
    {
        [Fact]
        public void DateToolsRoundShouldKeepTheSameDateTimeKind()
        {
            var originalDate = DateTime.Parse("2014-04-25T09:33:15.6457886Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            var roundedDate = DateTools.Round(originalDate, DateTools.Resolution.SECOND);

            var expectedTime = DateTime.Parse("2014-04-25T09:33:15Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            Assert.Equal(expectedTime, roundedDate);
            Assert.Equal(expectedTime.Kind, roundedDate.Kind);
        }
    }
}
