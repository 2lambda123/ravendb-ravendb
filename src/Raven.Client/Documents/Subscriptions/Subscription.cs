// -----------------------------------------------------------------------
//  <copyright file="Subscription.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Raven.Client.Documents.Exceptions.Subscriptions;
using Raven.Client.Documents.Identity;
using Raven.Client.Documents.Replication.Messages;
using Raven.Client.Documents.Session;
using Raven.Client.Exceptions.Database;
using Raven.Client.Exceptions.Security;
using Raven.Client.Extensions;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Converters;
using Raven.Client.Server.Commands;
using Raven.Client.Server.Tcp;
using Raven.Client.Util;
using Sparrow;
using Sparrow.Json;
using Sparrow.Logging;
using Sparrow.Utils;

namespace Raven.Client.Documents.Subscriptions
{
    public class SubscriptionBatch<T> 
    {
        public struct Item
        {
            public string Id { get; internal set; }
            public long Etag { get; internal set; }
            public T Result { get; internal set; }
            public BlittableJsonReaderObject RawResult { get; internal set; }
            public BlittableJsonReaderObject RawMetadata { get; internal set; }
            
            private IMetadataDictionary _metadata;
            public IMetadataDictionary Metadata => _metadata ?? (_metadata = new MetadataAsDictionary(RawMetadata));
        }

        public int NumberOfItemsInBatch;

        private readonly RequestExecutor _requestExecutor;
        private readonly IDocumentStore _store;
        private readonly string _dbName;
        private readonly Logger _logger;
        private readonly GenerateEntityIdOnTheClient _generateEntityIdOnTheClient;

        public List<Item> Items { get; } = new List<Item>();
        
        public IDocumentSession OpenSession()
        {
            return _store.OpenSession(new SessionOptions
            {
                Database = _dbName,
                RequestExecutor = _requestExecutor
            });
        }
        
        public IAsyncDocumentSession OpenAsyncSession()
        {
            return _store.OpenAsyncSession(new SessionOptions
            {
                Database = _dbName,
                RequestExecutor = _requestExecutor
            });
        }

        public SubscriptionBatch(RequestExecutor requestExecutor, IDocumentStore store, string dbName, Logger logger)
        {
            _requestExecutor = requestExecutor;
            _store = store;
            _dbName = dbName;
            _logger = logger;

            _generateEntityIdOnTheClient = new GenerateEntityIdOnTheClient(store.Conventions,
                entity => throw new InvalidOperationException("Shouldn't be generating new ids here"));
        }


        internal ChangeVectorEntry[] Initialize(List<SubscriptionConnectionServerMessage> batch)
        {
            Items.Capacity = Math.Max(Items.Capacity, batch.Count);
            Items.Clear();
            ChangeVectorEntry[] lastReceivedChangeVector = null;

            foreach (var item in batch)
            {
                
                BlittableJsonReaderObject metadata;
                var curDoc = item.Data;
                
                if (curDoc.TryGet(Constants.Documents.Metadata.Key, out metadata) == false)
                    ThrowRequired("@metadata field");
                if (metadata.TryGet(Constants.Documents.Metadata.Id, out string id) == false)
                    ThrowRequired("@id field");
                if (metadata.TryGet(Constants.Documents.Metadata.Etag, out long etag) == false)
                    ThrowRequired("@etag field");
                if (metadata.TryGet(Constants.Documents.Metadata.ChangeVector, out BlittableJsonReaderArray changeVectorAsObject) == false || 
                    changeVectorAsObject == null)
                    ThrowRequired("@change-vector field");
                else
                    lastReceivedChangeVector = changeVectorAsObject.ToVector();
                
                if (_logger.IsInfoEnabled)
                {
                    _logger.Info($"Got {id} (change vector: [{string.Join(",", lastReceivedChangeVector.Select(x => $"{x.DbId.ToString()}:{x.Etag}"))}], size {curDoc.Size}");
                }

                
                T instance;

                if (typeof(T) == typeof(BlittableJsonReaderObject))
                {
                    instance = (T)(object)curDoc;
                }
                else
                {
                    instance = (T)EntityToBlittable.ConvertToEntity(typeof(T), id, curDoc, _store.Conventions);
                }

                if (string.IsNullOrEmpty(id) == false)
                    _generateEntityIdOnTheClient.TrySetIdentity(instance, id);

                Items.Add(new Item
                {
                    Etag = etag,
                    Id = id,
                    RawResult = curDoc,
                    RawMetadata = metadata,
                    Result = instance,
                });
            }
            return lastReceivedChangeVector;
        }
        
