//-----------------------------------------------------------------------
// <copyright file="DocumentQuery.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
#if !NET_3_5
using System.Threading.Tasks;
using Raven.Client.Client.Async;
using Raven.Database.Linq;
#endif
using Newtonsoft.Json.Linq;
using Raven.Abstractions.Data;
using Raven.Client.Client;
using Raven.Client.Exceptions;
using Raven.Client.Linq;
using Raven.Database.Data;
using Raven.Database.Extensions;
using Raven.Database.Indexing;

namespace Raven.Client.Document
{
    /// <summary>
    ///   A query against a Raven index
    /// </summary>
    public abstract class AbstractDocumentQuery<T, TSelf> : IDocumentQueryCustomization, IRavenQueryInspector
        where TSelf : AbstractDocumentQuery<T, TSelf>
    {
        /// <summary>
        /// Whatever to negate the next operation
        /// </summary>
        protected bool negate;
#if !SILVERLIGHT
        /// <summary>
        /// The database commands to use
        /// </summary>
        protected readonly IDatabaseCommands theDatabaseCommands;
#endif
#if !NET_3_5
        /// <summary>
        /// Async database commands to use
        /// </summary>
        protected readonly IAsyncDatabaseCommands theAsyncDatabaseCommands;
#endif
        /// <summary>
        /// The index to query
        /// </summary>
        protected readonly string indexName;
        private int currentClauseDepth;

        private KeyValuePair<string, string> lastEquality;

        /// <summary>
        ///   The list of fields to project directly from the index
        /// </summary>
        protected readonly string[] projectionFields;

        /// <summary>
        /// The query listeners for this query
        /// </summary>
        protected readonly IDocumentQueryListener[] queryListeners;
        /// <summary>
        /// The session for this query
        /// </summary>
        protected readonly InMemoryDocumentSessionOperations theSession;

        /// <summary>
        ///   The cutoff date to use for detecting staleness in the index
        /// </summary>
        public DateTime? cutoff;

        /// <summary>
        ///   The fields to order the results by
        /// </summary>
        public string[] orderByFields = new string[0];


        /// <summary>
        ///   The types to sort the fields by (NULL if not specified)
        /// </summary>
        public HashSet<KeyValuePair<string, Type>> sortByHints = new HashSet<KeyValuePair<string, Type>>();

        /// <summary>
        ///   The page size to use when querying the index
        /// </summary>
        public int pageSize = 128;

        private QueryResult queryResult;

        /// <summary>
        /// The query to use
        /// </summary>
        protected StringBuilder theQueryText = new StringBuilder();

        /// <summary>
        ///   which record to start reading from
        /// </summary>
        protected int start;

        /// <summary>
        /// Timeout for this query
        /// </summary>
        protected TimeSpan timeout;
        /// <summary>
        /// Should we wait for non stale results
        /// </summary>
        protected bool theWaitForNonStaleResults;
        private readonly HashSet<string> includes = new HashSet<string>();
        /// <summary>
        /// What aggregated operation to execute
        /// </summary>
        protected AggregationOperation aggregationOp;
        /// <summary>
        /// Fields to group on
        /// </summary>
        protected string[] groupByFields;
#if !NET_3_5
        private Task<QueryResult> queryResultTask;
#endif

        /// <summary>
        ///   Gets the current includes on this query
        /// </summary>
        public IEnumerable<String> Includes
        {
            get { return includes; }
        }

        /// <summary>
        ///   Get the name of the index being queried
        /// </summary>
        public string IndexQueried
        {
            get { return indexName; }
        }

#if !SILVERLIGHT
        /// <summary>
        ///   Grant access to the database commands
        /// </summary>
        public IDatabaseCommands DatabaseCommands
        {
            get { return theDatabaseCommands; }
        }
#endif

#if !NET_3_5
        /// <summary>
        ///   Grant access to the async database commands
        /// </summary>
        public IAsyncDatabaseCommands AsyncDatabaseCommands
        {
            get { return theAsyncDatabaseCommands; }
        }
#endif

#if !SILVERLIGHT
        /// <summary>
        ///   Gets the session associated with this document query
        /// </summary>
        public IDocumentSession Session
        {
            get { return (IDocumentSession) theSession; }
        }
#endif

        /// <summary>
        ///   Gets the query text built so far
        /// </summary>
        protected StringBuilder QueryText
        {
            get { return theQueryText; }
        }


#if !SILVERLIGHT && !NET_3_5
        /// <summary>
        ///   Initializes a new instance of the <see cref = "DocumentQuery&lt;T&gt;" /> class.
        /// </summary>
        /// <param name = "theSession">The session.</param>
        /// <param name = "databaseCommands">The database commands.</param>
        /// <param name = "indexName">Name of the index.</param>
        /// <param name = "projectionFields">The projection fields.</param>
        public AbstractDocumentQuery(InMemoryDocumentSessionOperations theSession,
                                     IDatabaseCommands databaseCommands,
                                     string indexName,
                                     string[] projectionFields,
                                     IDocumentQueryListener[] queryListeners)
            : this(theSession, databaseCommands, null, indexName, projectionFields, queryListeners)
        {
        }
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="DocumentQuery&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="databaseCommands">The database commands.</param>
#if !NET_3_5
        /// <param name="asyncDatabaseCommands">The async database commands</param>
#endif

        /// <param name = "indexName">Name of the index.</param>
        /// <param name = "projectionFields">The projection fields.</param>
        /// <param name = "theSession">The session.</param>
        public AbstractDocumentQuery(InMemoryDocumentSessionOperations theSession,
#if !SILVERLIGHT
                                     IDatabaseCommands databaseCommands,
#endif
#if !NET_3_5
                                     IAsyncDatabaseCommands asyncDatabaseCommands,
#endif
                                     string indexName,
                                     string[] projectionFields,
                                     IDocumentQueryListener[] queryListeners)
        {
#if !SILVERLIGHT
            this.theDatabaseCommands = databaseCommands;
#endif
            this.projectionFields = projectionFields;
            this.queryListeners = queryListeners;
            this.indexName = indexName;
            this.theSession = theSession;
#if !NET_3_5
            this.theAsyncDatabaseCommands = asyncDatabaseCommands;
#endif
        }

        /// <summary>
        ///   Initializes a new instance of the <see cref = "DocumentQuery&lt;T&gt;" /> class.
        /// </summary>
        /// <param name = "other">The other.</param>
        protected AbstractDocumentQuery(AbstractDocumentQuery<T, TSelf> other)
        {
#if !SILVERLIGHT
            theDatabaseCommands = other.theDatabaseCommands;
#endif
#if !NET_3_5
            theAsyncDatabaseCommands = other.theAsyncDatabaseCommands;
#endif
            indexName = other.indexName;
            projectionFields = other.projectionFields;
            theSession = other.theSession;
            cutoff = other.cutoff;
            orderByFields = other.orderByFields;
            sortByHints = other.sortByHints;
            pageSize = other.pageSize;
            theQueryText = other.theQueryText;
            start = other.start;
            timeout = other.timeout;
            theWaitForNonStaleResults = other.theWaitForNonStaleResults;
            includes = other.includes;
            queryListeners = other.queryListeners;
        }

        #region TSelf Members

        /// <summary>
        ///   Includes the specified path in the query, loading the document specified in that path
        /// </summary>
        /// <param name = "path">The path.</param>
        IDocumentQueryCustomization IDocumentQueryCustomization.Include(string path)
        {
            Include(path);
            return this;
        }

        /// <summary>
        ///   EXPERT ONLY: Instructs the query to wait for non stale results for the specified wait timeout.
        ///   This shouldn't be used outside of unit tests unless you are well aware of the implications
        /// </summary>
        /// <param name = "waitTimeout">The wait timeout.</param>
        IDocumentQueryCustomization IDocumentQueryCustomization.WaitForNonStaleResults(TimeSpan waitTimeout)
        {
            WaitForNonStaleResults(waitTimeout);
            return this;
        }

        /// <summary>
        /// Selects the specified fields directly from the index
        /// </summary>
        /// <typeparam name="TProjection">The type of the projection.</typeparam>
        /// <param name="fields">The fields.</param>
        IDocumentQueryCustomization IDocumentQueryCustomization.CreateQueryForSelectedFields<TProjection>(params string[] fields)
        {
            return CreateQueryForSelectedFields<TProjection>(fields);
        }


        /// <summary>
        /// Selects the specified fields directly from the index
        /// </summary>
        protected abstract IDocumentQueryCustomization CreateQueryForSelectedFields<TProjection>(string[] fields);

        /// <summary>
        ///   Filter matches to be inside the specified radius
        /// </summary>
        /// <param name = "radius">The radius.</param>
        /// <param name = "latitude">The latitude.</param>
        /// <param name = "longitude">The longitude.</param>
        IDocumentQueryCustomization IDocumentQueryCustomization.WithinRadiusOf(double radius, double latitude,
                                                                               double longitude)
        {
            GenerateQueryWithinRadiusOf(radius, latitude, longitude);
            return this;
        }

        /// <summary>
        ///   Filter matches to be inside the specified radius
        /// </summary>
        /// <param name = "radius">The radius.</param>
        /// <param name = "latitude">The latitude.</param>
        /// <param name = "longitude">The longitude.</param>
        protected abstract object GenerateQueryWithinRadiusOf(double radius, double latitude, double longitude);

        /// <summary>
        ///   EXPERT ONLY: Instructs the query to wait for non stale results.
        ///   This shouldn't be used outside of unit tests unless you are well aware of the implications
        /// </summary>
        IDocumentQueryCustomization IDocumentQueryCustomization.WaitForNonStaleResults()
        {
            WaitForNonStaleResults();
            return this;
        }

        /// <summary>
        ///   Includes the specified path in the query, loading the document specified in that path
        /// </summary>
        /// <param name = "path">The path.</param>
        IDocumentQueryCustomization IDocumentQueryCustomization.Include<TResult>(Expression<Func<TResult, object>> path)
        {
            Include(path.ToPropertyPath());
            return this;
        }

        /// <summary>
        ///   Instruct the query to wait for non stale result for the specified wait timeout.
        /// </summary>
        /// <param name = "waitTimeout">The wait timeout.</param>
        /// <returns></returns>
        public void WaitForNonStaleResults(TimeSpan waitTimeout)
        {
            theWaitForNonStaleResults = true;
            timeout = waitTimeout;
        }

#if !SILVERLIGHT
        /// <summary>
        ///   Gets the query result
        ///   Execute the query the first time that this is called.
        /// </summary>
        /// <value>The query result.</value>
        public QueryResult QueryResult
        {
            get { return queryResult ?? (queryResult = GetQueryResult()); }
        }
#endif

#if !NET_3_5
        /// <summary>
        ///   Gets the query result
        ///   Execute the query the first time that this is called.
        /// </summary>
        /// <value>The query result.</value>
        public Task<QueryResult> QueryResultAsync
        {
            get { return queryResultTask ?? (queryResultTask = GetQueryResultAsync()); }
        }
#endif

        /// <summary>
        ///   Gets the fields for projection
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetProjectionFields()
        {
            return projectionFields ?? Enumerable.Empty<string>();
        }

        /// <summary>
        ///   Adds an ordering for a specific field to the query
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "descending">if set to <c>true</c> [descending].</param>
        public void AddOrder(string fieldName, bool descending)
        {
            AddOrder(fieldName, descending, null);
        }

        /// <summary>
        ///   Adds an ordering for a specific field to the query and specifies the type of field for sorting purposes
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "descending">if set to <c>true</c> [descending].</param>
        /// <param name = "fieldType">the type of the field to be sorted.</param>
        public void AddOrder(string fieldName, bool descending, Type fieldType)
        {
            fieldName = EnsureValidFieldName(fieldName);
            fieldName = descending ? "-" + fieldName : fieldName;
            orderByFields = orderByFields.Concat(new[] {fieldName}).ToArray();
            sortByHints.Add(new KeyValuePair<string, Type>(fieldName, fieldType));
        }

        /// <summary>
        ///   Gets the enumerator.
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
#if !SILVERLIGHT
            var sp = Stopwatch.StartNew();
#else
			var startTime = DateTime.Now;
#endif
            do
            {
                try
                {
#if !SILVERLIGHT
                    queryResult = QueryResult;
#else
                    queryResult = QueryResultAsync.Result;
#endif
                    foreach (var include in queryResult.Includes)
                    {
                        var metadata = include.Value<JObject>("@metadata");

                        theSession.TrackEntity<object>(metadata.Value<string>("@id"),
                                                    include,
                                                    metadata);
                    }
                    var list = queryResult.Results
                        .Select(Deserialize)
                        .ToList();
                    return list.GetEnumerator();
                }
                catch (NonAuthoritiveInformationException)
                {
#if !SILVERLIGHT
                    if (sp.Elapsed > theSession.NonAuthoritiveInformationTimeout)
                        throw;
#else
					if ((DateTime.Now - startTime) > theSession.NonAuthoritiveInformationTimeout)
						throw;

#endif
                    queryResult = null;
                    // we explicitly do NOT want to consider retries for non authoritive information as 
                    // additional request counted against the session quota
                    theSession.DecrementRequestCount();
                }
            } while (true);
        }

