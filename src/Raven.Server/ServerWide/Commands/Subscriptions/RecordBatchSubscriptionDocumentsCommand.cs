﻿using System.Collections.Generic;
using Raven.Client;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Exceptions.Database;
using Raven.Client.Exceptions.Documents.Subscriptions;
using Raven.Client.Json.Serialization;
using Raven.Client.ServerWide;
using Raven.Server.Documents.Subscriptions;
using Raven.Server.Rachis;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow.Binary;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Utils;
using Voron;
using Voron.Data.Tables;

namespace Raven.Server.ServerWide.Commands.Subscriptions
{
    public class RecordBatchSubscriptionDocumentsCommand :  UpdateValueForDatabaseCommand
    {
        public long SubscriptionId;
        public string SubscriptionName;
        public string CurrentChangeVector;
        public string PreviouslyRecordedChangeVector;
        public string NodeTag;
        public bool HasHighlyAvailableTasks;
        public List<DocumentRecord> Documents;
        public List<string> Deleted;
        public List<RevisionRecord> Revisions;
        public string ShardName;

        public RecordBatchSubscriptionDocumentsCommand()
        {
        }

        public RecordBatchSubscriptionDocumentsCommand(string databaseName, long subscriptionId, string subscriptionName, List<DocumentRecord> documents, string previouslyRecordedChangeVector, string currentChangeVector, string nodeTag, bool hasHighlyAvailableTasks, string uniqueRaftId) : base(databaseName, uniqueRaftId)
        {
            SubscriptionId = subscriptionId;
            SubscriptionName = subscriptionName;
            Documents = documents;
            CurrentChangeVector = currentChangeVector;
            PreviouslyRecordedChangeVector = previouslyRecordedChangeVector;
            NodeTag = nodeTag;
            HasHighlyAvailableTasks = hasHighlyAvailableTasks;
        }

        public RecordBatchSubscriptionDocumentsCommand(string databaseName, long subscriptionId, string subscriptionName, List<RevisionRecord> revisions, string previouslyRecordedChangeVector, string currentChangeVector, string nodeTag, bool hasHighlyAvailableTasks, string uniqueRaftId) : base(databaseName, uniqueRaftId)
        {
            SubscriptionId = subscriptionId;
            SubscriptionName = subscriptionName;
            Revisions = revisions;
            CurrentChangeVector = currentChangeVector;
            PreviouslyRecordedChangeVector = previouslyRecordedChangeVector;
            NodeTag = nodeTag;
            HasHighlyAvailableTasks = hasHighlyAvailableTasks;
        }

