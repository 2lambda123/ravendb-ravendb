﻿using System.Collections.Generic;
using Raven.Server.Documents.ETL.Test;

namespace Raven.Server.Documents.ETL.Providers.Queue.Test
{
    public sealed class QueueEtlTestScriptResult : TestEtlScriptResult
    {
        public List<QueueSummary> Summary { get; set; }
    }
}