        /// <summary>
        ///   Includes the specified path in the query, loading the document specified in that path
        /// </summary>
        /// <param name = "path">The path.</param>
        public void Include(string path)
        {
            includes.Add(path);
        }

        /// <summary>
        ///   This function exists solely to forbid in memory where clause on IDocumentQuery, because
        ///   that is nearly always a mistake.
        /// </summary>
        [Obsolete(
            @"
You cannot issue an in memory filter - such as Where(x=>x.Name == ""Ayende"") - on IDocumentQuery. 
This is likely a bug, because this will execute the filter in memory, rather than in RavenDB.
Consider using session.Query<T>() instead of session.LuceneQuery<T>. The session.Query<T>() method fully supports Linq queries, while session.LuceneQuery<T>() is intended for lower level API access.
If you really want to do in memory filtering on the data returned from the query, you can use: session.LuceneQuery<T>().ToList().Where(x=>x.Name == ""Ayende"")
"
            , true)]
        public IEnumerable<T> Where(Func<T, bool> predicate)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///   Includes the specified path in the query, loading the document specified in that path
        /// </summary>
        /// <param name = "path">The path.</param>
        public void Include(Expression<Func<T, object>> path)
        {
            Include(path.ToPropertyPath());
        }

        /// <summary>
        ///   Takes the specified count.
        /// </summary>
        /// <param name = "count">The count.</param>
        /// <returns></returns>
        public void Take(int count)
        {
            pageSize = count;
        }

