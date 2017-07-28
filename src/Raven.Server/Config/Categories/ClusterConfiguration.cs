﻿using System.ComponentModel;
using Raven.Server.Config.Attributes;
using Raven.Server.Config.Settings;

namespace Raven.Server.Config.Categories
{
    public class ClusterConfiguration : ConfigurationCategory
    {
        [Description("Timeout in which the node expects to receive a heartbeat from the leader")]
        [DefaultValue(300)]
        [TimeUnit(TimeUnit.Milliseconds)]
        [ConfigurationEntry("Cluster.ElectionTimeoutInMs")]
        public TimeSetting ElectionTimeout { get; set; }

        [Description("How frequently we sample the information about the databases and send it to the maintenance supervisor.")]
        [DefaultValue(250)]
        [TimeUnit(TimeUnit.Milliseconds)]
        [ConfigurationEntry("Cluster.WorkerSamplePeriodInMs")]
        public TimeSetting WorkerSamplePeriod { get; set; }

        [Description("As the maintenance supervisor, how frequent we sample the information received from the nodes.")]
        [DefaultValue(500)]
        [TimeUnit(TimeUnit.Milliseconds)]
        [ConfigurationEntry("Cluster.SupervisorSamplePeriodInMs")]
        public TimeSetting SupervisorSamplePeriod { get; set; }

        [Description("As the maintenance supervisor, how long we wait to hear from a worker before it is time out.")]
        [DefaultValue(1000)]
        [TimeUnit(TimeUnit.Milliseconds)]
        [ConfigurationEntry("Cluster.ReceiveFromWorkerTimeoutInMs")]
        public TimeSetting ReceiveFromWorkerTimeout { get; set; }

        [Description("As the maintenance supervisor, how long we wait after we received an exception from a worker. Before we retry.")]
        [DefaultValue(5000)]
        [TimeUnit(TimeUnit.Milliseconds)]
        [ConfigurationEntry("Cluster.OnErrorDelayTimeInMs")]
        public TimeSetting OnErrorDelayTime { get; set; }

        [Description("As a cluster node, how long it takes to timeout operation between two cluster nodes.")]
        [DefaultValue(15)]
        [TimeUnit(TimeUnit.Seconds)]
        [ConfigurationEntry("Cluster.ClusterOperationTimeoutInSec")]
        public TimeSetting ClusterOperationTimeout { get; set; }

        [Description("The time we give to the cluster stats to stabilize after a database topology change.")]
        [DefaultValue(5)]
        [TimeUnit(TimeUnit.Seconds)]
        [ConfigurationEntry("Cluster.StatsStabilizationTimeInSec")]
        public TimeSetting StabilizationTime { get; set; }

        [Description("The time we give to a database instance to be in a good and responsive state, before we adding a replica to match the replication factor.")]
        [DefaultValue(15 * 60)]
        [TimeUnit(TimeUnit.Seconds)]
        [ConfigurationEntry("Cluster.TimeBeforeAddingReplicaInSec")]
        public TimeSetting AddReplicaTimeout{ get; set; }

    }
}
