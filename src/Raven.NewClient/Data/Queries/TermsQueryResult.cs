﻿using System.Collections.Generic;

namespace Raven.NewClient.Client.Data.Queries
{
    public class TermsQueryResult
    {
        public HashSet<string> Terms { get; set; }

        public long ResultEtag { get; set; }

        public string IndexName { get; set; }
    }
}