        /// <summary>
        ///   Skips the specified count.
        /// </summary>
        /// <param name = "count">The count.</param>
        /// <returns></returns>
        public void Skip(int count)
        {
            start = count;
        }

        /// <summary>
        ///   Filter the results from the index using the specified where clause.
        /// </summary>
        /// <param name = "whereClause">The where clause.</param>
        public void Where(string whereClause)
        {
            if (theQueryText.Length > 0)
            {
                theQueryText.Append(" ");
            }

            theQueryText.Append(whereClause);
        }

        /// <summary>
        ///   Matches exact value
        /// </summary>
        /// <remarks>
        ///   Defaults to NotAnalyzed
        /// </remarks>
        public void WhereEquals(string fieldName, object value)
        {
            WhereEquals(fieldName, value, true, false);
        }

        /// <summary>
        ///   Matches exact value
        /// </summary>
        /// <remarks>
        ///   Defaults to allow wildcards only if analyzed
        /// </remarks>
        public void WhereEquals(string fieldName, object value, bool isAnalyzed)
        {
            WhereEquals(fieldName, value, isAnalyzed, isAnalyzed);
        }


        /// <summary>
        ///   Simplified method for opening a new clause within the query
        /// </summary>
        /// <returns></returns>
        public void OpenSubclause()
        {
            currentClauseDepth++;
            if (theQueryText.Length > 0 && theQueryText[theQueryText.Length - 1] != '(')
            {
                theQueryText.Append(" ");
            }
            NegateIfNeeded();
            theQueryText.Append("(");
        }

