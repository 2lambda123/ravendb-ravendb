﻿using System;
using System.Globalization;
using JetBrains.Annotations;
using Sparrow;
using Sparrow.Json;

namespace Raven.Server.Documents.Handlers.Processors.HiLo;

internal abstract class AbstractHiLoHandlerProcessorForGetNextHiLo<TRequestHandler, TOperationContext> : AbstractDatabaseHandlerProcessor<TRequestHandler, TOperationContext>
    where TOperationContext : JsonOperationContext
    where TRequestHandler : AbstractDatabaseRequestHandler<TOperationContext>
{
    protected AbstractHiLoHandlerProcessorForGetNextHiLo([NotNull] TRequestHandler requestHandler) : base(requestHandler)
    {
    }

    protected string GetTag() => RequestHandler.GetQueryStringValueAndAssertIfSingleAndNotEmpty("tag");

    protected long GetLastBatchSize() => RequestHandler.GetLongQueryString("lastBatchSize", false) ?? 0;

    protected DateTime? GetLastRangeAt()
    {
        var lastRangeAtAsString = RequestHandler.GetStringQueryString("lastRangeAt", false);
        if (lastRangeAtAsString == null || DateTime.TryParseExact(lastRangeAtAsString, DefaultFormat.DateTimeOffsetFormatsToWrite, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime lastRangeAt) == false)
            return null;

        return lastRangeAt;
    }

    protected char GetIdentityPartsSeparator() => RequestHandler.GetCharQueryString("identityPartsSeparator", false) ?? RequestHandler.IdentityPartsSeparator;

    protected long GetLastMax() => RequestHandler.GetLongQueryString("lastMax", false) ?? 0;
}
