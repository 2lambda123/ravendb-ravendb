﻿using System;
using System.Diagnostics;
using System.Text;
using Raven.Client.Documents.Commands.MultiGet;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.Client.Documents.Queries;
using Raven.Client.Util;
using Sparrow.Json;

namespace Raven.Client.Documents.Session.Operations.Lazy
{
    internal class LazyGetCompareExchangeValueOperation<T> : ILazyOperation
    {
        private readonly ClusterTransactionOperationsBase _clusterSession;
        private readonly DocumentConventions _conventions;
        private readonly string _key;

        public LazyGetCompareExchangeValueOperation(ClusterTransactionOperationsBase clusterSession, DocumentConventions conventions, string key)
        {
            _clusterSession = clusterSession ?? throw new ArgumentNullException(nameof(clusterSession));
            _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
            _key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public object Result { get; private set; }

        public QueryResult QueryResult => throw new NotImplementedException();

        public bool RequiresRetry { get; private set; }

        public GetRequest CreateRequest(JsonOperationContext ctx)
        {
            if (_clusterSession.IsTracked(_key))
            {
                Result = _clusterSession.GetCompareExchangeValueFromSessionInternal<T>(_key, out _);
                return null;
            }

            var queryBuilder = new StringBuilder("?key=")
                .Append(_key);

            return new GetRequest
            {
                Url = "/cmpxchg",
                Method = HttpMethods.Get,
                Query = queryBuilder.ToString()
            };
        }

        public void HandleResponse(GetResponse response)
        {
            if (response.ForceRetry)
            {
                Result = null;
                RequiresRetry = true;
                return;
            }

            if (response.Result != null)
            {
                var value = CompareExchangeValueResultParser<BlittableJsonReaderObject>.GetValue((BlittableJsonReaderObject)response.Result, _conventions);
                if (value != null)
                    _clusterSession.RegisterCompareExchangeValue(value);
            }

            if (_clusterSession.IsTracked(_key) == false)
                _clusterSession.RegisterMissingCompareExchangeValue(_key);

            Result = _clusterSession.GetCompareExchangeValueFromSessionInternal<T>(_key, out var notTracked);
            Debug.Assert(notTracked == false, "notTracked == false");
        }
    }
}
