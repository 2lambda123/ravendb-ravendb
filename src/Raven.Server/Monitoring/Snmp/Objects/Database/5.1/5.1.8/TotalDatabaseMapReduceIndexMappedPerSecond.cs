using Lextm.SharpSnmpLib;
using Raven.Server.Documents;
using Raven.Server.ServerWide;

namespace Raven.Server.Monitoring.Snmp.Objects.Database
{
    public class TotalDatabaseMapReduceIndexMappedPerSecond : DatabaseBase<Gauge32>
    {
        public TotalDatabaseMapReduceIndexMappedPerSecond(ServerStore serverStore)
            : base(serverStore, SnmpOids.Databases.General.TotalMapReduceIndexMappedPerSecond)
        {
        }

        protected override Gauge32 GetData()
        {
            var count = 0;
            foreach (var database in GetLoadedDatabases())
                count += GetCount(database);

            return new Gauge32(count);
        }

        private static int GetCount(DocumentDatabase database)
        {
            return (int)database.Metrics.MapReduceIndexes.MappedPerSec.OneMinuteRate;
        }
    }
}
