﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.Counters;
using Raven.Client.Documents.Smuggler;
using Raven.Client.Util;
using Raven.Server.Documents;
using Raven.Server.Documents.PeriodicBackup;
using Raven.Server.Documents.ShardedHandlers;
using Raven.Server.Documents.ShardedHandlers.ShardedCommands;
using Raven.Server.Documents.Sharding;
using Raven.Server.Smuggler.Documents.Data;
using Raven.Server.Utils;
using Sparrow.Json;
using Sparrow.Server;
using Sparrow.Server.Utils;
using Sparrow.Threading;

namespace Raven.Server.Smuggler.Documents
{
    public class MultiShardedDestination : ISmugglerDestination
    {
        private readonly ShardedContext _shardedContext;
        private readonly ShardedSmugglerHandler _handler;
        private readonly ISmugglerSource _source;
        private readonly List<StreamDestination> _destinations;
        private DatabaseSmugglerOptionsServerSide _options;

        public MultiShardedDestination(ISmugglerSource source, ShardedContext shardedContext, ShardedSmugglerHandler handler)
        {
            _source = source;
            _shardedContext = shardedContext;
            _handler = handler;
            _destinations = new List<StreamDestination>();
        }

        public IAsyncDisposable InitializeAsync(DatabaseSmugglerOptionsServerSide options, SmugglerResult result, long buildVersion)
        {
            _options = options;
            var streams = new List<Stream>();
            var disposables = new List<IDisposable>();
            var destinationsDispose = new List<IAsyncDisposable>();

            for (int i = 0; i < _shardedContext.ShardCount; i++)
            {
                streams.Add(_handler.GetOutputStream(new BeatingBlockingStream(), options));
                disposables.Add(_handler.ContextPool.AllocateOperationContext(out JsonOperationContext context));
                var destination = new StreamDestinationShardImport(streams[i], context, _source);
                _destinations.Add(destination);
                destinationsDispose.Add(destination.InitializeAsync(options, result, buildVersion));
            }

            var importOperation = new ShardedImportOperation(_handler, streams, options);

            var t = _shardedContext.ShardExecutor.ExecuteParallelForAllAsync(importOperation);

            return new AsyncDisposableAction(async () =>
            {
                foreach (var asyncDisposable in destinationsDispose)
                {
                    await asyncDisposable.DisposeAsync();
                }

                await t;

                foreach (var disposable in disposables)
                {
                    disposable.Dispose();
                }
            });
        }

        // All the NotImplementedException methods are handled on the smuggler level, since they are cluster wide and do no require any specific database
        public IDatabaseRecordActions DatabaseRecord() => throw new NotImplementedException();
        public IIndexActions Indexes() => throw new NotImplementedException();
        public IKeyValueActions<long> Identities() => throw new NotImplementedException();
        public ISubscriptionActions Subscriptions() => throw new NotImplementedException();
        public IReplicationHubCertificateActions ReplicationHubCertificates() => throw new NotImplementedException();

        public ICompareExchangeActions CompareExchange(JsonOperationContext context) => 
            new ShardedCompareExchangeActions(_shardedContext, _destinations.Select(d=>d.CompareExchange(context)).ToArray(), _options);

        public ICompareExchangeActions CompareExchangeTombstones(JsonOperationContext context) => 
            new ShardedCompareExchangeActions(_shardedContext, _destinations.Select(d=>d.CompareExchange(context)).ToArray(), _options);

        public IDocumentActions Documents(bool throwOnCollectionMismatchError = true) =>
            new SharededDocumentActions(_shardedContext, _destinations.Select(d => d.Documents(throwOnDuplicateCollection: false)).ToArray(), _options);

        public IDocumentActions RevisionDocuments() =>
            new SharededDocumentActions(_shardedContext, _destinations.Select(d => d.RevisionDocuments()).ToArray(), _options);

        public IDocumentActions Tombstones() =>
            new SharededDocumentActions(_shardedContext, _destinations.Select(d => d.Tombstones()).ToArray(), _options);

        public IDocumentActions Conflicts() =>
            new SharededDocumentActions(_shardedContext, _destinations.Select(d => d.Conflicts()).ToArray(), _options);

        public ICounterActions Counters(SmugglerResult result) =>
            new ShardedCounterActions(_shardedContext, _destinations.Select(d => d.Counters(result)).ToArray(), _options);

        public ICounterActions LegacyCounters(SmugglerResult result) =>
            new ShardedCounterActions(_shardedContext, _destinations.Select(d => d.LegacyCounters(result)).ToArray(), _options);

        public ITimeSeriesActions TimeSeries() =>
            new ShardedTimeSeriesActions(_shardedContext, _destinations.Select(d => d.TimeSeries()).ToArray(), _options);

        public ILegacyActions LegacyDocumentDeletions() =>
            new ShardedLegacyActions(_shardedContext, _destinations.Select(d => d.LegacyDocumentDeletions()).ToArray(), _options);

        public ILegacyActions LegacyAttachmentDeletions() =>
            new ShardedLegacyActions(_shardedContext, _destinations.Select(d => d.LegacyAttachmentDeletions()).ToArray(), _options);


        public abstract class ShardedActions<T> : INewDocumentActions, INewCompareExchangeActions where T : IAsyncDisposable
        {
            private JsonOperationContext _context;
            private readonly IDisposable _rtnCtx;
            private readonly DatabaseSmugglerOptionsServerSide _options;
            protected readonly ShardedContext _shardedContext;
            protected readonly T[] _actions;

