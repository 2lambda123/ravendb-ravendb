﻿using System;

namespace Raven.Client.ServerWide.Tcp
{
    public class TcpConnectionHeaderMessage
    {
        public enum OperationTypes
        {
            None,
            Subscription,
            Replication,
            Cluster,
            Heartbeats
        }

        public string DatabaseName { get; set; }

        public string SourceNodeTag { get; set; }

        public OperationTypes Operation { get; set; }

        public int OperationVersion { get; set; }

        public static readonly int ClusterTcpVersion = 10;
        public static readonly int HeartbeatsTcpVersion = 20;
        public static readonly int ReplicationTcpVersion = 30;
        public static readonly int SubscriptionTcpVersion = 40;

        public static int GetOperationTcpVersion(OperationTypes operationType)
        {
            switch (operationType)
            {
                case OperationTypes.None:
                    return -1;
                case OperationTypes.Subscription:
                    return SubscriptionTcpVersion;
                case OperationTypes.Replication:
                    return ReplicationTcpVersion;
                case OperationTypes.Cluster:
                    return  ClusterTcpVersion;
                case OperationTypes.Heartbeats:
                    return  HeartbeatsTcpVersion;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operationType), operationType, null);
            }
        }
    }
}