        private static void ThrowRequired(string name)
        {
            throw new InvalidOperationException("Document must have a " + name);
        }
    }
    
    public class Subscription<T> : IAsyncDisposable, IDisposable where T : class
    {
        public delegate Task AfterAcknowledgmentAction(SubscriptionBatch<T> batch);
        
        private readonly Logger _logger;
        private readonly IDocumentStore _store;
        private readonly string _dbName;
        private readonly CancellationTokenSource _processingCts = new CancellationTokenSource();
        private readonly SubscriptionConnectionOptions _options;
        private (Func<SubscriptionBatch<T>, Task> Async, Action<SubscriptionBatch<T>> Sync) _subscriber;
        private TcpClient _tcpClient;
        private bool _disposed;
        private Task _subscriptionTask;
        private Stream _stream;

        /// <summary>
        /// allows the user to define stuff that happens after the confirm was received from the server (this way we know we won't
        /// get those documents again)
        /// </summary>
        public event AfterAcknowledgmentAction  AfterAcknowledgment;

        internal Subscription(SubscriptionConnectionOptions options, IDocumentStore documentStore, string dbName)
        {
            _options = options;
            _logger = LoggingSource.Instance.GetLogger<Subscription<T>>(dbName);
            if (_options.SubscriptionId <= 0 && string.IsNullOrEmpty(options.SubscriptionName))
                throw new ArgumentException(
                    "SubscriptionConnectionOptions must specify the SubscriptionId if the SubscriptionName is not set, but was set to " + _options.SubscriptionId,
                    nameof(options));
            _store = documentStore;
            _dbName = dbName ?? documentStore.Database;

        }

        public long SubscriptionId => _options.SubscriptionId;

        public void Dispose()
        {
            Dispose(true);
        }

        public void Dispose(bool waitForSubscriptionTask)
        {
            if (_disposed)
                return;

            AsyncHelpers.RunSync(() => DisposeAsync(waitForSubscriptionTask));
        }

        public Task DisposeAsync()
        {
            return DisposeAsync(true);
        }

        public async Task DisposeAsync(bool waitForSubscriptionTask)
        {
            try
            {
                if (_disposed)
                    return;

                _disposed = true;
                _processingCts.Cancel();

                CloseTcpClient(); // we disconnect immediately, freeing the subscription task

                if (_subscriptionTask != null && waitForSubscriptionTask)
                {
                    try
                    {
                        await _subscriptionTask.ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // just need to wait for it to end                        
                    }
                }

            }
            catch (Exception ex)
            {
                if (_logger.IsInfoEnabled)
                    _logger.Info("Error during dispose of subscription", ex);
            }
            finally
            {
                OnDisposed(this);
            }
        }

        public Task Run(Action<SubscriptionBatch<T>> processDocuments)
        {
            if (processDocuments == null) throw new ArgumentNullException(nameof(processDocuments));
            _subscriber = (null, processDocuments);
            return Run();
        }

        public Task Run(Func<SubscriptionBatch<T>, Task> processDocuments)
        {
            if (processDocuments == null) throw new ArgumentNullException(nameof(processDocuments));
            _subscriber = (processDocuments, null);
            return Run();
        }

        private Task Run()
        {
            if (_subscriptionTask != null)
                throw new InvalidOperationException("The subscription is already running");

            return _subscriptionTask = RunSubscriptionAsync();

        }

