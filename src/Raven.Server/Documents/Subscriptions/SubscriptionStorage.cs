﻿// -----------------------------------------------------------------------
//  <copyright file="SubscriptionStorage.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// ----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Documents.Subscriptions;
using Raven.Client.Json.Serialization;
using Raven.Client.ServerWide;
using Raven.Client.Util;
using Raven.Server.Documents.Subscriptions.Stats;
using Raven.Server.Documents.TcpHandlers;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Commands.Subscriptions;
using Raven.Server.ServerWide.Context;
using Sparrow.Logging;

namespace Raven.Server.Documents.Subscriptions
{
    public class SubscriptionStorage : AbstractSubscriptionStorage<SubscriptionConnectionsState>
    {
        internal readonly DocumentDatabase _db;

        public event Action<string> OnAddTask;
        public event Action<string> OnRemoveTask;
        public event Action<SubscriptionConnection> OnEndConnection;
        public event Action<string, SubscriptionBatchStatsAggregator> OnEndBatch;

        public SubscriptionStorage(DocumentDatabase db, ServerStore serverStore, string name) : base(serverStore, db.Configuration.Subscriptions.MaxNumberOfConcurrentConnections)
        {
            _db = db;
            _databaseName = name; // this is full name for sharded db 
            _logger = LoggingSource.Instance.GetLogger<SubscriptionStorage>(name);
        }

        protected override void DropSubscriptionConnections(SubscriptionConnectionsState state, SubscriptionException ex)
        {
            foreach (var subscriptionConnection in state.GetConnections())
            {
                state.DropSingleConnection(subscriptionConnection, ex);
            }
        }

        public async Task<(long Index, long SubscriptionId)> PutSubscription(SubscriptionCreationOptions options, string raftRequestId, long? subscriptionId = null, bool? disabled = false, string mentor = null)
        {
            var command = new PutSubscriptionCommand(_databaseName, options.Query, mentor, raftRequestId)
            {
                InitialChangeVector = options.ChangeVector,
                SubscriptionName = options.Name,
                SubscriptionId = subscriptionId,
                PinToMentorNode = options.PinToMentorNode,
                Disabled = disabled ?? false
            };

            var (etag, _) = await _serverStore.SendToLeaderAsync(command);

            if (_logger.IsInfoEnabled)
                _logger.Info($"New Subscription with index {etag} was created");

            await _db.RachisLogIndexNotifications.WaitForIndexNotification(etag, _serverStore.Engine.OperationTimeout);

            if (subscriptionId != null)
            {
                // updated existing subscription
                return (etag, subscriptionId.Value);
            }

            _db.SubscriptionStorage.RaiseNotificationForTaskAdded(options.Name);

            return (etag, etag);
        }

        public SubscriptionState GetSubscriptionFromServerStore(string name)
        {
            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                return _serverStore.Cluster.Subscriptions.ReadSubscriptionStateByName(context, _databaseName, name);
            }
        }

        public IEnumerable<SubscriptionState> GetAllSubscriptionsFromServerStore(TransactionOperationContext context)
        {
            foreach (var state in SubscriptionsClusterStorage.GetAllSubscriptionsWithoutState(context, _databaseName, 0, int.MaxValue))
                yield return state;
        }

