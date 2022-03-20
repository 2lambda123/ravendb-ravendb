﻿// -----------------------------------------------------------------------
//  <copyright file="ShardedSubscriptionConnection.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// ----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Http;
using Raven.Server.Documents.Subscriptions;
using Raven.Server.Documents.TcpHandlers;
using Raven.Server.ServerWide;
using Sparrow.Json;
using Sparrow.Json.Parsing;
using Sparrow.Server;
using Sparrow.Utils;

namespace Raven.Server.Documents.ShardedTcpHandlers
{
    public class ShardedSubscriptionConnection : SubscriptionConnectionBase, IDisposable
    {
        private readonly Dictionary<string, SubscriptionShardHolder> _shardWorkers = new Dictionary<string, SubscriptionShardHolder>();
        public static ConcurrentDictionary<string, ShardedSubscriptionConnection> Connections = new ConcurrentDictionary<string, ShardedSubscriptionConnection>();
        public  AsyncManualResetEvent _mre;

        private ShardedSubscriptionConnection(ServerStore serverStore, TcpConnectionOptions tcpConnection, IDisposable tcpConnectionDisposable, JsonOperationContext.MemoryBuffer buffer)
            : base(tcpConnection, serverStore, buffer, tcpConnectionDisposable, tcpConnection.ShardedContext.DatabaseName, CancellationTokenSource.CreateLinkedTokenSource(tcpConnection.ShardedContext.DatabaseShutdown))
        {
            _mre = new AsyncManualResetEvent();
        }

       /*
        * The subscription worker (client) connects to an orchestrator (SendShardedSubscriptionDocuments). 
        * The orchestrator initializes shard subscription workers (StartShardSubscriptionWorkers).
        * ShardedSubscriptionWorkers are maintaining the connection with subscription on each shard.
        * The orchestrator maintains the connection with the client and checks if there is an available batch on each Sharded Worker (MaintainConnectionWithClientWorker).
        * Handle batch flow:
        * Orchestrator sends the batch to the client (WriteBatchToClientAndAck).
        * Orchestrator receive batch ACK request from client.
        * Orchestrator advances the sharded worker and waits for the sharded worker.
        * Sharded worker sends an ACK to the shard and waits for CONFIRM from shard (ACK command in cluster)
        * Sharded worker advances the Orchestrator
        * Orchestrator sends the CONFIRM to client
        */
        public static void SendShardedSubscriptionDocuments(ServerStore server, TcpConnectionOptions tcpConnectionOptions, JsonOperationContext.MemoryBuffer buffer)
        {
            var tcpConnectionDisposable = tcpConnectionOptions.ConnectionProcessingInProgress($"ShardedSubscription_{tcpConnectionOptions.ShardedContext.DatabaseName}");
            try
            {
                var connection = new ShardedSubscriptionConnection(server, tcpConnectionOptions, tcpConnectionDisposable, buffer);
                _ = connection.Run();
            }
            catch (Exception)
            {
                tcpConnectionDisposable?.Dispose();
                throw;
            }
        }

        public async Task Run()
        {
            try
            {
                await ParseSubscriptionOptionsAsync();
                await StartShardSubscriptionWorkers();
                await MaintainConnectionWithClientWorker();
            }
            catch (Exception e)
            {
                await ReportException(SubscriptionError.Error, ConnectionException ?? e);
            }
            finally
            {
                AddToStatusDescription(CreateStatusMessage(ConnectionStatus.Info, "Finished processing sharded subscription."));
                if (_logger.IsInfoEnabled)
                    _logger.Info($"Finished processing sharded subscription '{SubscriptionId}' / from client '{TcpConnection.TcpClient.Client.RemoteEndPoint}'.");

                Connections.TryRemove(_options.SubscriptionName, out _);
                Dispose();
            }
        }

        private new async Task ParseSubscriptionOptionsAsync()
        {
            await base.ParseSubscriptionOptionsAsync();

            // we want to limit the batch of each shard, to not hold too much memory if there are other batches while batch is proceed
            _options.MaxDocsPerBatch = Math.Min(_options.MaxDocsPerBatch / TcpConnection.ShardedContext.ShardCount, _options.MaxDocsPerBatch);
            _options.MaxDocsPerBatch = Math.Max(1, _options.MaxDocsPerBatch);

            // add to connections
            if (Connections.TryAdd(_options.SubscriptionName, this) == false)
            {
                throw new InvalidOperationException($"Sharded subscription '{_options.SubscriptionName}' on '{Database}' already exists!");
            } 
        }

        protected override async Task ReportException(SubscriptionError error, Exception e)
        {
            try
            {
                await LogExceptionAndReportToClient(ConnectionException ?? e);
            }
            catch (Exception)
            {
                // ignored
            }
        }

        protected override Task OnClientAck()
        {
            return Task.CompletedTask;
        }

