﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Raven.Abstractions.Connection;
using Raven.Abstractions.Counters;
using Raven.Client.Counters.Changes;
using Raven.Client.Counters.Replication;

namespace Raven.Client.Counters
{
	public interface ICounterStore : IDisposalNotification
	{
		CounterStore.BatchOperationsStore Batch { get; }

		OperationCredentials Credentials { get;  }

		string Name { get; }

		string Url { get; }

		CountersConvention Convention { get; set; }

		CounterReplicationInformer ReplicationInformer { get; }

		CounterStore.CounterStoreAdvancedOperations Advanced { get; }

		CounterStore.CounterStoreAdminOperations Admin { get; }

		Task ChangeAsync(string groupName, string counterName, long delta, CancellationToken token = default(CancellationToken));

		Task IncrementAsync(string groupName, string counterName, CancellationToken token = default(CancellationToken));

		Task DecrementAsync(string groupName, string counterName, CancellationToken token = default(CancellationToken));

		Task ResetAsync(string groupName, string counterName, CancellationToken token = default(CancellationToken));

		Task DeleteAsync(string groupName, string counterName, CancellationToken token = default(CancellationToken));

		Task<long> GetOverallTotalAsync(string groupName, string counterName, CancellationToken token = default(CancellationToken));

		Task<CounterStorageStats> GetCounterStatsAsync(CancellationToken token = default (CancellationToken));

		Task<CountersStorageMetrics> GetCounterMetricsAsync(CancellationToken token = default (CancellationToken));

		Task<List<CounterStorageReplicationStats>> GetCounterRelicationStatsAsync(CancellationToken token = default (CancellationToken));


		Task<CountersReplicationDocument> GetReplicationsAsync(CancellationToken token = default (CancellationToken));

		Task SaveReplicationsAsync(CountersReplicationDocument newReplicationDocument, CancellationToken token = default(CancellationToken));

		Task<long> GetLastEtag(string serverId, CancellationToken token = default(CancellationToken));

		void Initialize(bool ensureDefaultCounterExists = false);

		/// <summary>
		/// Subscribe to change notifications from the server
		/// </summary>
		ICountersChanges Changes(string counterStorage = null);
	}
}
