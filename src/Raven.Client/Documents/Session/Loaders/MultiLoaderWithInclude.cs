﻿//-----------------------------------------------------------------------
// <copyright file="MultiLoaderWithInclude.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Raven.Client.Extensions;

namespace Raven.Client.Documents.Session.Loaders
{
    /// <summary>
    /// Fluent implementation for specifying include paths
    /// for loading documents
    /// </summary>
    internal class MultiLoaderWithInclude<T> : ILoaderWithInclude<T>
    {
        private readonly IDocumentSessionImpl _session;
        private readonly List<string> _includes = new List<string>();
        private List<string> _counters;

        /// <summary>
        /// Includes the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        public ILoaderWithInclude<T> Include(string path)
        {
            _includes.Add(path);
            return this;
        }

        /// <summary>
        /// Includes the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        public ILoaderWithInclude<T> Include(Expression<Func<T, string>> path)
        {
            return Include(path.ToPropertyPath());
        }

        /// <summary>
        /// Includes the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        public ILoaderWithInclude<T> Include<TInclude>(Expression<Func<T, string>> path)
        {
            return Include(IncludesUtil.GetPrefixedIncludePath<TInclude>(path.ToPropertyPath(), _session.Conventions));
        }

        /// <summary>
        /// Includes the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        public ILoaderWithInclude<T> Include(Expression<Func<T, IEnumerable<string>>> path)
        {
            return Include(path.ToPropertyPath());
        }

        /// <summary>
        /// Includes the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        public ILoaderWithInclude<T> Include<TInclude>(Expression<Func<T, IEnumerable<string>>> path)
        {
            return Include(IncludesUtil.GetPrefixedIncludePath<TInclude>(path.ToPropertyPath(), _session.Conventions));
        }

        /// <summary>
        /// Includes a counter of the document to be loaded, by counter name
        /// <param name="name">Name of the counter to include.</param>
        /// </summary>
        public ILoaderWithInclude<T> IncludeCounter(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentNullException(nameof(name));

            if (_counters == null)
                _counters = new List<string>();

            _counters.Add(name);
            return this;
        }

        /// <summary>
        /// Includes counters of the document to be loaded, by counters names
        /// <param name="names">Names of the counters to include.</param>
        /// </summary>
        public ILoaderWithInclude<T> IncludeCounters(string[] names)
        {
            if (_counters == null)
                _counters = new List<string>();

            if (names == null)
                throw new ArgumentNullException(nameof(names));

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException("IncludeCounters(string[] names) : 'names' should not contain null or whitespace elements");
                _counters.Add(name);
            }
            return this;
        }

        /// <summary>
        /// Includes all the counters of the document to be loaded
        /// </summary>
        public ILoaderWithInclude<T> IncludeCounters()
        {
            return IncludeCounters(new string[0]);
        }

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <param name="ids">The ids.</param>
        /// <returns></returns>
        public Dictionary<string, T> Load(params string[] ids)
        {
            return _session.LoadInternal<T>(ids, _includes.ToArray(), _counters?.ToArray());
        }

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <param name="ids">The ids.</param>
        /// <returns></returns>
        public Dictionary<string, T> Load(IEnumerable<string> ids)
        {
            return _session.LoadInternal<T>(ids.ToArray(), _includes.ToArray(), _counters?.ToArray());
        }

        /// <summary>
        /// Loads the specified id.
        /// </summary>
        /// <param name="id">The id.</param>
        public T Load(string id)
        {
            return _session.LoadInternal<T>(new[] { id }, _includes.ToArray(), _counters?.ToArray()).Values.FirstOrDefault();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiLoaderWithInclude{T}"/> class.
        /// </summary>
        /// <param name="session">The session.</param>
        internal MultiLoaderWithInclude(IDocumentSessionImpl session)
        {
            _session = session;
        }

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="ids">The ids.</param>
        public Dictionary<string, TResult> Load<TResult>(params string[] ids)
        {
            return _session.LoadInternal<TResult>(ids, _includes.ToArray(), _counters?.ToArray());
        }

        /// <summary>
        /// Loads the specified ids.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="ids">The ids.</param>
        public Dictionary<string, TResult> Load<TResult>(IEnumerable<string> ids)
        {
            return _session.LoadInternal<TResult>(ids.ToArray(), _includes.ToArray(), _counters?.ToArray());
        }

        /// <summary>
        /// Loads the specified id.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="id">The id.</param>
        public TResult Load<TResult>(string id)
        {
            return Load<TResult>(new[] { id }).Values.FirstOrDefault();
        }
    }
}
