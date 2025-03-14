﻿using System;
using System.Text;
using Raven.Client.Documents.Replication;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.Replication
{
    public sealed class PullReplicationAsSink : ExternalReplicationBase
    {
        public PullReplicationMode Mode = PullReplicationMode.HubToSink;

        public string[] AllowedHubToSinkPaths;
        public string[] AllowedSinkToHubPaths;

        public string CertificateWithPrivateKey; // base64
        public string CertificatePassword;

        public string AccessName;

        public string HubName;

        public PullReplicationAsSink()
        {
        }

        public PullReplicationAsSink(string database, string connectionStringName, string hubName) : base(database, connectionStringName)
        {
            HubName = hubName;
        }

        public override ReplicationType GetReplicationType() => ReplicationType.PullAsSink;

        public override bool IsEqualTo(ReplicationNode other)
        {
            if (other is PullReplicationAsSink sink)
            {
                return base.IsEqualTo(other) &&
                       string.Equals(Url, sink.Url, StringComparison.OrdinalIgnoreCase) &&
                       Mode == sink.Mode &&
                       string.Equals(HubName, sink.HubName) &&
                       string.Equals(CertificatePassword, sink.CertificatePassword) &&
                       string.Equals(CertificateWithPrivateKey, sink.CertificateWithPrivateKey);
            }

            return false;
        }

        public override ulong GetTaskKey()
        {
            var hashCode = base.GetTaskKey();
            hashCode = (hashCode * 397) ^ (ulong)Mode;
            hashCode = (hashCode * 397) ^ CalculateStringHash(Url);
            hashCode = (hashCode * 397) ^ CalculateStringHash(CertificateWithPrivateKey);
            hashCode = (hashCode * 397) ^ CalculateStringHash(CertificatePassword);
            return (hashCode * 397) ^ CalculateStringHash(HubName);
        }

        public override DynamicJsonValue ToJson()
        {
            if (string.IsNullOrEmpty(HubName))
                throw new ArgumentException("Must be not empty", nameof(HubName));

            var djv = base.ToJson();

            djv[nameof(Mode)] = Mode;
            djv[nameof(HubName)] = HubName;
            djv[nameof(CertificateWithPrivateKey)] = CertificateWithPrivateKey;
            djv[nameof(CertificatePassword)] = CertificatePassword;
            djv[nameof(AllowedHubToSinkPaths)] = AllowedHubToSinkPaths;
            djv[nameof(AllowedSinkToHubPaths)] = AllowedSinkToHubPaths;
            djv[nameof(AccessName)] = AccessName;
            return djv;
        }

        public override DynamicJsonValue ToAuditJson()
        {
            var djv = base.ToAuditJson();

            djv[nameof(Mode)] = Mode;
            djv[nameof(HubName)] = HubName;
            djv[nameof(AllowedHubToSinkPaths)] = AllowedHubToSinkPaths;
            djv[nameof(AllowedSinkToHubPaths)] = AllowedSinkToHubPaths;
            djv[nameof(AccessName)] = AccessName;
            return djv;
        }

        public override string ToString()
        {
            var sb = new StringBuilder($"Replication Sink {FromString()}. " +
                                       $"Hub Task Name: '{HubName}', " +
                                       $"Connection String: '{ConnectionStringName}', " +
                                       $"Mode: '{Mode}'");

            if (string.IsNullOrEmpty(AccessName) == false)
                sb.Append($", Access Name: '{AccessName}'");

            return sb.ToString();
        }

        public override string GetDefaultTaskName()
        {
            return ToString();
        }
    }
}
