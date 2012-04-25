using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Raven.Studio.Infrastructure
{
    public abstract class VirtualCollectionSource<T> : IVirtualCollectionSource<T>
    {
        private readonly object lockObject = new object();
        public event EventHandler<EventArgs> CollectionChanged;
        public event EventHandler<DataFetchErrorEventArgs> DataFetchError;

        private int count;

        public virtual int Count
        {
            get
            {
                lock (lockObject)
                {
                    return count;
                }
            }
        }

        protected abstract Task<int> GetCount();
        public abstract Task<IList<T>> GetPageAsync(int start, int pageSize, IList<SortDescription> sortDescriptions);

        public virtual void Refresh()
        {
            BeginGetCount();
        }

        private void BeginGetCount()
        {
            GetCount()
                .ContinueWith(t =>
                {
                    if (!t.IsFaulted)
                    {
                        SetCount(t.Result, forceCollectionChangeNotification: true);
                    }
                    else
                    {
                        SetCount(0, forceCollectionChangeNotification: true);
                        OnDataFetchError(new DataFetchErrorEventArgs(t.Exception));
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously);
        }

        protected void OnCollectionChanged(EventArgs e)
        {
            var handler = CollectionChanged;
            if (handler != null) handler(this, e);
        }

        protected void OnDataFetchError(DataFetchErrorEventArgs e)
        {
            var handler = DataFetchError;
            if (handler != null) handler(this, e);
        }

        protected void SetCount(int newCount, bool forceCollectionChangeNotification = false)
        {
            bool fileCountChanged;

            lock (lockObject)
            {
                fileCountChanged = newCount != count;
                count = newCount;
            }

            if (fileCountChanged || forceCollectionChangeNotification)
            {
                OnCollectionChanged(EventArgs.Empty);
            }
        }
    }
}