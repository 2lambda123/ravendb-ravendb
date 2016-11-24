using System;
using System.Threading.Tasks;
using Raven.NewClient.Abstractions.Extensions;
using Raven.NewClient.Abstractions.Util;

using Raven.NewClient.Client.Data;
using Raven.NewClient.Client.Exceptions;


namespace Raven.NewClient.Client.Connection
{
    public class Operation : IObserver<OperationStatusChangeNotification>
    {
        private readonly long _id;
        private IDisposable _subscription;
        private readonly TaskCompletionSource<IOperationResult> _result = new TaskCompletionSource<IOperationResult>();

        public Action<IOperationProgress> OnProgressChanged;

        internal long Id => _id;

        public Operation(long id)
        {
            _id = id;

            Task.Factory.StartNew(Initialize);
        }

        private async Task Initialize()
        {
            throw new NotImplementedException();
            /* try
             {
                 await _asyncServerClient.changes.Value.ConnectionTask.ConfigureAwait(false);
                 var observableWithTask = _asyncServerClient.changes.Value.ForOperationId(_id);
                 _subscription = observableWithTask.Subscribe(this);
                 await FetchOperationStatus().ConfigureAwait(false);
             }
             catch (Exception e)
             {
                 _result.TrySetException(e);
             }*/
        }

        /// <summary>
        /// Since operation might complete before we subscribe to it, 
        /// fetch operation status but only once  to avoid race condition
        /// If we receive notification using changes API meanwhile, ignore fetched status
        /// to avoid issues with non monotonic increasing progress
        /// </summary>
        private async Task FetchOperationStatus()
        {
            throw new NotImplementedException();
            /* var operationStatusJson = await _asyncServerClient.GetOperationStatusAsync(_id).ConfigureAwait(false);
             var operationStatus = _asyncServerClient.convention
                     .CreateSerializer()
                     .Deserialize<OperationState>(new RavenJTokenReader(operationStatusJson));
             // using deserializer from Conventions to properly handle $type mapping

             OnNext(new OperationStatusChangeNotification
             {
                 OperationId = _id,
                 State = operationStatus
             });*/
        }

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
                    if (exceptionResult?.StatusCode == 409)
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

        public virtual async Task<IOperationResult> WaitForCompletionAsync(TimeSpan? timeout = null)
        {
            var completed = await _result.Task.WaitWithTimeout(timeout).ConfigureAwait(false);
            if (completed == false)
                throw new TimeoutException($"After {timeout}, did not get a reply for operation " + _id);

            return await _result.Task.ConfigureAwait(false);
        }

        public virtual IOperationResult WaitForCompletion(TimeSpan? timeout = null)
        {
            return AsyncHelpers.RunSync(() => WaitForCompletionAsync(timeout));
        }
    }
}
