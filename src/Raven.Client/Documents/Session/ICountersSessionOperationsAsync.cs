//-----------------------------------------------------------------------
// <copyright file="ICountersSessionOperationsAsync.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.Counters;

namespace Raven.Client.Documents.Session
{
    /// <summary>
    ///     Advanced async counters session operations
    /// </summary>
    public interface ICountersSessionOperationsAsync : ICountersSessionOperationsBase
    {
        /// <summary>
        /// Returns all the counters for a specific document.
        /// </summary>
        Task<Dictionary<string, long>> GetAsync(string documentId);

        /// <summary>
        /// Returns all the counters for an entity.
        /// </summary>
        Task<Dictionary<string, long>> GetAsync(object entity);

        /// <summary>
        /// Returns the counter by the document id and counter name.
        /// </summary>
        Task<long?> GetAsync(string documentId, string counter);

        /// <summary>
        /// Returns the counter by entity and counter name.
        /// </summary>
        Task<long?> GetAsync(object entity, string counter);

        /// <summary>
        /// Returns CountersDetail on all the specified counters, by document id and counter names
        /// <param name="documentId">the document which holds the counters</param>
        /// <param name="counters">counters names</param>
        /// </summary>
        Task<Dictionary<string, long>> GetAsync(string documentId, IEnumerable<string> counters);

        /// <summary>
        /// Returns CountersDetail on all the specified counters
        /// <param name="entity">instance of entity of the document which holds the counter</param>
        /// <param name="counters">counters names</param>
        /// </summary>
        Task<Dictionary<string, long>> GetAsync(object entity, IEnumerable<string> counters);

        /// <summary>
        /// Returns CountersDetail on all the specified counters
        /// <param name="documentId">the document which holds the counters</param>
        /// <param name="counters">counters names</param>
        /// </summary>
        Task<Dictionary<string, long>> GetAsync(string documentId, params string[] counters);

        /// <summary>
        /// Returns CountersDetail on all the specified counters
        /// <param name="entity">instance of entity of the document which holds the counter</param>
        /// <param name="counters">counters names</param>
        /// </summary>
        Task<Dictionary<string, long>> GetAsync(object entity, params string[] counters);

    }
}