        private ServerNode _redirectNode;
        private RequestExecutor _subscriptionLocalRequestExecuter;

        public string CurrentNodeTag => _redirectNode?.ClusterTag;

        private async Task<Stream> ConnectToServer()
        {
            var command = new GetTcpInfoCommand("Subscription/" + _dbName);

            JsonOperationContext context;
             var requestExecutor = _store.GetRequestExecutor(_dbName);
      
                 using (requestExecutor.ContextPool.AllocateOperationContext(out context))
            {
                if (_redirectNode != null)
                {
                    try
                    {
                        await requestExecutor.ExecuteAsync(_redirectNode, context, command, shouldRetry: false).ConfigureAwait(false);

                    }
                    catch (Exception)
                    {
                        // if we failed to talk to a node, we'll forget about it and let the topology to 
                        // redirect us to the current node
                        _redirectNode = null; 
                        throw;
                    }
                }
                else
                {
                    await requestExecutor.ExecuteAsync(command, context).ConfigureAwait(false);
                }

                var apiToken = await requestExecutor.GetAuthenticationToken(context, command.RequestedNode).ConfigureAwait(false);
                var uri = new Uri(command.Result.Url);

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(TcpUtils.GetTcpUrl(uri.Host), uri.Port).ConfigureAwait(false);

                _tcpClient.NoDelay = true;
                _tcpClient.SendBufferSize = 32 * 1024;
                _tcpClient.ReceiveBufferSize = 4096;
                _stream = _tcpClient.GetStream();
                _stream = await TcpUtils.WrapStreamWithSslAsync(_tcpClient, command.Result).ConfigureAwait(false);

                var databaseName = _dbName ?? _store.Database;
                var header = Encodings.Utf8.GetBytes(JsonConvert.SerializeObject(new TcpConnectionHeaderMessage
                {
                    Operation = TcpConnectionHeaderMessage.OperationTypes.Subscription,
                    DatabaseName = databaseName,
                    AuthorizationToken = apiToken
                }));

                var options = Encodings.Utf8.GetBytes(JsonConvert.SerializeObject(_options));

                await _stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
                //Reading reply from server
                using (var response = context.ReadForMemory(_stream, "Subscription/tcp-header-response"))
                {
                    var reply = JsonDeserializationClient.TcpConnectionHeaderResponse(response);
                    switch (reply.Status)
                    {
                        case TcpConnectionHeaderResponse.AuthorizationStatus.Forbidden:
                        case TcpConnectionHeaderResponse.AuthorizationStatus.ForbiddenReadOnly:
                            throw AuthorizationException.Forbidden($"Cannot access database {databaseName} because we got a Forbidden authorization status");
                        case TcpConnectionHeaderResponse.AuthorizationStatus.Success:
                            break;
                        default:
                            throw AuthorizationException.Unauthorized(reply.Status, _dbName);
                    }
                }
                await _stream.WriteAsync(options, 0, options.Length).ConfigureAwait(false);

                await _stream.FlushAsync().ConfigureAwait(false);

                _subscriptionLocalRequestExecuter?.Dispose();
                _subscriptionLocalRequestExecuter = RequestExecutor.CreateForSingleNode(command.RequestedNode.Url, _dbName, requestExecutor.ApiKey);
                return _stream;
            }
        }