        ///<summary>
        ///  Instruct the index to group by the specified fields using the specified aggregation operation
        ///</summary>
        ///<remarks>
        ///  This is only valid on dynamic indexes queries
        ///</remarks>
        public void GroupBy(AggregationOperation aggregationOperation, params string[] fieldsToGroupBy)
        {
            groupByFields = fieldsToGroupBy;
            aggregationOp = aggregationOperation;
        }

        /// <summary>
        ///   Simplified method for closing a clause within the query
        /// </summary>
        /// <returns></returns>
        public void CloseSubclause()
        {
            currentClauseDepth--;
            theQueryText.Append(")");
        }

        /// <summary>
        ///   Matches exact value
        /// </summary>
        public void WhereEquals(string fieldName, object value, bool isAnalyzed, bool allowWildcards)
        {
            fieldName = EnsureValidFieldName(fieldName);
            var transformToEqualValue = TransformToEqualValue(value, isAnalyzed, allowWildcards);
            lastEquality = new KeyValuePair<string, string>(fieldName, transformToEqualValue);
            if (theQueryText.Length > 0 && theQueryText[theQueryText.Length - 1] != '(')
            {
                theQueryText.Append(" ");
            }

            NegateIfNeeded();

            theQueryText.Append(fieldName);
            theQueryText.Append(":");
            theQueryText.Append(transformToEqualValue);
        }

        private string EnsureValidFieldName(string fieldName)
        {
            if (theSession == null)
                return fieldName;
            if (theSession.Conventions == null)
                return fieldName;
            var identityProperty = theSession.Conventions.GetIdentityProperty(typeof (T));
            if (identityProperty != null && identityProperty.Name == fieldName)
            {
                fieldName = "__document_id";
            }
            return fieldName;
        }

        ///<summary>
        /// Negate the next operation
        ///</summary>
        public void NegateNext()
        {
            negate = !negate;
        }

        private void NegateIfNeeded()
        {
            if (negate == false)
                return;
            negate = false;
            theQueryText.Append("-");
        }

        /// <summary>
        ///   Matches substrings of the field
        /// </summary>
        public void WhereContains(string fieldName, object value)
        {
            WhereEquals(fieldName, value, true, true);
        }

