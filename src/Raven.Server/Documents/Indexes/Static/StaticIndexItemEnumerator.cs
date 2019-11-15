﻿using System;
using System.Collections;
using System.Collections.Generic;
using Raven.Client.Documents.Indexes;

namespace Raven.Server.Documents.Indexes.Static
{
    public class StaticIndexItemEnumerator<TType> : IIndexedItemEnumerator where TType : AbstractDynamicObject, new()
    {
        private readonly IndexingStatsScope _documentReadStats;
        private readonly IEnumerator<IndexItem> _itemsEnumerator;
        private readonly IEnumerable _resultsOfCurrentDocument;
        private readonly MultipleIndexingFunctionsEnumerator<TType> _multipleIndexingFunctionsEnumerator;

        protected StaticIndexItemEnumerator(IEnumerable<IndexItem> items)
        {
            _itemsEnumerator = items.GetEnumerator();
        }

        public StaticIndexItemEnumerator(IEnumerable<IndexItem> items, List<IndexingFunc> funcs, IIndexCollection collection, IndexingStatsScope stats, IndexType type)
            : this(items)
        {
            _documentReadStats = stats?.For(IndexingOperation.Map.DocumentRead, start: false);

            var indexingFunctionType = type.IsJavaScript() ? IndexingOperation.Map.Jint : IndexingOperation.Map.Linq;

            var mapFuncStats = stats?.For(indexingFunctionType, start: false);

            if (funcs.Count == 1)
            {
                _resultsOfCurrentDocument =
                    new TimeCountingEnumerable(funcs[0](new DynamicIteratorOfCurrentItemWrapper<TType>(this)), mapFuncStats);
            }
            else
            {
                _multipleIndexingFunctionsEnumerator = new MultipleIndexingFunctionsEnumerator<TType>(funcs, new DynamicIteratorOfCurrentItemWrapper<TType>(this));
                _resultsOfCurrentDocument = new TimeCountingEnumerable(_multipleIndexingFunctionsEnumerator, mapFuncStats);
            }

            CurrentIndexingScope.Current.SetSourceCollection(collection, mapFuncStats);
        }

        public bool MoveNext(out IEnumerable resultsOfCurrentDocument)
        {
            using (_documentReadStats?.Start())
            {
                if (Current.Item is IDisposable disposable)
                    disposable.Dispose();

                if (_itemsEnumerator.MoveNext() == false)
                {
                    Current = default;
                    resultsOfCurrentDocument = null;

                    return false;
                }

                Current = _itemsEnumerator.Current;
                resultsOfCurrentDocument = _resultsOfCurrentDocument;

                return true;
            }
        }

        public void OnError()
        {
            _multipleIndexingFunctionsEnumerator?.Reset();
        }

        public IndexItem Current { get; private set; }

        public void Dispose()
        {
            _itemsEnumerator.Dispose();

            if (Current.Item is IDisposable disposable)
                disposable.Dispose();
        }

        protected class DynamicIteratorOfCurrentItemWrapper<TDynamicIteratorOfCurrentItemWrapperType> : IEnumerable<TDynamicIteratorOfCurrentItemWrapperType> where TDynamicIteratorOfCurrentItemWrapperType : AbstractDynamicObject, new()
        {
            private readonly StaticIndexItemEnumerator<TDynamicIteratorOfCurrentItemWrapperType> _indexingEnumerator;
            private Enumerator<TDynamicIteratorOfCurrentItemWrapperType> _enumerator;

            public DynamicIteratorOfCurrentItemWrapper(StaticIndexItemEnumerator<TDynamicIteratorOfCurrentItemWrapperType> indexingEnumerator)
            {
                _indexingEnumerator = indexingEnumerator;
            }

            public IEnumerator<TDynamicIteratorOfCurrentItemWrapperType> GetEnumerator()
            {
                return _enumerator ?? (_enumerator = new Enumerator<TDynamicIteratorOfCurrentItemWrapperType>(_indexingEnumerator));
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private class Enumerator<TEnumeratorType> : IEnumerator<TEnumeratorType> where TEnumeratorType : AbstractDynamicObject, new()
            {
                private TEnumeratorType _dynamicDocument;
                private readonly StaticIndexItemEnumerator<TEnumeratorType> _inner;
                private object _seen;

                public Enumerator(StaticIndexItemEnumerator<TEnumeratorType> indexingEnumerator)
                {
                    _inner = indexingEnumerator;
                }

                public bool MoveNext()
                {
                    if (_seen == _inner.Current.Item) // already iterated
                        return false;

                    _seen = _inner.Current.Item;

                    if (_dynamicDocument == null)
                        _dynamicDocument = new TEnumeratorType();

                    _dynamicDocument.Set(_seen);

                    Current = _dynamicDocument;

                    CurrentIndexingScope.Current.Source = _dynamicDocument;

                    return true;
                }

                public void Reset()
                {
                    throw new NotSupportedException();
                }

                public TEnumeratorType Current { get; private set; }

                object IEnumerator.Current => Current;

                public void Dispose()
                {
                }
            }
        }

        private class MultipleIndexingFunctionsEnumerator<TMultipleIndexingFunctionsEnumeratorType> : IEnumerable where TMultipleIndexingFunctionsEnumeratorType : AbstractDynamicObject, new()
        {
            private readonly Enumerator<TMultipleIndexingFunctionsEnumeratorType> _enumerator;

            public MultipleIndexingFunctionsEnumerator(List<IndexingFunc> funcs, DynamicIteratorOfCurrentItemWrapper<TMultipleIndexingFunctionsEnumeratorType> iterationOfCurrentDocument)
            {
                _enumerator = new Enumerator<TMultipleIndexingFunctionsEnumeratorType>(funcs, iterationOfCurrentDocument.GetEnumerator());
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return _enumerator;
            }

            public void Reset()
            {
                _enumerator.Reset();
            }

            private class Enumerator<TEnumeratorType> : IEnumerator where TEnumeratorType : AbstractDynamicObject
            {
                private readonly List<IndexingFunc> _funcs;
                private readonly IEnumerator<TEnumeratorType> _docEnumerator;
                private readonly TEnumeratorType[] _currentDoc = new TEnumeratorType[1];
                private int _index;
                private bool _moveNextDoc = true;
                private IEnumerator _currentFuncEnumerator;

                public Enumerator(List<IndexingFunc> funcs, IEnumerator<TEnumeratorType> docEnumerator)
                {
                    _funcs = funcs;
                    _docEnumerator = docEnumerator;
                }

                public bool MoveNext()
                {
                    if (_moveNextDoc && _docEnumerator.MoveNext() == false)
                        return false;

                    _moveNextDoc = false;

                    while (true)
                    {
                        if (_currentFuncEnumerator == null)
                        {
                            _currentDoc[0] = _docEnumerator.Current;
                            _currentFuncEnumerator = _funcs[_index](_currentDoc).GetEnumerator();
                        }

                        if (_currentFuncEnumerator.MoveNext() == false)
                        {
                            _currentFuncEnumerator = null;
                            _index++;

                            if (_index < _funcs.Count)
                                continue;

                            _index = 0;
                            _moveNextDoc = true;

                            return false;
                        }

                        Current = _currentFuncEnumerator.Current;
                        return true;
                    }
                }

                public void Reset()
                {
                    _index = 0;
                    _moveNextDoc = true;
                    _currentFuncEnumerator = null;
                }

                public object Current { get; private set; }
            }
        }
    }
}