        private void AssertConnectionState(SubscriptionConnectionServerMessage connectionStatus)
        {
            if (connectionStatus.Type == SubscriptionConnectionServerMessage.MessageType.Error)
            {
                if (connectionStatus.Exception.Contains(nameof(DatabaseDoesNotExistException)))
                    DatabaseDoesNotExistException.ThrowWithMessage(_dbName, connectionStatus.Message);
            }
            if (connectionStatus.Type != SubscriptionConnectionServerMessage.MessageType.ConnectionStatus)
                throw new Exception("Server returned illegal type message when expecting connection status, was: " +
                                    connectionStatus.Type);

            switch (connectionStatus.Status)
            {
                case SubscriptionConnectionServerMessage.ConnectionStatus.Accepted:
                    break;
                case SubscriptionConnectionServerMessage.ConnectionStatus.InUse:
                    throw new SubscriptionInUseException(
                        $"Subscription With Id {_options.SubscriptionId} cannot be opened, because it's in use and the connection strategy is {_options.Strategy}");
                case SubscriptionConnectionServerMessage.ConnectionStatus.Closed:
                    throw new SubscriptionClosedException(
                        $"Subscription With Id {_options.SubscriptionId} cannot be opened, because it was closed.  " + connectionStatus.Exception);
                case SubscriptionConnectionServerMessage.ConnectionStatus.Invalid:
                    throw new SubscriptionInvalidStateException(
                        $"Subscription With Id {_options.SubscriptionId} cannot be opened, because it is in invalid state. " + connectionStatus.Exception);
                case SubscriptionConnectionServerMessage.ConnectionStatus.NotFound:
                    throw new SubscriptionDoesNotExistException(
                        $"Subscription With Id {_options.SubscriptionId} cannot be opened, because it does not exist. " + connectionStatus.Exception);
                case SubscriptionConnectionServerMessage.ConnectionStatus.Redirect:
                    throw new SubscriptionDoesNotBelongToNodeException(
                        $"Subscription With Id {_options.SubscriptionId} cannot be proccessed by current node, it will be redirected to {connectionStatus.Data[nameof(SubscriptionConnectionServerMessage.SubscriptionRedirectData.RedirectedTag)]}"
                        )
                    {
                        AppropriateNode = connectionStatus.Data[nameof(SubscriptionConnectionServerMessage.SubscriptionRedirectData.RedirectedTag)].ToString()
                    };
                default:
                    throw new ArgumentException(
                        $"Subscription {_options.SubscriptionId} could not be opened, reason: {connectionStatus.Status}");
            }
        }