        private async Task StartShardSubscriptionWorkers()
        {
            AddToStatusDescription(CreateStatusMessage(ConnectionStatus.Create));

            for (int i = 0; i < TcpConnection.ShardedContext.ShardCount; i++)
            {
                var re = TcpConnection.ShardedContext.RequestExecutors[i];
                var shard = TcpConnection.ShardedContext.GetShardedDatabaseName(i);
                var shardWorker = new ShardedSubscriptionWorker(_options, shard, re, parent: this);

                var worker = new SubscriptionShardHolder
                {
                    Worker = shardWorker,
                    PullingTask = shardWorker.RunInternal(CancellationTokenSource.Token),
                    RequestExecutor = re
                };

                _shardWorkers.Add(shard, worker);
            }

            await WriteJsonAsync(new DynamicJsonValue
            {
                [nameof(SubscriptionConnectionServerMessage.Type)] = nameof(SubscriptionConnectionServerMessage.MessageType.ConnectionStatus),
                [nameof(SubscriptionConnectionServerMessage.Status)] = nameof(SubscriptionConnectionServerMessage.ConnectionStatus.Accepted)
            });
        }

        private async Task MaintainConnectionWithClientWorker()
        {
            var hasBatch = _mre.WaitAsync(CancellationTokenSource.Token);
            while (CancellationTokenSource.IsCancellationRequested == false)
            {
                var whenAny = await Task.WhenAny(TimeoutManager.WaitFor(TimeSpan.FromSeconds(3), CancellationTokenSource.Token), hasBatch);
                CancellationTokenSource.Token.ThrowIfCancellationRequested();
                HandleBatchFromWorkersResult result;
                if (whenAny == hasBatch)
                {
                    Interlocked.Exchange(ref _mre, new AsyncManualResetEvent());
                    hasBatch = _mre.WaitAsync(CancellationTokenSource.Token);
                    result = await TryHandleBatchFromWorkersAndCheckReconnect(redirectBatch: true);
                }
                else
                {
                    await SendHeartBeat("Waited for 3000ms for batch from shard workers");
                    result = await TryHandleBatchFromWorkersAndCheckReconnect(redirectBatch: false);
                }

                if (result.Stopping)
                    ThrowStoppingSubscriptionException(result);

                ReconnectWorkersIfNeeded(result.ShardsToReconnect);
            }

            CancellationTokenSource.Token.ThrowIfCancellationRequested();
        }

        private void ThrowStoppingSubscriptionException(HandleBatchFromWorkersResult result)
        {
            throw new AggregateException(
                $"Stopping sharded subscription '{_options.SubscriptionName}' with id '{SubscriptionId}' for database '{TcpConnection.ShardedContext.DatabaseName}' because shard {string.Join(", ", result.Exceptions.Keys)} workers failed.",
                result.Exceptions.Values);
        }

        private void ReconnectWorkersIfNeeded(List<string> shardsToReconnect)
        {
            if (shardsToReconnect.Count == 0)
                return;

            foreach (var shard in shardsToReconnect)
            {
                CancellationTokenSource.Token.ThrowIfCancellationRequested();

                if (_shardWorkers.ContainsKey(shard) == false)
                    continue;

                using (var old = _shardWorkers[shard].Worker)
                {
                    _shardWorkers[shard].Worker = new ShardedSubscriptionWorker(_options, shard, _shardWorkers[shard].RequestExecutor, parent: this);
                    var t = _shardWorkers[shard].Worker.RunInternal(CancellationTokenSource.Token);
                    _shardWorkers[shard].PullingTask = t;
                }
            }
        }

        private class HandleBatchFromWorkersResult
        {
            public Dictionary<string, Exception> Exceptions;
            public List<string> ShardsToReconnect;
            public bool Stopping;
        }

        private async Task<HandleBatchFromWorkersResult> TryHandleBatchFromWorkersAndCheckReconnect(bool redirectBatch)
        {
            var result = new HandleBatchFromWorkersResult
            {
                Exceptions = new Dictionary<string, Exception>(), 
                ShardsToReconnect = new List<string>(), 
                Stopping = false
            };
            foreach ((string shard, SubscriptionShardHolder shardHolder) in _shardWorkers)
            {
                CancellationTokenSource.Token.ThrowIfCancellationRequested();

                if (result.Stopping)
                    continue;

                if (shardHolder.PullingTask.IsFaulted == false)
                {
                    if (redirectBatch == false)
                        continue;

                    var batch = shardHolder.Worker.PublishedBatchItem;
                    if (batch == null)
                        continue;

                    await RedirectBatchAndConfirm(batch, shard);
                    continue;
                }

                result.Exceptions.Add(shard, shardHolder.PullingTask.Exception);
                result.ShardsToReconnect.Add(shard);

                if (CanContinueSubscription(shardHolder))
                    continue;

                // we are stopping this subscription
                result.Stopping = true;
            }

            if (result.Exceptions.Count == _shardWorkers.Count && result.Stopping == false)
            {
                // stop subscription if all workers have unrecoverable exception
                result.Stopping = CanStopSubscription(result.Exceptions);
            }

            return result;
        }