        /// <summary>
        ///   Matches fields which starts with the specified value.
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "value">The value.</param>
        public void WhereStartsWith(string fieldName, object value)
        {
            // NOTE: doesn't fully match StartsWith semantics
            WhereEquals(fieldName, String.Concat(value, "*"), true, true);
        }

        /// <summary>
        ///   Matches fields which ends with the specified value.
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "value">The value.</param>
        public void WhereEndsWith(string fieldName, object value)
        {
            // http://lucene.apache.org/java/2_4_0/queryparsersyntax.html#Wildcard%20Searches
            // You cannot use a * or ? symbol as the first character of a search

            // NOTE: doesn't fully match EndsWith semantics
            WhereEquals(fieldName, String.Concat("*", value), true, true);
        }

        /// <summary>
        ///   Matches fields where the value is between the specified start and end, exclusive
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "start">The start.</param>
        /// <param name = "end">The end.</param>
        /// <returns></returns>
        public void WhereBetween(string fieldName, object start, object end)
        {
            if (theQueryText.Length > 0)
            {
                theQueryText.Append(" ");
            }

            if ((start ?? end) != null)
                sortByHints.Add(new KeyValuePair<string, Type>(fieldName, (start ?? end).GetType()));

            NegateIfNeeded();

            fieldName = EnsureValidFieldName(fieldName);

            theQueryText.Append(fieldName).Append(":{");
            theQueryText.Append(start == null ? "*" : TransformToRangeValue(start));
            theQueryText.Append(" TO ");
            theQueryText.Append(end == null ? "NULL" : TransformToRangeValue(end));
            theQueryText.Append("}");
        }

        /// <summary>
        ///   Matches fields where the value is between the specified start and end, inclusive
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "start">The start.</param>
        /// <param name = "end">The end.</param>
        /// <returns></returns>
        public void WhereBetweenOrEqual(string fieldName, object start, object end)
        {
            if (theQueryText.Length > 0)
            {
                theQueryText.Append(" ");
            }
            if ((start ?? end) != null)
                sortByHints.Add(new KeyValuePair<string, Type>(fieldName, (start ?? end).GetType()));

            NegateIfNeeded();

            fieldName = EnsureValidFieldName(fieldName);
            theQueryText.Append(fieldName).Append(":[");
            theQueryText.Append(start == null ? "*" : TransformToRangeValue(start));
            theQueryText.Append(" TO ");
            theQueryText.Append(end == null ? "NULL" : TransformToRangeValue(end));
            theQueryText.Append("]");
        }

        /// <summary>
        ///   Matches fields where the value is greater than the specified value
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "value">The value.</param>
        public void WhereGreaterThan(string fieldName, object value)
        {
            WhereBetween(fieldName, value, null);
        }

        /// <summary>
        ///   Matches fields where the value is greater than or equal to the specified value
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "value">The value.</param>
        public void WhereGreaterThanOrEqual(string fieldName, object value)
        {
            WhereBetweenOrEqual(fieldName, value, null);
        }

        /// <summary>
        ///   Matches fields where the value is less than the specified value
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "value">The value.</param>
        public void WhereLessThan(string fieldName, object value)
        {
            WhereBetween(fieldName, null, value);
        }

        /// <summary>
        ///   Matches fields where the value is less than or equal to the specified value
        /// </summary>
        /// <param name = "fieldName">Name of the field.</param>
        /// <param name = "value">The value.</param>
        public void WhereLessThanOrEqual(string fieldName, object value)
        {
            WhereBetweenOrEqual(fieldName, null, value);
        }

        /// <summary>
        ///   Add an AND to the query
        /// </summary>
        public void AndAlso()
        {
            if (theQueryText.Length < 1)
            {
                throw new InvalidOperationException("Missing where clause");
            }

            theQueryText.Append(" AND");
        }

        /// <summary>
        ///   Add an OR to the query
        /// </summary>
        public void OrElse()
        {
            if (theQueryText.Length < 1)
            {
                throw new InvalidOperationException("Missing where clause");
            }

            theQueryText.Append(" OR");
        }

        /// <summary>
        ///   Specifies a boost weight to the last where clause.
        ///   The higher the boost factor, the more relevant the term will be.
        /// </summary>
        /// <param name = "boost">boosting factor where 1.0 is default, less than 1.0 is lower weight, greater than 1.0 is higher weight</param>
        /// <returns></returns>
        /// <remarks>
        ///   http://lucene.apache.org/java/2_4_0/queryparsersyntax.html#Boosting%20a%20Term
        /// </remarks>
        public void Boost(decimal boost)
        {
            if (theQueryText.Length < 1)
            {
                throw new InvalidOperationException("Missing where clause");
            }

            if (boost <= 0m)
            {
                throw new ArgumentOutOfRangeException("Boost factor must be a positive number");
            }

            if (boost != 1m)
            {
                // 1.0 is the default
                theQueryText.Append("^").Append(boost);
            }
        }

