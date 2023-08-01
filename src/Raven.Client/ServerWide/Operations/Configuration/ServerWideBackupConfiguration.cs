﻿using Raven.Client.Documents.Operations.Backups;
using Raven.Client.ServerWide.Operations.OngoingTasks;
using Sparrow.Json.Parsing;

namespace Raven.Client.ServerWide.Operations.Configuration
{
    public sealed class ServerWideBackupConfiguration : PeriodicBackupConfiguration, IServerWideTask
    {
        internal static string NamePrefix = "Server Wide Backup";

        public string[] ExcludedDatabases { get; set; }

        public override DynamicJsonValue ToJson()
        {
            var json = base.ToJson();
            json[nameof(NamePrefix)] = NamePrefix;
            json[nameof(ExcludedDatabases)] = ExcludedDatabases;
            return json;
        }
    }
}
