//-----------------------------------------------------------------------
// <copyright file="IDocumentQuery.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
#if !NET_3_5
using System.Threading.Tasks;
#endif
using Raven.Abstractions.Data;
using Raven.Database.Data;

namespace Raven.Client
{
	/// <summary>
	/// A query against a Raven index
	/// </summary>
	public interface IDocumentQuery<T> : IEnumerable<T>, IDocumentQueryBase<T, IDocumentQuery<T>>
	{

        /// <summary>
        /// Selects the specified fields directly from the index
        /// </summary>
        /// <typeparam name="TProjection">The type of the projection.</typeparam>
        /// <param name="fields">The fields.</param>
        IDocumentQuery<TProjection> SelectFields<TProjection>(params string[] fields);

#if !SILVERLIGHT
		/// <summary>
		/// Gets the query result
		/// Execute the query the first time that this is called.
		/// </summary>
		/// <value>The query result.</value>
		QueryResult QueryResult { get; }
#endif
	}
}
