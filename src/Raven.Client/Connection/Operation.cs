using System;
using System.Threading.Tasks;
using Raven.Abstractions.Util;
using Raven.Client.Connection.Async;
using Raven.Client.Data;
using Raven.Client.Exceptions;
using Raven.Json.Linq;

namespace Raven.Client.Connection
{
    public class Operation : IObserver<OperationStatusChangeNotification>
    {
        private readonly AsyncServerClient _asyncServerClient;
        private readonly long _id;
        private IDisposable _subscription;
        private readonly TaskCompletionSource<IOperationResult> _result = new TaskCompletionSource<IOperationResult>();

        public Operation(long id)
        {
            _id = id;
            throw new NotImplementedException();
        }

        public Operation(AsyncServerClient asyncServerClient, long id)
        {
            _asyncServerClient = asyncServerClient;
            _id = id;
            Task.Run(Initialize);
        }

        private async Task Initialize()
        {
            await _asyncServerClient.changes.Value.ConnectionTask.ConfigureAwait(false);
            var observableWithTask = _asyncServerClient.changes.Value.ForOperationId(_id);
            _subscription = observableWithTask.Subscribe(this);
            await FetchOperationStatus();
        }

        /// <summary>
        /// Since operation might complete before we subscribe to it, 
        /// fetch operation status but only once  to avoid race condition
        /// If we receive notification using changes API meanwhile, ignore fetched status
        /// to avoid issues with non monotonic increasing progress
        /// </summary>
        private async Task FetchOperationStatus()
        {
            var operationStatusJson = await _asyncServerClient.GetOperationStatusAsync(_id);
            var operationStatus = _asyncServerClient.convention
                    .CreateSerializer()
                    .Deserialize<OperationState>(new RavenJTokenReader(operationStatusJson));
                // using deserializer from Conventions to properly handle $type mapping

            OnNext(new OperationStatusChangeNotification
            {
                OperationId = _id,
                State = operationStatus
            });
        }

        internal long Id => _id;

        public Action<IOperationProgress> OnProgressChanged;

        public void OnNext(OperationStatusChangeNotification notification)
        {
            var onProgress = OnProgressChanged;

            switch (notification.State.Status)
            {
                case OperationStatus.InProgress:
                    if (onProgress != null && notification.State.Progress != null)
                    {
                        onProgress(notification.State.Progress);
                    }
                    break;
                case OperationStatus.Completed:
                    _subscription.Dispose();
                    _result.TrySetResult(notification.State.Result);
                    break;
                case OperationStatus.Faulted:
                    _subscription.Dispose();
                    var exceptionResult = notification.State.Result as OperationExceptionResult;
                    if(exceptionResult?.StatusCode == 409)
                        _result.TrySetException(new DocumentInConflictException(exceptionResult.Message));
                    else
                        _result.TrySetException(new InvalidOperationException(exceptionResult?.Message));
                    break;
                case OperationStatus.Canceled:
                    _subscription.Dispose();
                    _result.TrySetCanceled();
                    break;
            }
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }

        public virtual async Task<IOperationResult> WaitForCompletionAsync()
        {
            var whenAny = await Task.WhenAny(_result.Task, Task.Delay(15*1000));
            if(whenAny != _result.Task)
                throw new TimeoutException("After 15 seconds, did not get a reply for operation " + _id);
            return await _result.Task;
        }

        public virtual IOperationResult WaitForCompletion()
        {
            return AsyncHelpers.RunSync(WaitForCompletionAsync);
        }
    }
}
