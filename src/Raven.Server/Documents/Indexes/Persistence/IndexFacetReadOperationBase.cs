using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Raven.Client.Documents.Queries.Facets;
using Raven.Server.Documents.Indexes.Static.Spatial;
using Raven.Server.Documents.Queries;
using Raven.Server.Documents.Queries.Facets;
using Raven.Server.Documents.Queries.Timings;
using Raven.Server.ServerWide.Context;
using Sparrow.Logging;

namespace Raven.Server.Documents.Indexes.Persistence;

public abstract class IndexFacetReadOperationBase : IndexOperationBase
{
    protected readonly QueryBuilderFactories _queryBuilderFactories;

    public IndexFacetReadOperationBase(Index index, QueryBuilderFactories queryBuilderFactories, Logger logger) : base(index, logger)
    {
        _queryBuilderFactories = queryBuilderFactories;
    }

    public abstract List<FacetResult> FacetedQuery(FacetQuery facetQuery, QueryTimingsScope queryTimings, DocumentsOperationContext context,
        Func<string, SpatialField> getSpatialField, CancellationToken token);

    protected static void CompleteFacetCalculationsStage(Dictionary<string, FacetedQueryParser.FacetResult> results, IndexQueryServerSide query)
    {
        if (query.ReturnRawFacetResults)
            return;

        foreach (var result in results)
        {
            foreach (var value in result.Value.Result.Values)
            {
                if (value.Average.HasValue == false)
                    continue;

                if (value.Count == 0)
                    value.Average = double.NaN;
                else
                    value.Average = value.Average / value.Count;
            }
        }
    }

    protected class FacetValues
    {
        private static readonly FacetAggregationField Default = new FacetAggregationField();

        private readonly bool _legacy;
        private readonly Dictionary<FacetAggregationField, FacetValue> _values = new Dictionary<FacetAggregationField, FacetValue>();

        public int Count;
        public bool Any;

        public FacetValues(bool legacy)
        {
            _legacy = legacy;
        }

        public void AddDefault(string range)
        {
            Any = true;
            _values[Default] = new FacetValue {Range = range};
        }

        public void Add(FacetAggregationField field, string range)
        {
            if (_legacy)
            {
                if (Any)
                    return;

                AddDefault(range);
                return;
            }

            Any = true;
            _values[field] = new FacetValue {Range = range, Name = string.IsNullOrWhiteSpace(field.DisplayName) ? field.Name : field.DisplayName};
        }

        public FacetValue Get(FacetAggregationField field)
        {
            if (_legacy)
                return _values[Default];

            return _values[field];
        }

        public IEnumerable<FacetValue> GetAll()
        {
            return _values.Values;
        }

        public void IncrementCount(int count)
        {
            Count += count;

            foreach (var facetValue in _values)
                facetValue.Value.Count += count;
        }
    }

    protected static void UpdateFacetResults(Dictionary<string, FacetedQueryParser.FacetResult> results, IndexQueryServerSide query,
        Dictionary<string, Dictionary<string, FacetValues>> facetsByName)
    {
        foreach (var result in results)
        {
            if (result.Value.Ranges != null && result.Value.Ranges.Count > 0)
                continue;

            var valuesCount = 0;
            var valuesSumOfCounts = 0;
            var values = new List<FacetValue>();
            List<string> allTerms;
            if (facetsByName?.TryGetValue(result.Key, out var groups) is null or false || groups == null)
                continue;

            int start;
            int pageSize;

            if (query.ReturnRawFacetResults == false)
            {
                allTerms = GetAllTermsSorted(result.Value.Options.TermSortMode, groups, x => x.Value.Count);

                start = result.Value.Options.Start;
                pageSize = Math.Min(allTerms.Count, result.Value.Options.PageSize);
            }
            else
            {
                allTerms = new List<string>(groups.Keys);

                start = 0;
                pageSize = int.MaxValue;
            }

            foreach (var term in allTerms.Skip(start).TakeWhile(term => valuesCount < pageSize))
            {
                valuesCount++;

                if (groups.TryGetValue(term, out var facetValues) == false || facetValues == null || facetValues.Any == false)
                {
                    values.Add(new FacetValue {Range = term});
                    continue;
                }

                values.AddRange(facetValues.GetAll());

                valuesSumOfCounts += facetValues.Count;
            }

            var previousHits = allTerms.Take(start).Sum(allTerm =>
            {
                if (groups.TryGetValue(allTerm, out var facetValues) == false || facetValues == null || facetValues.Any == false)
                    return 0;

                return facetValues.Count;
            });

            result.Value.Result = new FacetResult
            {
                Name = result.Key,
                Values = values,
                RemainingTermsCount = allTerms.Count - (start + valuesCount),
                RemainingHits = groups.Values.Sum(x => x.Count) - (previousHits + valuesSumOfCounts)
            };

            if (result.Value.Options.IncludeRemainingTerms)
                result.Value.Result.RemainingTerms = allTerms.Skip(start + valuesCount).ToList();
        }
    }

    internal static List<string> GetAllTermsSorted<T>(FacetTermSortMode sortMode, Dictionary<string, T> values, Func<KeyValuePair<string, T>, int> countSelector)
    {
        List<string> allTerms;

        switch (sortMode)
        {
            case FacetTermSortMode.ValueAsc:
                allTerms = new List<string>(values.OrderBy(x => x.Key).ThenBy(countSelector).Select(x => x.Key));
                break;
            case FacetTermSortMode.ValueDesc:
                allTerms = new List<string>(values.OrderByDescending(x => x.Key).ThenBy(countSelector).Select(x => x.Key));
                break;
            case FacetTermSortMode.CountAsc:
                allTerms = new List<string>(values.OrderBy(countSelector).ThenBy(x => x.Key).Select(x => x.Key));
                break;
            case FacetTermSortMode.CountDesc:
                allTerms = new List<string>(values.OrderByDescending(countSelector).ThenBy(x => x.Key).Select(x => x.Key));
                break;
            default:
                throw new ArgumentException($"Could not understand '{sortMode}'");
        }

        return allTerms;
    }

    public override void Dispose()
    {
    }
}
