﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.Counters;
using Raven.Client.Documents.Operations.ETL;
using Raven.Client.Http;
using Raven.Client.Util;
using Raven.Server.Documents.ETL.Metrics;
using Raven.Server.Documents.ETL.Providers.Raven.Enumerators;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Documents.ETL.Providers.Raven
{
    public class RavenEtl : EtlProcess<RavenEtlItem, ICommandData, RavenEtlConfiguration, RavenConnectionString>
    {
        public const string RavenEtlTag = "Raven ETL";

        private readonly RequestExecutor _requestExecutor;
        private string _recentUrl;
        public string Url => _recentUrl;

        private readonly RavenEtlDocumentTransformer.ScriptInput _script;

        public RavenEtl(Transformation transformation, RavenEtlConfiguration configuration, DocumentDatabase database, ServerStore serverStore) : base(transformation, configuration, database, serverStore, RavenEtlTag)
        {
            Metrics = new EtlMetricsCountersManager();
            _requestExecutor = RequestExecutor.Create(configuration.Connection.TopologyDiscoveryUrls, configuration.Connection.Database, serverStore.Server.Certificate.Certificate, DocumentConventions.Default);
            _script = new RavenEtlDocumentTransformer.ScriptInput(transformation);            
        }

        protected override IEnumerator<RavenEtlItem> ConvertDocsEnumerator(IEnumerator<Document> docs, string collection)
        {
            return new DocumentsToRavenEtlItems(docs, collection);
        }

        protected override IEnumerator<RavenEtlItem> ConvertTombstonesEnumerator(IEnumerator<Tombstone> tombstones, string collection, EtlItemType type)
        {
            return new TombstonesToRavenEtlItems(tombstones, collection, type);
        }

        protected override IEnumerator<RavenEtlItem> ConvertCountersEnumerator(IEnumerator<CounterDetail> counters, string collection)
        {
            return new CountersToRavenEtlItems(counters, collection);
        }

        protected override bool ShouldTrackAttachmentTombstones()
        {
            // if script isn't empty we relay on addAttachment() calls and detect that attachments needs be deleted
            // when this call gets an attachment reference marked as null ($attachment/{attachment-name}/$null)

            return string.IsNullOrEmpty(Transformation.Script);
        }

        protected override bool ShouldTrackCounters()
        {
            // we track counters only if script is empty (then we send all counters together with documents) or
            // when load counter behavior functions are defined, otherwise counters are send on document updates
            // when addCounter() is called during transformation

            return string.IsNullOrEmpty(Transformation.Script) || Transformation.CollectionToLoadCounterBehaviorFunction != null;
        }

        protected override EtlTransformer<RavenEtlItem, ICommandData> GetTransformer(DocumentsOperationContext context)
        {
            return new RavenEtlDocumentTransformer(Transformation, Database, context, _script);
        }

        protected override void LoadInternal(IEnumerable<ICommandData> items, JsonOperationContext context)
        {
            var commands = items as List<ICommandData>;

            Debug.Assert(commands != null);

            if (commands.Count == 0)
                return;

            BatchOptions options = null;
            if (Configuration.LoadRequestTimeoutInSec != null)
            {
                options = new BatchOptions
                {
                    RequestTimeout = TimeSpan.FromSeconds(Configuration.LoadRequestTimeoutInSec.Value)
                };
            }

            var batchCommand = new BatchCommand(DocumentConventions.Default, context, commands, options);
            
            try
            {
                AsyncHelpers.RunSync(() => _requestExecutor.ExecuteAsync(batchCommand, context, token: CancellationToken));
                _recentUrl = _requestExecutor.Url;
            }
            catch (OperationCanceledException e)
            {
                if (CancellationToken.IsCancellationRequested == false)
                {
                    ThrowTimeoutException(commands.Count, e);
                }

                throw;
            }
        }

        protected override bool ShouldFilterOutHiLoDocument()
        {
            // if we transfer all documents to the same collections (no script specified) then don't exclude HiLo docs
            return string.IsNullOrEmpty(Transformation.Script) == false;
        }

        private static void ThrowTimeoutException(int numberOfCommands, Exception e)
        {
            var message = $"Load request applying {numberOfCommands} commands timed out.";

            throw new TimeoutException(message, e);
        }

        public override void Dispose()
        {
            base.Dispose();
            _requestExecutor?.Dispose();
        }
    }
}
