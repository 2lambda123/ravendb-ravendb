using System;
using System.Collections.Generic;

namespace Raven.Server.Documents.ETL.Providers.SQL.Connections
{
    public class SqlConnections
    {
        public string Id { get; set; }

        public readonly Dictionary<string, PredefinedSqlConnection> Connections;

        public SqlConnections()
        {
            Connections = new Dictionary<string, PredefinedSqlConnection>(StringComparer.OrdinalIgnoreCase);
        }
    }
}