        private bool CanStopSubscription(IReadOnlyDictionary<string, Exception> exceptions)
        {
            bool stopping = true;
            foreach (var worker in _shardWorkers)
            {
                var ex = exceptions[worker.Key];
                Debug.Assert(ex != null, "ex != null");

                (bool shouldTryToReconnect, _) = worker.Value.Worker.CheckIfShouldReconnectWorker(ex, CancellationTokenSource, null, null, throwOnRedirectNodeNotFound: false);
                if (shouldTryToReconnect)
                {
                    // we have at least one worker to try to reconnect
                    stopping = false;
                }
            }

            return stopping;
        }

        private async Task RedirectBatchAndConfirm(ShardedSubscriptionWorker.PublishedBatch batch, string shard)
        {
            try
            {
                // Send to client
                await WriteBatchToClientAndAck(batch, shard);

                // let sharded subscription worker know that we sent the batch to the client and received an ack request from it
                batch.SendBatchToClientTcs.SetResult();
            }
            catch (Exception e)
            {
                // need to fail the shard subscription worker
                batch.SendBatchToClientTcs.TrySetException(e);
                throw;
            }

            // wait for sharded subscription worker to send ack to the shard subscription connection
            // and receive the confirm from the shard subscription connection
            await batch.ConfirmFromShardSubscriptionConnectionTcs.Task;

            // send confirm to client and continue processing
            await SendConfirmToClient();
        }

        private bool CanContinueSubscription(SubscriptionShardHolder shardHolder)
        {
            if (shardHolder.LastErrorDateTime.HasValue == false)
            {
                shardHolder.LastErrorDateTime = DateTime.UtcNow;
                return true;
            }

            if (DateTime.UtcNow - shardHolder.LastErrorDateTime.Value <= _options.ShardedConnectionMaxErroneousPeriod)
                return true;

            return false;
        }

        private async Task WriteBatchToClientAndAck(ShardedSubscriptionWorker.PublishedBatch batch, string shard)
        {
            var replyFromClientTask = GetReplyFromClientAsync();
            AddToStatusDescription(CreateStatusMessage(ConnectionStatus.Info, "Starting to send documents."));
            int docsToFlush = 0;
            string lastReceivedChangeVector = null;

            var sendingCurrentBatchStopwatch = Stopwatch.StartNew();
            using (TcpConnection.ContextPool.AllocateOperationContext(out JsonOperationContext context))
            using (var buffer = new MemoryStream())
            await using (var writer = new AsyncBlittableJsonTextWriter(context, buffer))
            {
                foreach (var doc in batch._batchFromServer.Messages)
                {
                    CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    (BlittableJsonReaderObject metadata, string id, string changeVector) = BatchFromServer.GetMetadataFromBlittable(doc.Data);
                    lastReceivedChangeVector = changeVector;
                    SubscriptionConnection.WriteDocumentOrException(context, writer, document: null, doc.Data, metadata, doc.Exception, id, null, null, null);
                    docsToFlush++;

                    if (await SubscriptionConnection.FlushBatchIfNeeded(sendingCurrentBatchStopwatch, SubscriptionId, writer, buffer, TcpConnection, stats: null, _logger, docsToFlush, CancellationTokenSource.Token) == false)
                        continue;

                    docsToFlush = 0;
                    sendingCurrentBatchStopwatch.Restart();
                }

                //TODO: egor https://issues.hibernatingrhinos.com/issue/RavenDB-16279

                SubscriptionConnection.WriteEndOfBatch(writer);

                AddToStatusDescription(CreateStatusMessage(ConnectionStatus.Info, $"Flushing docs collected from shard '{shard}'"));
                await SubscriptionConnection.FlushDocsToClient(SubscriptionId, writer, buffer, TcpConnection, stats: null, _logger, docsToFlush, endOfBatch: true, CancellationTokenSource.Token);
            }

            batch.LastSentChangeVectorInBatch = lastReceivedChangeVector;
            await WaitForClientAck(replyFromClientTask, sharded: true);
            AddToStatusDescription(CreateStatusMessage(ConnectionStatus.Info, $"Shard '{shard}' got ack from client '{TcpConnection.TcpClient.Client.RemoteEndPoint}'."));
        }

        internal async Task SendConfirmToClient()
        {
            AddToStatusDescription(CreateStatusMessage(ConnectionStatus.Info, $"Shard subscription connection '{SubscriptionId}' send confirm to client '{TcpConnection.TcpClient.Client.RemoteEndPoint}'."));
            await WriteJsonAsync(new DynamicJsonValue
            {
                [nameof(SubscriptionConnectionServerMessage.Type)] = nameof(SubscriptionConnectionServerMessage.MessageType.Confirm)
            });
        }

        public new void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            base.Dispose();

            foreach (var worker in _shardWorkers)
            {
                worker.Value.Dispose();
            }
        }

        private class SubscriptionShardHolder : IDisposable
        {
            public ShardedSubscriptionWorker Worker;
            public Task PullingTask;
            public DateTime? LastErrorDateTime;
            public RequestExecutor RequestExecutor;

            public void Dispose()
            {
                Worker.Dispose();
            }
        }
    }
}