        public override unsafe void Execute(ClusterOperationContext context, Table items, long index, RawDatabaseRecord record, RachisState state, out object result)
        {
            result = null;
            var shouldUpdateChangeVector = true;
            var subscriptionName = SubscriptionName;
            if (string.IsNullOrEmpty(subscriptionName))
            {
                subscriptionName = SubscriptionId.ToString();
            }

            //insert all docs to voron table. If exists, then batchId will be replaced
            var subscriptionStateTable = context.Transaction.InnerTransaction.OpenTable(ClusterStateMachine.SubscriptionStateSchema, ClusterStateMachine.SubscriptionState);
            
            var itemKey = SubscriptionState.GenerateSubscriptionItemKeyName(DatabaseName, subscriptionName);
            using (Slice.From(context.Allocator, itemKey.ToLowerInvariant(), out Slice valueNameLowered))
            using (Slice.From(context.Allocator, itemKey, out Slice valueName))
            {
                if (items.ReadByKey(valueNameLowered, out var tvr) == false)
                {
                    throw new RachisApplyException($"Cannot find subscription {subscriptionName} @ {DatabaseName}");
                }

                var ptr = tvr.Read(2, out int size);
                var existingValue = new BlittableJsonReaderObject(ptr, size, context);

                if (existingValue == null)
                    throw new SubscriptionDoesNotExistException($"Subscription with name '{subscriptionName}' does not exist in database '{DatabaseName}'");

                var subscriptionState = JsonDeserializationClient.SubscriptionState(existingValue);

                DevelopmentHelper.ShardingToDo(DevelopmentHelper.TeamMember.Karmel, DevelopmentHelper.Severity.Normal, "create subscription WhosTaskIsIt");
                DevelopmentHelper.ShardingToDo(DevelopmentHelper.TeamMember.Karmel, DevelopmentHelper.Severity.Normal, "Need to handle NodeTag, currently is isn't used for sharded because it is shared");

                var topology = string.IsNullOrEmpty(ShardName) ? record.TopologyForSubscriptions() : record.Shards[ShardHelper.GetShardNumber(ShardName)];
                var lastResponsibleNode = string.IsNullOrEmpty(ShardName)
                    ? AcknowledgeSubscriptionBatchCommand.GetLastResponsibleNode(HasHighlyAvailableTasks, topology, NodeTag)
                    : null;

                var appropriateNode = topology.WhoseTaskIsIt(RachisState.Follower, subscriptionState, lastResponsibleNode);
                if (appropriateNode == null && record.DeletionInProgress.ContainsKey(NodeTag))
                    throw new DatabaseDoesNotExistException($"Stopping subscription '{subscriptionName}' on node {NodeTag}, because database '{DatabaseName}' is being deleted.");

                if (appropriateNode != NodeTag)
                {
                    throw new SubscriptionDoesNotBelongToNodeException(
                            $"Cannot apply {nameof(AcknowledgeSubscriptionBatchCommand)} for subscription '{subscriptionName}' with id '{SubscriptionId}', on database '{DatabaseName}', on node '{NodeTag}'," +
                            $" because the subscription task belongs to '{appropriateNode ?? "N/A"}'.")
                        { AppropriateNode = appropriateNode };
                }

                if (CurrentChangeVector == nameof(Constants.Documents.SubscriptionChangeVectorSpecialStates.DoNotChange))
                {
                    context.ReadObject(existingValue, subscriptionName);
                    shouldUpdateChangeVector = false;
                }

                CheckConcurrencyForBatchCv(subscriptionState, subscriptionName);

                if (shouldUpdateChangeVector)
                {
                    UpdateChangeVector(subscriptionState);

                    subscriptionState.NodeTag = NodeTag;
                    using (var obj = context.ReadObject(subscriptionState.ToJson(), "subscription"))
                    {
                        ClusterStateMachine.UpdateValue(index, items, valueNameLowered, valueName, obj);
                    }
                }
            }

            foreach (var deletedId in Deleted)
            {
                using (SubscriptionConnectionsState.GetDatabaseAndSubscriptionAndDocumentKey(context, DatabaseName, SubscriptionId, deletedId, out var key))
                {
                    using var _ = Slice.External(context.Allocator, key, out var keySlice);
                    subscriptionStateTable.DeleteByKey(keySlice);
                }
            }

            foreach (var documentRecord in Documents)
            {
                using (SubscriptionConnectionsState.GetDatabaseAndSubscriptionAndDocumentKey(context, DatabaseName, SubscriptionId, documentRecord.DocumentId, out var key))
                using (subscriptionStateTable.Allocate(out var tvb))
                {
                    using var _ = Slice.External(context.Allocator, key, out var keySlice);
                    using var __ = Slice.From(context.Allocator, documentRecord.ChangeVector, out var changeVectorSlice);

                    tvb.Add(keySlice);
                    tvb.Add(changeVectorSlice);
                    tvb.Add(Bits.SwapBytes(index)); // batch id

                    subscriptionStateTable.Set(tvb);
                }
            }
            foreach (var revisionRecord in Revisions)
            {
                using (SubscriptionConnectionsState.GetDatabaseAndSubscriptionAndRevisionKey(context, DatabaseName, SubscriptionId, revisionRecord.Current, out var key))
                using (subscriptionStateTable.Allocate(out var tvb))
                {
                    using var _ = Slice.External(context.Allocator, key, out var keySlice);
                    using var __ = Slice.From(context.Allocator, revisionRecord.Previous ?? string.Empty, out var changeVectorSlice);

                    tvb.Add(keySlice);
                    tvb.Add(changeVectorSlice); //prev change vector
                    tvb.Add(Bits.SwapBytes(index)); // batch id

                    subscriptionStateTable.Set(tvb);
                }
            }
        }

