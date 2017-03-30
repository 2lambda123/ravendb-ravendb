using System;

namespace Raven.Client.Documents.Replication
{
    public class IncomingReplicationPerformanceStats : ReplicationPerformanceBase<ReplicationPerformanceOperation>
    {
        public IncomingReplicationPerformanceStats()
        {
            // for deserialization
        }

        public IncomingReplicationPerformanceStats(TimeSpan duration)
            : base(duration)
        {
        }

        public long ReceivedLastEtag { get; set; }

        public NetworkStats Network { get; set; }

        public class NetworkStats
        {
            public int InputCount { get; set; }

            public int DocumentReadCount { get; set; }
            public int TombstoneReadCount { get; set; }
            public int AttachmentReadCount { get; set; }
        }
    }
}
