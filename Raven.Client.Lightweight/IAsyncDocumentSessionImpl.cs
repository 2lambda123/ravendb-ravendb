//-----------------------------------------------------------------------
// <copyright file="IAsyncDocumentSession.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Raven.Client.Document;

namespace Raven.Client
{
	/// <summary>
	/// Interface for document session using async approaches
	/// </summary>
	public interface IAsyncDocumentSessionImpl : IAsyncDocumentSession
	{
		DocumentConvention Conventions { get; }

        Task<T[]> LoadAsyncInternal<T>(string[] ids, KeyValuePair<string, Type>[] includes);
	}
}