        public SubscriptionState GetSubscriptionFromServerStoreById(long id)
        {
            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext serverStoreContext))
            using (serverStoreContext.OpenReadTransaction())
            {
                var subscriptionState = _serverStore.Cluster.Subscriptions.ReadSubscriptionStateById(serverStoreContext, _databaseName, id);
                return subscriptionState;
            }
        }

        public string GetResponsibleNode(TransactionOperationContext serverContext, string name)
        {
            var subscription = _serverStore.Cluster.Subscriptions.ReadSubscriptionStateByName(serverContext, _databaseName, name);
            var topology = _serverStore.Cluster.ReadDatabaseTopology(serverContext, _db.Name);
            return _db.WhoseTaskIsIt(topology, subscription, subscription);
        }

        public static async Task DeleteSubscriptionInternal(ServerStore serverStore, string databaseName, string name, string raftRequestId, Logger logger)
        {
            var command = new DeleteSubscriptionCommand(databaseName, name, raftRequestId);
            var (etag, _) = await serverStore.SendToLeaderAsync(command);
            await serverStore.Cluster.WaitForIndexNotification(etag, serverStore.Engine.OperationTimeout);
            if (logger.IsInfoEnabled)
            {
                logger.Info($"Subscription with name {name} was deleted");
            }
        }

        protected override void SetConnectionException(SubscriptionConnectionsState state, SubscriptionException ex)
        {
            foreach (var connection in state.GetConnections())
            {
                // this is just to set appropriate exception, the connections will be dropped on state dispose
                connection.ConnectionException = ex;
            }
        }

        protected override string GetSubscriptionResponsibleNode(DatabaseRecord databaseRecord, SubscriptionState taskStatus)
        {
            return  _serverStore.WhoseTaskIsIt(databaseRecord.Topology, taskStatus, taskStatus);
        }

        protected override bool SubscriptionChangeVectorHasChanges(SubscriptionConnectionsState state, SubscriptionState taskStatus)
        {
            return taskStatus.LastClientConnectionTime == null &&
                   taskStatus.ChangeVectorForNextBatchStartingPoint != state.LastChangeVectorSent;
        }

        public bool DropSingleSubscriptionConnection(long subscriptionId, string workerId, SubscriptionException ex)
        {
            if (_subscriptions.TryGetValue(subscriptionId, out SubscriptionConnectionsState subscriptionConnectionsState) == false)
                return false;

            var connectionToDrop = subscriptionConnectionsState.GetConnections().FirstOrDefault(conn => conn.WorkerId == workerId);
            if (connectionToDrop == null)
                return false;

            subscriptionConnectionsState.DropSingleConnection(connectionToDrop, ex);
            if (_logger.IsInfoEnabled)
                _logger.Info(
                    $"Connection with id {workerId} in subscription with id '{subscriptionId}' and name '{subscriptionConnectionsState.SubscriptionName}' was dropped.", ex);

            return true;
        }

        public IEnumerable<SubscriptionGeneralDataAndStats> GetAllSubscriptions<TRavenTransaction>(TransactionOperationContext<TRavenTransaction> serverStoreContext, bool history, int start, int take) where TRavenTransaction : RavenTransaction
        {
            foreach (var keyValue in ClusterStateMachine.ReadValuesStartingWith(serverStoreContext, SubscriptionState.SubscriptionPrefix(_databaseName)))
            {
                if (start > 0)
                {
                    start--;
                    continue;
                }

                if (take-- <= 0)
                    yield break;

                var subscriptionState = JsonDeserializationClient.SubscriptionState(keyValue.Value);
                var subscriptionConnectionsState = GetSubscriptionConnectionsState(serverStoreContext, subscriptionState.SubscriptionName);

                var subscriptionGeneralData = new SubscriptionGeneralDataAndStats(subscriptionState)
                {
                    Connections = subscriptionConnectionsState?.GetConnections(),
                    RecentConnections = subscriptionConnectionsState?.RecentConnections,
                    RecentRejectedConnections = subscriptionConnectionsState?.RecentRejectedConnections
                };
                GetSubscriptionInternal(subscriptionGeneralData, history);
                yield return subscriptionGeneralData;
            }
        }

        public string GetSubscriptionNameById(TransactionOperationContext serverStoreContext, long id)
        {
            foreach (var keyValue in ClusterStateMachine.ReadValuesStartingWith(serverStoreContext,
                SubscriptionState.SubscriptionPrefix(_databaseName)))
            {
                if (keyValue.Value.TryGet(nameof(SubscriptionState.SubscriptionId), out long _id) == false)
                    continue;
                if (_id == id)
                {
                    if (keyValue.Value.TryGet(nameof(SubscriptionState.SubscriptionName), out string name))
                        return name;
                }
            }

            return null;
        }

        public SubscriptionConnectionsState GetSubscriptionStateById(long id) => _subscriptions[id];

        public IEnumerable<SubscriptionGeneralDataAndStats> GetAllRunningSubscriptions(TransactionOperationContext context, bool history, int start, int take)
        {
            foreach (var kvp in _subscriptions)
            {
                var subscriptionConnectionsState = kvp.Value;

                if (subscriptionConnectionsState.IsSubscriptionActive() == false)
                    continue;

                if (start > 0)
                {
                    start--;
                    continue;
                }

                if (take-- <= 0)
                    yield break;

                var subscriptionData = GetSubscriptionFromServerStore(context, subscriptionConnectionsState.SubscriptionName);
                GetRunningSubscriptionInternal(history, subscriptionData, subscriptionConnectionsState);
                yield return subscriptionData;
            }
        }

        public int GetNumberOfRunningSubscriptions()
        {
            var c = 0;
            foreach ((_, SubscriptionConnectionsState value) in _subscriptions)
            {
                if (value.IsSubscriptionActive() == false)
                    continue;

                c++;
            }

            return c;
        }

        public SubscriptionGeneralDataAndStats GetSubscription(TransactionOperationContext context, long? id, string name, bool history)
        {
            SubscriptionGeneralDataAndStats subscription;

            if (string.IsNullOrEmpty(name) == false)
            {
                subscription = GetSubscriptionFromServerStore(context, name);
            }
            else if (id.HasValue)
            {
                subscription = GetSubscriptionFromServerStore(context, id.ToString());
            }
            else
            {
                throw new ArgumentNullException("Must receive either subscription id or subscription name in order to provide subscription data");
            }

            GetSubscriptionInternal(subscription, history);

            return subscription;
        }

        public SubscriptionGeneralDataAndStats GetSubscriptionFromServerStore(TransactionOperationContext context, string name)
        {
            var subscriptionBlittable = _serverStore.Cluster.Read(context, SubscriptionState.GenerateSubscriptionItemKeyName(_databaseName, name));

            if (subscriptionBlittable == null)
                throw new SubscriptionDoesNotExistException($"Subscription with name '{name}' was not found in server store");

            var subscriptionState = JsonDeserializationClient.SubscriptionState(subscriptionBlittable);
            var subscriptionConnectionsState = GetSubscriptionConnectionsState(context, subscriptionState.SubscriptionName);

            var subscriptionJsonValue = new SubscriptionGeneralDataAndStats(subscriptionState)
            {
                Connections = subscriptionConnectionsState?.GetConnections(),
                RecentConnections = subscriptionConnectionsState?.RecentConnections,
                RecentRejectedConnections = subscriptionConnectionsState?.RecentRejectedConnections
            };
            return subscriptionJsonValue;
        }

        public SubscriptionGeneralDataAndStats GetRunningSubscription(TransactionOperationContext context, long? id, string name, bool history)
        {
            SubscriptionGeneralDataAndStats subscription;
            if (string.IsNullOrEmpty(name) == false)
            {
                subscription = GetSubscriptionFromServerStore(context, name);
            }
            else if (id.HasValue)
            {
                name = GetSubscriptionNameById(context, id.Value);
                subscription = GetSubscriptionFromServerStore(context, name);
            }
            else
            {
                throw new ArgumentNullException("Must receive either subscription id or subscription name in order to provide subscription data");
            }

            if (_subscriptions.TryGetValue(subscription.SubscriptionId, out SubscriptionConnectionsState subscriptionConnectionsState) == false)
                return null;

            if (subscriptionConnectionsState.IsSubscriptionActive() == false)
                return null;

            GetRunningSubscriptionInternal(history, subscription, subscriptionConnectionsState);
            return subscription;
        }

        public bool TryGetRunningSubscriptionConnectionsState(long subscriptionId, out SubscriptionConnectionsState connections)
        {
            connections = null;

            if (_subscriptions.TryGetValue(subscriptionId, out var concurrentSubscription) == false)
                return false;

            if (concurrentSubscription == null)
                return false;

            connections = concurrentSubscription;

            return true;
        }

        public class SubscriptionGeneralDataAndStats : SubscriptionState
        {
            public SubscriptionConnection Connection => Connections?.FirstOrDefault();
            public List<SubscriptionConnection> Connections;
            public IEnumerable<SubscriptionConnection> RecentConnections;
            public IEnumerable<SubscriptionConnection> RecentRejectedConnections;

            public SubscriptionGeneralDataAndStats() { }

            public SubscriptionGeneralDataAndStats(SubscriptionState @base)
            {
                Query = @base.Query;
                ChangeVectorForNextBatchStartingPoint = @base.ChangeVectorForNextBatchStartingPoint;
                SubscriptionId = @base.SubscriptionId;
                SubscriptionName = @base.SubscriptionName;
                MentorNode = @base.MentorNode;
                PinToMentorNode = @base.PinToMentorNode;
                NodeTag = @base.NodeTag;
                LastBatchAckTime = @base.LastBatchAckTime;
                LastClientConnectionTime = @base.LastClientConnectionTime;
                Disabled = @base.Disabled;
                ShardingState = @base.ShardingState;
            }
        }

        public long GetRunningCount()
        {
            return _subscriptions.Count(x => x.Value.IsSubscriptionActive());
        }

        public long GetAllSubscriptionsCount()
        {
            using (_serverStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                return ClusterStateMachine.ReadValuesStartingWith(context, SubscriptionState.SubscriptionPrefix(_databaseName))
                    .Count();
            }
        }

        private static void SetSubscriptionHistory(SubscriptionConnectionsState subscriptionConnectionsState, SubscriptionGeneralDataAndStats subscriptionData)
        {
            subscriptionData.RecentConnections = subscriptionConnectionsState.RecentConnections;
            subscriptionData.RecentRejectedConnections = subscriptionConnectionsState.RecentRejectedConnections;
        }

        private static void GetRunningSubscriptionInternal(bool history, SubscriptionGeneralDataAndStats subscriptionData, SubscriptionConnectionsState subscriptionConnectionsState)
        {
            subscriptionData.Connections = subscriptionConnectionsState.GetConnections();
            if (history) // Only valid for this node
                SetSubscriptionHistory(subscriptionConnectionsState, subscriptionData);
        }

        private void GetSubscriptionInternal(SubscriptionGeneralDataAndStats subscriptionData, bool history)
        {
            if (_subscriptions.TryGetValue(subscriptionData.SubscriptionId, out SubscriptionConnectionsState concurrentSubscription))
            {
                subscriptionData.Connections = concurrentSubscription.GetConnections();

                if (history)//Only valid if this is my subscription
                    SetSubscriptionHistory(concurrentSubscription, subscriptionData);
            }
        }

        internal virtual void CleanupSubscriptions()
        {
            var maxTaskLifeTime = _db.Is32Bits ? TimeSpan.FromHours(12) : TimeSpan.FromDays(2);
            var oldestPossibleIdleSubscription = SystemTime.UtcNow - maxTaskLifeTime;

            foreach (var kvp in _subscriptions)
            {
                if (kvp.Value.IsSubscriptionActive())
                    continue;

                var recentConnection = kvp.Value.MostRecentEndedConnection();

                if (recentConnection != null && recentConnection.Stats.Metrics.LastMessageSentAt < oldestPossibleIdleSubscription)
                {
                    if (_subscriptions.TryRemove(kvp.Key, out var subsState))
                    {
                        subsState.Dispose();
                    }
                }
            }
        }

        public void RaiseNotificationForTaskAdded(string subscriptionName)
        {
            OnAddTask?.Invoke(subscriptionName);
        }

        public void RaiseNotificationForTaskRemoved(string subscriptionName)
        {
            OnRemoveTask?.Invoke(subscriptionName);
        }

        public void RaiseNotificationForConnectionEnded(SubscriptionConnection connection)
        {
            OnEndConnection?.Invoke(connection);
        }

        public void RaiseNotificationForBatchEnded(string subscriptionName, SubscriptionBatchStatsAggregator batchAggregator)
        {
            OnEndBatch?.Invoke(subscriptionName, batchAggregator);
        }
    }
}
