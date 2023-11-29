﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents
{
    public class ClusterTransactionWaiter
    {
        internal readonly DocumentDatabase Database;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<long?>> _results = new ConcurrentDictionary<string, TaskCompletionSource<long?>>();

        public ClusterTransactionWaiter(DocumentDatabase database)
        {
            Database = database;
        }

        public RemoveTask CreateTask(string id, long index, out Task<long?> task)
        {
            var t = new TaskCompletionSource<long?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = _results.GetOrAdd(id, t);

            if (current == t)
            {
                var lastCompleted = Interlocked.Read(ref Database.LastCompletedClusterTransactionIndex);
                if (lastCompleted >= index)
                {
                    current.TrySetResult(null);
                }
            }

            task = current.Task;
            return new RemoveTask(this, id);
        }

        public TaskCompletionSource<long?> Get(string id)
        {
            _results.TryGetValue(id, out var val);
            return val;
        }

        public void SetResult(string id, long index, long count)
        {
            Database.RachisLogIndexNotifications.NotifyListenersAbout(index, null);
            if (_results.TryGetValue(id, out var task))
            {
                task.SetResult(count);
            }
        }

        public void SetException(string id, long index, Exception e)
        {
            Database.RachisLogIndexNotifications.NotifyListenersAbout(index, e);
            if (_results.TryGetValue(id, out var task))
            {
                task.SetException(e);
            }
        }

        public struct RemoveTask : IDisposable
        {
            private readonly ClusterTransactionWaiter _parent;
            private readonly string _id;

            public RemoveTask(ClusterTransactionWaiter parent, string id)
            {
                _parent = parent;
                _id = id;
            }

            public void Dispose()
            {
                if (_parent._results.TryRemove(_id, out var task))
                {
                    // cancel it, if someone still awaits
                    task.TrySetCanceled();
                }
            }
        }

        public async Task WaitForResults(string id, CancellationToken token)
        {
            if (_results.TryGetValue(id, out var task) == false)
            {
                throw new InvalidOperationException($"Task with the id '{id}' was not found.");
            }

            await using (token.Register(() => task.TrySetCanceled()))
            {
                await task.Task.ConfigureAwait(false);
            }
        }
    }
}
