﻿using Corax.Querying.Matches;
using Corax.Querying.Matches.Meta;

namespace Corax.Querying;

public partial class IndexSearcher
{
    public MultiUnaryMatch<TInner> CreateMultiUnaryMatch<TInner>(TInner inner, MultiUnaryItem[] unaryItems)
    where TInner : IQueryMatch
    {
        return new MultiUnaryMatch<TInner>(this, inner, unaryItems);
    }


}