        private async Task ProcessSubscriptionAsync()
        {
            try
            {
                _processingCts.Token.ThrowIfCancellationRequested();
                var contextPool = _store.GetRequestExecutor(_dbName).ContextPool;
                using (var buffer = JsonOperationContext.ManagedPinnedBuffer.LongLivedInstance())
                {
                    using (var tcpStream = await ConnectToServer().ConfigureAwait(false))
                    {
                        _processingCts.Token.ThrowIfCancellationRequested();
                        JsonOperationContext handshakeContext;
                        var tcpStreamCopy = tcpStream;
                        using (contextPool.AllocateOperationContext(out handshakeContext))
                        {
                            var connectionStatus = await ReadNextObject(handshakeContext, tcpStreamCopy, buffer).ConfigureAwait(false);
                            if (_processingCts.IsCancellationRequested)
                                return;

                            if (connectionStatus.Type != SubscriptionConnectionServerMessage.MessageType.ConnectionStatus ||
                                connectionStatus.Status != SubscriptionConnectionServerMessage.ConnectionStatus.Accepted)
                                AssertConnectionState(connectionStatus);
                        }

                        if (_processingCts.IsCancellationRequested)
                            return;

                        Task notifiedSubscriber = Task.CompletedTask;
                        
                        var batch = new SubscriptionBatch<T>(_subscriptionLocalRequestExecuter, _store, _dbName, _logger);
                        
                        while (_processingCts.IsCancellationRequested == false)
                        {
                            // start the read from the server
                            var readFromServer = ReadSingleSubscriptionBatchFromServer(contextPool, tcpStreamCopy, buffer, batch);
                            try
                            {
                                // and then wait for the subscriber to complete
                                await notifiedSubscriber.ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                // if the subscriber errored, we shut down
                                try
                                {
                                    CloseTcpClient();
                                    using ((await readFromServer.ConfigureAwait(false)).ReturnContext)
                                    {
                                        
                                    }
                                }
                                catch (Exception)
                                {
                                    // nothing to be done here
                                }
                                throw;
                            }
                            var incomingBatch = await readFromServer.ConfigureAwait(false);

                            _processingCts.Token.ThrowIfCancellationRequested();

                            var lastReceivedChangeVector = batch.Initialize(incomingBatch.Messages);
                            
                            
                            notifiedSubscriber = Task.Run(async () =>
                            {
                                // ReSharper disable once AccessToDisposedClosure
                                using (incomingBatch.ReturnContext)
                                {
                                    try
                                    {
                                        if (_subscriber.Async != null)
                                            await _subscriber.Async(batch).ConfigureAwait(false);
                                        else
                                            _subscriber.Sync(batch);
                                    }
                                    catch (Exception ex)
                                    {
                                        if (_logger.IsInfoEnabled)
                                        {
                                            _logger.Info(
                                                $"Subscription #{_options.SubscriptionId}. Subscriber threw an exception on document batch", ex);
                                        }

                                        if (_options.IgnoreSubscriberErrors == false)
                                            throw;
                                    }

                                }
                             
                                try
                                {
                                    if (tcpStreamCopy != null) //possibly prevent ObjectDisposedException
                                    {
                                        SendAck(lastReceivedChangeVector, tcpStreamCopy);
                                    }
                                }
                                catch (ObjectDisposedException)
                                {
                                    //if this happens, this means we are disposing, so don't care..
                                    //(this peace of code happens asynchronously to external using(tcpStream) statement)
                                }
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // this is thrown when shutting down, it
                // isn't an error, so we don't need to treat
                // it as such
            }
        }

        private async Task<(List<SubscriptionConnectionServerMessage> Messages, IDisposable ReturnContext)> ReadSingleSubscriptionBatchFromServer(JsonContextPool contextPool, Stream tcpStream, JsonOperationContext.ManagedPinnedBuffer buffer, SubscriptionBatch<T> batch)
        {
            JsonOperationContext context;
            var incomingBatch = new List<SubscriptionConnectionServerMessage>();
            var returnContext = contextPool.AllocateOperationContext(out context);
            bool endOfBatch = false;
            while (endOfBatch == false && _processingCts.IsCancellationRequested == false)
            {
                var receivedMessage = await ReadNextObject(context, tcpStream, buffer).ConfigureAwait(false);
                if (receivedMessage == null || _processingCts.IsCancellationRequested)
                    break;

                switch (receivedMessage.Type)
                {
                    case SubscriptionConnectionServerMessage.MessageType.Data:
                        incomingBatch.Add(receivedMessage);
                        break;
                    case SubscriptionConnectionServerMessage.MessageType.EndOfBatch:
                        endOfBatch = true;
                        break;
                    case SubscriptionConnectionServerMessage.MessageType.Confirm:
                        var onAfterAcknowledgment = AfterAcknowledgment;
                        if (onAfterAcknowledgment != null)
                            await onAfterAcknowledgment(batch).ConfigureAwait(false);
                        incomingBatch.Clear();
                        batch.Items.Clear();
                        break;
                    case SubscriptionConnectionServerMessage.MessageType.ConnectionStatus:
                        AssertConnectionState(receivedMessage);
                        break;
                    case SubscriptionConnectionServerMessage.MessageType.Error:
                        ThrowSubscriptionError(receivedMessage);
                        break;
                    default:
                        ThrowInvalidServerResponse(receivedMessage);
                        break;
                }
            }
            return (incomingBatch, returnContext);
        }

        private static void ThrowInvalidServerResponse(SubscriptionConnectionServerMessage receivedMessage)
        {
            throw new ArgumentException(
                $"Unrecognized message '{receivedMessage.Type}' type received from server");
        }

        private static void ThrowSubscriptionError(SubscriptionConnectionServerMessage receivedMessage)
        {
            throw new InvalidOperationException(
                $"Connection terminated by server. Exception: {receivedMessage.Exception ?? "None"}");
        }

        private async Task<SubscriptionConnectionServerMessage> ReadNextObject(JsonOperationContext context, Stream stream, JsonOperationContext.ManagedPinnedBuffer buffer)
        {
            if (_processingCts.IsCancellationRequested || _tcpClient.Connected == false)
                return null;

            if (_disposed) //if we are disposed, nothing to do...
                return null;

            try
            {
                var blittable = await context.ParseToMemoryAsync(stream, "Subscription/next/object", BlittableJsonDocumentBuilder.UsageMode.None, buffer)
                    .ConfigureAwait(false);

                blittable.BlittableValidation();
                return JsonDeserializationClient.SubscriptionNextObjectResult(blittable);
            }
            catch (ObjectDisposedException)
            {
                //this can happen only if Subscription<T> is disposed, and in this case we don't care about a result...
                return null;
            }
        }

    
        private void SendAck(ChangeVectorEntry[] lastReceivedChangeVector, Stream networkStream)
        {
            var ack = Encodings.Utf8.GetBytes(JsonConvert.SerializeObject(new SubscriptionConnectionClientMessage
            {
                ChangeVector = lastReceivedChangeVector,
                Type = SubscriptionConnectionClientMessage.MessageType.Acknowledge
            }));

            networkStream.Write(ack, 0, ack.Length);
            networkStream.Flush();
        }

        private async Task RunSubscriptionAsync()
        {
            while (_processingCts.Token.IsCancellationRequested == false)
            {
                try
                {
                    CloseTcpClient();
                    if (_logger.IsInfoEnabled)
                    {
                        _logger.Info($"Subscription #{_options.SubscriptionId}. Connecting to server...");
                    }

                    await ProcessSubscriptionAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    bool shouldRetrhow = false;
                    try
                    {
                        if (_processingCts.Token.IsCancellationRequested)
                            return;
                        
                        if (_logger.IsInfoEnabled)
                        {
                            _logger.Info(
                                $"Subscription #{_options.SubscriptionId}. Pulling task threw the following exception", ex);
                        }
                        if (TryHandleRejectedConnectionOrDispose(ex) == false)
                        {
                            if (_logger.IsInfoEnabled)
                                _logger.Info($"Connection to subscription #{_options.SubscriptionId} have been shut down because of an error", ex);

                            shouldRetrhow = true;
                        }
                        else
                        {
                            await TimeoutManager.WaitFor(_options.TimeToWaitBeforeConnectionRetry).ConfigureAwait(false);
                        }
                    }
                    catch (Exception e)
                    {
                        throw new AggregateException(e, ex);
                    }
                    if (shouldRetrhow)
                        throw;
                }
            }
        }

        private bool TryHandleRejectedConnectionOrDispose(Exception ex)
        {
            switch (ex)
            {
                case SubscriptionInUseException _:
                case SubscriptionDoesNotExistException _:
                case SubscriptionClosedException _:
                case SubscriptionInvalidStateException _:
                case DatabaseDoesNotExistException _:
                case AuthorizationException _:
                    _processingCts.Cancel();
                    break;
                case SubscriptionDoesNotBelongToNodeException se:
                    var requestExecutor = _store.GetRequestExecutor(_dbName);
                    var nodeToRedirectTo = requestExecutor.TopologyNodes
                        .FirstOrDefault(x => x.ClusterTag == se.AppropriateNode);
                    _redirectNode = nodeToRedirectTo ?? throw new AggregateException(ex,
                                        new InvalidOperationException($"Could not redirect to {se.AppropriateNode}, because it was not found in local topology, even after retrying"));
                    return true;
            }

            return false;
        }


        private void CloseTcpClient()
        {
            if (_stream != null)
            {
                try
                {
                    _stream.Dispose();
                    _stream = null;
                }
                catch (Exception)
                {
                    // can't do anything here
                }
            }
            if (_tcpClient != null)
            {
                try
                {
                    _tcpClient.Dispose();
                    _tcpClient = null;
                }
                catch (Exception)
                {
                    // nothing to be done
                }
            }
        }

        public event Action<Subscription<T>> OnDisposed = delegate {  };
    }
}
