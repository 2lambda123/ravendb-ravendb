//-----------------------------------------------------------------------
// <copyright file="IRavenQueryable.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Linq;
using Raven.Client.Indexes;

namespace Raven.Client.Linq
{
	/// <summary>
	/// An implementation of <see cref="IOrderedQueryable{T}"/> with Raven specific operation
	/// </summary>
	public interface IRavenQueryable<T> : IOrderedQueryable<T>
	{
		/// <summary>
		/// Provide statistics about the query, such as total count of matching records
		/// </summary>
		IRavenQueryable<T> Statistics(out RavenQueryStatistics stats);

		/// <summary>
		/// Customizes the query using the specified action
		/// </summary>
		/// <param name="action">The action.</param>
		/// <returns></returns>
		IRavenQueryable<T> Customize(Action<IDocumentQueryCustomization> action);

        /// <summary>
        /// Specifies a result transformer to use on the results
        /// </summary>
        /// <typeparam name="TTransformer"></typeparam>
        /// <typeparam name="TResult"></typeparam>
        /// <returns></returns>
	    IRavenQueryable<TResult> TransformWith<TTransformer, TResult>() where TTransformer : AbstractTransformerCreationTask<T>, new();

	}
}