        /// <summary>
        ///   Specifies a fuzziness factor to the single word term in the last where clause
        /// </summary>
        /// <param name = "fuzzy">0.0 to 1.0 where 1.0 means closer match</param>
        /// <returns></returns>
        /// <remarks>
        ///   http://lucene.apache.org/java/2_4_0/queryparsersyntax.html#Fuzzy%20Searches
        /// </remarks>
        public void Fuzzy(decimal fuzzy)
        {
            if (theQueryText.Length < 1)
            {
                throw new InvalidOperationException("Missing where clause");
            }

            if (fuzzy < 0m || fuzzy > 1m)
            {
                throw new ArgumentOutOfRangeException("Fuzzy distance must be between 0.0 and 1.0");
            }

            var ch = theQueryText[theQueryText.Length - 1];
            if (ch == '"' || ch == ']')
            {
                // this check is overly simplistic
                throw new InvalidOperationException("Fuzzy factor can only modify single word terms");
            }

            theQueryText.Append("~");
            if (fuzzy != 0.5m)
            {
                // 0.5 is the default
                theQueryText.Append(fuzzy);
            }
        }

        /// <summary>
        ///   Specifies a proximity distance for the phrase in the last where clause
        /// </summary>
        /// <param name = "proximity">number of words within</param>
        /// <returns></returns>
        /// <remarks>
        ///   http://lucene.apache.org/java/2_4_0/queryparsersyntax.html#Proximity%20Searches
        /// </remarks>
        public void Proximity(int proximity)
        {
            if (theQueryText.Length < 1)
            {
                throw new InvalidOperationException("Missing where clause");
            }

            if (proximity < 1)
            {
                throw new ArgumentOutOfRangeException("proximity", "Proximity distance must be positive number");
            }

            if (theQueryText[theQueryText.Length - 1] != '"')
            {
                // this check is overly simplistic
                throw new InvalidOperationException("Proximity distance can only modify a phrase");
            }

            theQueryText.Append("~").Append(proximity);
        }