            protected ShardedActions(ShardedContext shardedContext, T[] actions, DatabaseSmugglerOptionsServerSide options)
            {
                _shardedContext = shardedContext;
                _actions = actions;
                _options = options;
                _rtnCtx = _shardedContext.AllocateContext(out _context);
            }

            public JsonOperationContext GetContextForNewCompareExchangeValue() => _context;
            public JsonOperationContext GetContextForNewDocument() => _context;

            public virtual async ValueTask DisposeAsync()
            {
                foreach (var action in _actions)
                {
                    await action.DisposeAsync();
                }

                _rtnCtx.Dispose();
            }

            public Stream GetTempStream() => StreamDestination.GetTempStream(_options);
        }
        
        private class ShardedCompareExchangeActions : ShardedActions<ICompareExchangeActions>, ICompareExchangeActions 
        {
            public ShardedCompareExchangeActions(ShardedContext shardedContext, ICompareExchangeActions[] actions, DatabaseSmugglerOptionsServerSide options) : base(shardedContext, actions, options)
            {
            }

            public async ValueTask WriteKeyValueAsync(string key, BlittableJsonReaderObject value)
            {
                var bucket = ShardHelper.GetBucket(key);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteKeyValueAsync(key, value);
            }

            public async ValueTask WriteTombstoneKeyAsync(string key)
            {
                var bucket = ShardHelper.GetBucket(key);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteTombstoneKeyAsync(key);
            }
        }


        private class SharededDocumentActions : ShardedActions<IDocumentActions>, IDocumentActions 
        {
            private readonly ByteStringContext _allocator;

            public SharededDocumentActions(ShardedContext shardedContext, IDocumentActions[] actions, DatabaseSmugglerOptionsServerSide options) : base(shardedContext, actions, options)
            {
                _allocator = new ByteStringContext(SharedMultipleUseFlag.None);
            }

            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync();
                _allocator.Dispose();
            }

            public async ValueTask WriteDocumentAsync(DocumentItem item, SmugglerProgressBase.CountsWithLastEtagAndAttachments progress)
            {
                var bucket = ShardHelper.GetBucket(_allocator, item.Document.Id);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteDocumentAsync(item, progress);
            }

            public async ValueTask WriteTombstoneAsync(Tombstone tombstone, SmugglerProgressBase.CountsWithLastEtag progress)
            {
                var bucket = ShardHelper.GetBucket(_allocator, tombstone.LowerId);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteTombstoneAsync(tombstone, progress);
            }

            public async ValueTask WriteConflictAsync(DocumentConflict conflict, SmugglerProgressBase.CountsWithLastEtag progress)
            {
                var bucket = ShardHelper.GetBucket(_allocator, conflict.Id);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteConflictAsync(conflict, progress);
            }

            public ValueTask DeleteDocumentAsync(string id) => ValueTask.CompletedTask;

            public IEnumerable<DocumentItem> GetDocumentsWithDuplicateCollection()
            {
                yield break;
            }
        }

        private class ShardedCounterActions : ShardedActions<ICounterActions>, ICounterActions
        {
            private readonly ByteStringContext _allocator;

            public ShardedCounterActions(ShardedContext shardedContext, ICounterActions[] actions, DatabaseSmugglerOptionsServerSide options) : base(shardedContext,actions, options)
            {
                _allocator = new ByteStringContext(SharedMultipleUseFlag.None);
            }

            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync();
                _allocator.Dispose();
            }

            public async ValueTask WriteCounterAsync(CounterGroupDetail counterDetail)
            {
                var bucket = ShardHelper.GetBucket(_allocator, counterDetail.DocumentId);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteCounterAsync(counterDetail);
            }

            public async ValueTask WriteLegacyCounterAsync(CounterDetail counterDetail)
            {
                var bucket = ShardHelper.GetBucket(counterDetail.DocumentId);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteLegacyCounterAsync(counterDetail);
            }

            public void RegisterForDisposal(IDisposable data)
            {
            }
        }

        private class ShardedTimeSeriesActions : ShardedActions<ITimeSeriesActions>, ITimeSeriesActions
        {
            public ShardedTimeSeriesActions(ShardedContext shardedContext, ITimeSeriesActions[] actions, DatabaseSmugglerOptionsServerSide options) : base(shardedContext, actions, options)
            {
            }

            public async ValueTask WriteTimeSeriesAsync(TimeSeriesItem ts)
            {
                var bucket = ShardHelper.GetBucket(ts.DocId);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteTimeSeriesAsync(ts);
            }
        }

        private class ShardedLegacyActions : ShardedActions<ILegacyActions>, ILegacyActions
        {
            public ShardedLegacyActions(ShardedContext shardedContext, ILegacyActions[] actions, DatabaseSmugglerOptionsServerSide options) : base(shardedContext,actions, options)
            {
            }

            public async ValueTask WriteLegacyDeletions(string id)
            {
                var bucket = ShardHelper.GetBucket(id);
                var index = _shardedContext.GetShardIndex(bucket);
                await _actions[index].WriteLegacyDeletions(id);
            }
        }

        public class StreamDestinationShardImport : StreamDestination
        {
            private readonly Stream _stream;

            public StreamDestinationShardImport(Stream stream, JsonOperationContext context, ISmugglerSource source) : base(stream, context, source)
            {
                _stream = stream;
            }

            public override IAsyncDisposable InitializeAsync(DatabaseSmugglerOptionsServerSide options, SmugglerResult result, long buildVersion)
            {
                var dispose = base.InitializeAsync(options, result, buildVersion);
                return new AsyncDisposableAction(async () =>
                {
                    await dispose.DisposeAsync();
                    await _stream.DisposeAsync();
                });
            }
        }

    }
}
