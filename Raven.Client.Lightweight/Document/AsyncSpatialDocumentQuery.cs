﻿#if !NET_3_5

using System.Linq;
using Raven.Client.Client;
using Raven.Client.Client.Async;
using Raven.Database.Data;

namespace Raven.Client.Document
{
    /// <summary>
    /// A spatial query allows to perform spatial filtering on the index
    /// </summary>
    public class AsyncSpatialDocumentQuery<T> : AsyncDocumentQuery<T>
    {
        private readonly double lat, lng, radius;
        private readonly bool sortByDistance;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpatialDocumentQuery&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="databaseCommands">The database commands.</param>
        /// <param name="indexName">Name of the index.</param>
        /// <param name="projectionFields">The projection fields.</param>
        public AsyncSpatialDocumentQuery(
            InMemoryDocumentSessionOperations session,
#if !SILVERLIGHT
            IDatabaseCommands databaseCommands, 
#endif
#if !NET_3_5
            IAsyncDatabaseCommands asyncDatabaseCommands,
#endif
            string indexName, 
            string[] projectionFields,
            IDocumentQueryListener[] queryListeners)
            : base(session, 
#if !SILVERLIGHT
                   databaseCommands, 
#endif
#if !NET_3_5
                   asyncDatabaseCommands,
#endif
                   indexName, 
                   projectionFields,
                   queryListeners)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpatialDocumentQuery&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="documentQuery">The document query.</param>
        /// <param name="radius">The radius.</param>
        /// <param name="latitude">The latitude.</param>
        /// <param name="longitude">The longitude.</param>
        public AsyncSpatialDocumentQuery(AsyncDocumentQuery<T> documentQuery, double radius, double latitude, double longitude)
            : base(documentQuery)
        {
            this.radius = radius;
            lat = latitude;
            lng = longitude;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SpatialDocumentQuery&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="documentQuery">The document query.</param>
        /// <param name="sortByDistance">if set to <c>true</c> the query will be sorted by distance.</param>
        public AsyncSpatialDocumentQuery(AsyncDocumentQuery<T> documentQuery, bool sortByDistance)
            : base(documentQuery)
        {
            this.sortByDistance = sortByDistance;

            var other = documentQuery as AsyncSpatialDocumentQuery<T>;
            if (other == null)
                return;

            radius = other.radius;
            lat = other.lat;
            lng = other.lng;				
        }

        
        /// <summary>
        /// Generates the index query.
        /// </summary>
        /// <param name="query">The query.</param>
        /// <returns></returns>
        protected override IndexQuery GenerateIndexQuery(string query)
        {
            return new SpatialIndexQuery
            {
                Query = query,
                PageSize = pageSize,
                Start = start,
                Cutoff = cutoff,
                SortedFields = orderByFields.Select(x => new SortedField(x)).ToArray(),
                FieldsToFetch = projectionFields,
                Latitude = lat,
                Longitude = lng,
                Radius = radius,
                SortByDistance = sortByDistance
            };
        }
    }
}
#endif