        /// <summary>
        ///   Order the results by the specified fields
        /// </summary>
        /// <remarks>
        ///   The fields are the names of the fields to sort, defaulting to sorting by ascending.
        ///   You can prefix a field name with '-' to indicate sorting by descending or '+' to sort by ascending
        /// </remarks>
        /// <param name = "fields">The fields.</param>
        public void OrderBy(params string[] fields)
        {
            orderByFields = orderByFields.Concat(fields).ToArray();
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of now.
        /// </summary>
        /// <returns></returns>
        public void WaitForNonStaleResultsAsOfNow()
        {
            theWaitForNonStaleResults = true;
            cutoff = DateTime.UtcNow;
            timeout = TimeSpan.FromSeconds(15);
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of now for the specified timeout.
        /// </summary>
        /// <param name = "waitTimeout">The wait timeout.</param>
        /// <returns></returns>
        IDocumentQueryCustomization IDocumentQueryCustomization.WaitForNonStaleResultsAsOfNow(TimeSpan waitTimeout)
        {
            WaitForNonStaleResultsAsOfNow(waitTimeout);
            return this;
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of the cutoff date.
        /// </summary>
        /// <param name = "cutOff">The cut off.</param>
        /// <returns></returns>
        IDocumentQueryCustomization IDocumentQueryCustomization.WaitForNonStaleResultsAsOf(DateTime cutOff)
        {
            WaitForNonStaleResultsAsOf(cutOff);
            return this;
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of the cutoff date for the specified timeout
        /// </summary>
        /// <param name = "cutOff">The cut off.</param>
        /// <param name = "waitTimeout">The wait timeout.</param>
        IDocumentQueryCustomization IDocumentQueryCustomization.WaitForNonStaleResultsAsOf(DateTime cutOff,
                                                                                           TimeSpan waitTimeout)
        {
            WaitForNonStaleResultsAsOf(cutOff, waitTimeout);
            return this;
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of now.
        /// </summary>
        /// <returns></returns>
        IDocumentQueryCustomization IDocumentQueryCustomization.WaitForNonStaleResultsAsOfNow()
        {
            WaitForNonStaleResultsAsOfNow();
            return this;
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of now for the specified timeout.
        /// </summary>
        /// <param name = "waitTimeout">The wait timeout.</param>
        /// <returns></returns>
        public void WaitForNonStaleResultsAsOfNow(TimeSpan waitTimeout)
        {
            theWaitForNonStaleResults = true;
            cutoff = DateTime.UtcNow;
            timeout = waitTimeout;
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of the cutoff date.
        /// </summary>
        /// <param name = "cutOff">The cut off.</param>
        /// <returns></returns>
        public void WaitForNonStaleResultsAsOf(DateTime cutOff)
        {
            theWaitForNonStaleResults = true;
            cutoff = cutOff.ToUniversalTime();
        }

        /// <summary>
        ///   Instructs the query to wait for non stale results as of the cutoff date for the specified timeout
        /// </summary>
        /// <param name = "cutOff">The cut off.</param>
        /// <param name = "waitTimeout">The wait timeout.</param>
        public void WaitForNonStaleResultsAsOf(DateTime cutOff, TimeSpan waitTimeout)
        {
            theWaitForNonStaleResults = true;
            cutoff = cutOff.ToUniversalTime();
            timeout = waitTimeout;
        }

        /// <summary>
        ///   EXPERT ONLY: Instructs the query to wait for non stale results.
        ///   This shouldn't be used outside of unit tests unless you are well aware of the implications
        /// </summary>
        public void WaitForNonStaleResults()
        {
            theWaitForNonStaleResults = true;
            timeout = TimeSpan.FromSeconds(15);
        }

        #endregion

#if !NET_3_5
        private Task<QueryResult> GetQueryResultAsync()
        {
            theSession.IncrementRequestCount();
            var startTime = DateTime.Now;

            var query = theQueryText.ToString();

            Debug.WriteLine(string.Format("Executing query '{0}' on index '{1}' in '{2}'",
                                          query, indexName, theSession.StoreIdentifier));

            var indexQuery = GenerateIndexQuery(query);

            AddOperationHeaders(theAsyncDatabaseCommands.OperationsHeaders.Add);

            return GetQueryResultTaskResult(query, indexQuery, startTime);
        }

        private Task<QueryResult> GetQueryResultTaskResult(string query, IndexQuery indexQuery, DateTime startTime)
        {
            return theAsyncDatabaseCommands.QueryAsync(indexName, indexQuery, includes.ToArray())
                .ContinueWith(task =>
                {
                    if (theWaitForNonStaleResults && task.Result.IsStale)
                    {
                        var elapsed1 = DateTime.Now - startTime;
                        if (elapsed1 > timeout)
                        {
                            throw new TimeoutException(
                                string.Format("Waited for {0:#,#}ms for the query to return non stale result.",
                                              elapsed1.TotalMilliseconds));
                        }
                        Debug.WriteLine(
                            string.Format(
                                "Stale query results on non stable query '{0}' on index '{1}' in '{2}', query will be retried",
                                query, indexName, theSession.StoreIdentifier));


                        return TaskEx.Delay(100)
                            .ContinueWith(_ => GetQueryResultTaskResult(query, indexQuery, startTime))
                            .Unwrap();
                    }

                    Debug.WriteLine(string.Format("Query returned {0}/{1} results", task.Result.Results.Count,
                                                  task.Result.TotalResults));
                    return task;
                }).Unwrap();
        }
#endif

#if !SILVERLIGHT
        /// <summary>
        ///   Gets the query result.
        /// </summary>
        /// <returns></returns>
        protected virtual QueryResult GetQueryResult()
        {
            foreach (var documentQueryListener in queryListeners)
            {
                documentQueryListener.BeforeQueryExecuted(this);
            }
            theSession.IncrementRequestCount();
            var sp = Stopwatch.StartNew();
            while (true)
            {
                var query = theQueryText.ToString();

                Debug.WriteLine(string.Format("Executing query '{0}' on index '{1}' in '{2}'",
                                              query, indexName, theSession.StoreIdentifier));

                var indexQuery = GenerateIndexQuery(query);

                AddOperationHeaders(theDatabaseCommands.OperationsHeaders.Add);

                var result = theDatabaseCommands.Query(indexName, indexQuery, includes.ToArray());
                if (theWaitForNonStaleResults && result.IsStale)
                {
                    if (sp.Elapsed > timeout)
                    {
                        sp.Stop();
                        throw new TimeoutException(
                            string.Format("Waited for {0:#,#}ms for the query to return non stale result.",
                                          sp.ElapsedMilliseconds));
                    }
                    Debug.WriteLine(
                        string.Format(
                            "Stale query results on non stable query '{0}' on index '{1}' in '{2}', query will be retried",
                            query, indexName, theSession.StoreIdentifier));
                    Thread.Sleep(100);
                    continue;
                }
                Debug.WriteLine(string.Format("Query returned {0}/{1} results", result.Results.Count,
                                              result.TotalResults));
                return result;
            }
        }
#endif

        private SortOptions FromPrimitiveTypestring(string type)
        {
            switch (type)
            {
                case "Int16":
                    return SortOptions.Short;
                case "Int32":
                    return SortOptions.Int;
                case "Int64":
                    return SortOptions.Long;
                case "Single":
                    return SortOptions.Float;
                case "String":
                    return SortOptions.String;
                default:
                    return SortOptions.String;
            }
        }


        /// <summary>
        ///   Generates the index query.
        /// </summary>
        /// <param name = "query">The query.</param>
        /// <returns></returns>
        protected virtual IndexQuery GenerateIndexQuery(string query)
        {
            return new IndexQuery
            {
                GroupBy = groupByFields,
                AggregationOperation = aggregationOp,
                Query = query,
                PageSize = pageSize,
                Start = start,
                Cutoff = cutoff,
                SortedFields = orderByFields.Select(x => new SortedField(x)).ToArray(),
                FieldsToFetch = projectionFields
            };
        }

        private void AddOperationHeaders(Action<string, string> addOperationHeader)
        {
            foreach (var sortByHint in sortByHints)
            {
                if (sortByHint.Value == null)
                    continue;

                addOperationHeader(
                    string.Format("SortHint_{0}", Uri.EscapeDataString(sortByHint.Key.Trim('-'))),
                    FromPrimitiveTypestring(sortByHint.Value.Name).ToString());
            }
        }

        private T Deserialize(JObject result)
        {
            var metadata = result.Value<JObject>("@metadata");
            if (projectionFields != null && projectionFields.Length > 0
                // we asked for a projection directly from the index
                || metadata == null)
                // we aren't querying a document, we are probably querying a map reduce index result
            {
                if (typeof (T) == typeof (JObject))
                    return (T) (object) result;

#if !NET_3_5
                if (typeof (T) == typeof (object))
                {
                    return (T) (object) new DynamicJsonObject(result);
                }
#endif
                var deserializedResult =
                    (T) theSession.Conventions.CreateSerializer().Deserialize(new JTokenReader(result), typeof (T));

                var documentId = result.Value<string>("__document_id"); //check if the result contain the reserved name
                if (string.IsNullOrEmpty(documentId) == false)
                {
                    // we need to make an addtional check, since it is possible that a value was explicitly stated
                    // for the identity property, in which case we don't want to override it.
                    var identityProperty = theSession.Conventions.GetIdentityProperty(typeof (T));
                    if (identityProperty == null ||
                        (result.Property(identityProperty.Name) == null ||
                            result.Property(identityProperty.Name).Value.Type == JTokenType.Null))
                    {
                        theSession.TrySetIdentity(deserializedResult, documentId);
                    }
                }

                return deserializedResult;
            }
            return theSession.TrackEntity<T>(metadata.Value<string>("@id"),
                                          result,
                                          metadata);
        }

        private static string TransformToEqualValue(object value, bool isAnalyzed, bool allowWildcards)
        {
            if (value == null)
            {
                return "[[NULL_VALUE]]";
            }

            if (value is bool)
            {
                return (bool) value ? "true" : "false";
            }

            if (value is DateTime)
            {
                return DateTools.DateToString((DateTime) value, DateTools.Resolution.MILLISECOND);
            }

            var escaped = RavenQuery.Escape(Convert.ToString(value, CultureInfo.InvariantCulture),
                                            allowWildcards && isAnalyzed);

            if (value is string == false)
                return escaped;

            return isAnalyzed ? escaped : String.Concat("[[", escaped, "]]");
        }

        private static string TransformToRangeValue(object value)
        {
            if (value == null)
                return "[[NULL_VALUE]]";

            if (value is int)
                return NumberUtil.NumberToString((int) value);
            if (value is long)
                return NumberUtil.NumberToString((long) value);
            if (value is decimal)
                return NumberUtil.NumberToString((double) (decimal) value);
            if (value is double)
                return NumberUtil.NumberToString((double) value);
            if (value is float)
                return NumberUtil.NumberToString((float) value);
            if (value is DateTime)
                return DateTools.DateToString((DateTime) value, DateTools.Resolution.MILLISECOND);

            return RavenQuery.Escape(value.ToString(), false);
        }

        /// <summary>
        ///   Returns a <see cref = "System.String" /> that represents this instance.
        /// </summary>
        /// <returns>
        ///   A <see cref = "System.String" /> that represents this instance.
        /// </returns>
        public override string ToString()
        {
            if (currentClauseDepth != 0)
            {
                throw new InvalidOperationException(
                    string.Format("A clause was not closed correctly within this query, current clause depth = {0}",
                                  currentClauseDepth));
            }
            return theQueryText.ToString();
        }

        /// <summary>
        ///   The last term that we asked the query to use equals on
        /// </summary>
        public KeyValuePair<string, string> GetLastEqualityTerm()
        {
            return lastEquality;
        }
    }
}