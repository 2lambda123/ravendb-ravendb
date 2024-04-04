﻿using System;
using Raven.Client.Documents.Replication;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.Replication
{
    public class ExternalReplication : ExternalReplicationBase, IExternalReplication
    {
        public TimeSpan DelayReplicationFor { get; set; }

        public ReplicationType Type = ReplicationType.External;

        public ExternalReplication()
        {
        }

        public ExternalReplication(string database, string connectionStringName) : base(database, connectionStringName)
        {
        }

        public override DynamicJsonValue ToJson()
        {
            var json = base.ToJson();
            json[nameof(DelayReplicationFor)] = DelayReplicationFor;
            json[nameof(Type)] = Type;
            return json;
        }

        public override DynamicJsonValue ToAuditJson()
        {
            return ToJson();
        }
        public override bool IsEqualTo(ReplicationNode other)
        {
            if (other is ExternalReplication externalReplication)
            {
                return base.IsEqualTo(other) &&
                       DelayReplicationFor == externalReplication.DelayReplicationFor;
            }

            return false;
        }
    }
}
