using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Raven.Client.Util
{
    public class SizeLimitedConcurrentDictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        private readonly ConcurrentDictionary<TKey, TValue> _dic;
        private readonly ConcurrentQueue<TKey> _queue = new ConcurrentQueue<TKey>();

        private readonly int _size;

        public SizeLimitedConcurrentDictionary(int size = 100)
            : this(size, EqualityComparer<TKey>.Default)
        {

        }

        public SizeLimitedConcurrentDictionary(int size, IEqualityComparer<TKey> equalityComparer)
        {
            _size = size;
            _dic = new ConcurrentDictionary<TKey, TValue>(equalityComparer);
        }

        public void Set(TKey key, TValue item)
        {
            _dic.AddOrUpdate(key, _ => item, (_, __) => item);
            _queue.Enqueue(key);

            while (_queue.Count > _size)
            {
                TKey result;
                if (_queue.TryDequeue(out result) == false)
                    break;
                TValue value;
                _dic.TryRemove(key, out value);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dic.TryGetValue(key, out value);
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            return _dic.TryRemove(key, out value);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _dic.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
