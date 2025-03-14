﻿using System;

namespace Corax.Querying.Matches.Meta
{
    public interface ITermProvider
    {
        bool IsFillSupported { get; }

        int Fill(Span<long> containers);
        
        void Reset();
        bool Next(out TermMatch term);
        QueryInspectionNode Inspect();

        string DebugView => Inspect().ToString();
    }    
}
