﻿using System;
using Raven.Client.Documents.Queries;

namespace Raven.Client.Documents.Session
{
    public sealed class GroupByField
    {
        public string FieldName { get; set; }

        public string ProjectedName { get; set; }
    }

    /// <inheritdoc cref="IGroupByDocumentQuery{T}"/>
    public sealed class GroupByDocumentQuery<T> : IGroupByDocumentQuery<T>
    {
        private readonly DocumentQuery<T> _query;

        public GroupByDocumentQuery(DocumentQuery<T> query)
        {
            _query = query;
        }

        /// <inheritdoc />
        public IGroupByDocumentQuery<T> SelectKey(string fieldName = null, string projectedName = null)
        {
            _query.GroupByKey(fieldName, projectedName);
            return this;
        }

        /// <inheritdoc />
        public IDocumentQuery<T> SelectSum(GroupByField field, params GroupByField[] fields)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            _query.GroupBySum(field.FieldName, field.ProjectedName);

            if (fields == null || fields.Length == 0)
                return _query;

            foreach (var f in fields)
                _query.GroupBySum(f.FieldName, f.ProjectedName);

            return _query;
        }

        /// <inheritdoc />
        public IDocumentQuery<T> SelectCount(string projectedName = null)
        {
            _query.GroupByCount(projectedName);
            return _query;
        }

        /// <inheritdoc />
        public IGroupByDocumentQuery<T> Filter(Action<IFilterFactory<T>> builder, int limit)
        {
            using (_query.SetFilterMode(true))
            {
                var f = new FilterFactory<T>(_query, limit);
                builder.Invoke(f);
            }
            
            return this;
        }
    }

    /// <inheritdoc cref="IAsyncGroupByDocumentQuery{T}"/>
    public sealed class AsyncGroupByDocumentQuery<T> : IAsyncGroupByDocumentQuery<T>
    {
        private readonly AsyncDocumentQuery<T> _query;

        public AsyncGroupByDocumentQuery(AsyncDocumentQuery<T> query)
        {
            _query = query;
        }

        /// <inheritdoc />
        public IAsyncGroupByDocumentQuery<T> SelectKey(string fieldName = null, string projectedName = null)
        {
            _query.GroupByKey(fieldName, projectedName);
            return this;
        }

        /// <inheritdoc />
        public IAsyncDocumentQuery<T> SelectSum(GroupByField field, params GroupByField[] fields)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            _query.GroupBySum(field.FieldName, field.ProjectedName);

            if (fields == null || fields.Length == 0)
                return _query;

            foreach (var f in fields)
                _query.GroupBySum(f.FieldName, f.ProjectedName);

            return _query;
        }

        /// <inheritdoc />
        public IAsyncDocumentQuery<T> SelectCount(string projectedName = null)
        {
            _query.GroupByCount(projectedName);
            return _query;
        }

        /// <inheritdoc />
        public IAsyncGroupByDocumentQuery<T> Filter(Action<IFilterFactory<T>> builder, int limit)
        {
            using (_query.SetFilterMode(true))
            {
                var f = new FilterFactory<T>(_query, limit);
                builder.Invoke(f);
            }
           
            return this;
        }
    }
}
