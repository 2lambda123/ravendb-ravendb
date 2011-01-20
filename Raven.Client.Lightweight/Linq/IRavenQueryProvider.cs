//-----------------------------------------------------------------------
// <copyright file="IRavenQueryProvider.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Linq;
using Raven.Client.Document;
using Raven.Database.Data;

namespace Raven.Client.Linq
{
	/// <summary>
	/// Extension for the built-in <see cref="IQueryProvider"/> allowing for Raven specific operations
	/// </summary>
	public interface IRavenQueryProvider : IQueryProvider
	{
		/// <summary>
		/// Callback to get the results of the query
		/// </summary>
		void AfterQueryExecuted(Action<QueryResult> afterQueryExecuted);

		/// <summary>
		/// Customizes the query using the specified action
		/// </summary>
		void Customize(Action<IDocumentQueryCustomization> action);

		/// <summary>
		/// Gets the name of the index.
		/// </summary>
		/// <value>The name of the index.</value>
		string IndexName { get; }

		/// <summary>
		/// Get the query generator
		/// </summary>
		IDocumentQueryGenerator QueryGenerator { get; }

		/// <summary>
		/// Change the result type for the query provider
		/// </summary>
		IRavenQueryProvider For<S>();
	}
}