        private void CheckConcurrencyForBatchCv(SubscriptionState state, string subscriptionName)
        {
            if (ShardName == null)
            {
                var subscriptionStateChangeVectorForNextBatchStartingPoint = state.ChangeVectorForNextBatchStartingPoint;
                if (subscriptionStateChangeVectorForNextBatchStartingPoint != PreviouslyRecordedChangeVector)
                {
                    throw new SubscriptionChangeVectorUpdateConcurrencyException($"Can't record subscription with name '{subscriptionName}' due to inconsistency in change vector progress. Probably there was an admin intervention that changed the change vector value. Stored value: {subscriptionStateChangeVectorForNextBatchStartingPoint}, received value: {PreviouslyRecordedChangeVector}.");
                }
                return;
            }

            state.ChangeVectorForNextBatchStartingPointPerShard.TryGetValue(ShardName, out string cvInStorage);
            if (cvInStorage != PreviouslyRecordedChangeVector)
            {
                throw new SubscriptionChangeVectorUpdateConcurrencyException($"Can't record sharded subscription with name '{subscriptionName}' due to inconsistency in change vector progress. Probably there was an admin intervention that changed the change vector value. Stored value: {cvInStorage}, received value: {PreviouslyRecordedChangeVector}.");
            }
        }

        private void UpdateChangeVector(SubscriptionState subscriptionState)
        {
            if (ShardName == null)
            {
                subscriptionState.ChangeVectorForNextBatchStartingPoint =
                    ChangeVectorUtils.MergeVectors(CurrentChangeVector, subscriptionState.ChangeVectorForNextBatchStartingPoint);
                return;
            }
            if (subscriptionState.ChangeVectorForNextBatchStartingPointPerShard.TryGetValue(ShardName, out string cvInStorage))
            {
                subscriptionState.ChangeVectorForNextBatchStartingPointPerShard[ShardName] = ChangeVectorUtils.MergeVectors(cvInStorage, CurrentChangeVector);
            }
            else
            {
                subscriptionState.ChangeVectorForNextBatchStartingPointPerShard[ShardName] = CurrentChangeVector;
            }
        }

        public override string GetItemId()
        {
            throw new System.NotImplementedException();
        }

        public override void FillJson(DynamicJsonValue json)
        {
            json[nameof(SubscriptionId)] = SubscriptionId;
            json[nameof(SubscriptionName)] = SubscriptionName;
            json[nameof(CurrentChangeVector)] = CurrentChangeVector;
            json[nameof(PreviouslyRecordedChangeVector)] = PreviouslyRecordedChangeVector;
            json[nameof(NodeTag)] = NodeTag;
            json[nameof(HasHighlyAvailableTasks)] = HasHighlyAvailableTasks;
            json[nameof(ShardName)] = ShardName;
            if (Documents != null)
                json[nameof(Documents)] = new DynamicJsonArray(Documents);
            if(Revisions != null)
                json[nameof(Revisions)] = new DynamicJsonArray(Revisions);
            if(Deleted != null)
                json[nameof(Deleted)] = new DynamicJsonArray(Deleted);
        }

        protected override BlittableJsonReaderObject GetUpdatedValue(long index, RawDatabaseRecord record, JsonOperationContext context, BlittableJsonReaderObject existingValue)
        {
            throw new System.NotImplementedException();
        }
    }

    public abstract class SubscriptionRecord
    {

    }

    public class DocumentRecord : SubscriptionRecord, IDynamicJsonValueConvertible
    {
        public string DocumentId;
        public string ChangeVector;

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue()
            {
                [nameof(DocumentId)] = DocumentId,
                [nameof(ChangeVector)] = ChangeVector
            };
        }
    }

    public class RevisionRecord : SubscriptionRecord, IDynamicJsonValueConvertible
    {
        public string Previous;
        public string Current;
        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue()
            {
                [nameof(Previous)] = Previous,
                [nameof(Current)] = Current
            };
        }
    }

    public enum SubscriptionType : byte
    {
        None = 0,
        Document = 1,
        Revision = 2
    }
}
