﻿using System;
using System.Collections.Generic;
using System.Text;
using Raven.Client.Documents.Queries.Facets;

namespace Raven.Client.Documents.Session.Tokens
{
    public class FacetToken : QueryToken
    {
        private readonly string _facetSetupDocumentKey;
        private readonly string _fieldName;
        private readonly string _alias;
        private readonly List<string> _ranges;
        private readonly string _optionsParameterName;

        private readonly List<FacetAggregationToken> _aggregations;

        public string Name => _alias ?? _fieldName;

        private FacetToken(string facetSetupDocumentKey)
        {
            _facetSetupDocumentKey = facetSetupDocumentKey;
        }

        private FacetToken(string fieldName, string alias, List<string> ranges, string optionsParameterName)
        {
            _fieldName = fieldName;
            _alias = alias;
            _ranges = ranges;
            _optionsParameterName = optionsParameterName;
            _aggregations = new List<FacetAggregationToken>();
        }

        public static FacetToken Create(string facetSetupDocumentKey)
        {
            if (string.IsNullOrWhiteSpace(facetSetupDocumentKey))
                throw new ArgumentNullException(nameof(facetSetupDocumentKey));

            return new FacetToken(facetSetupDocumentKey);
        }

        public static FacetToken Create(Facet facet, Func<object, string> addQueryParameter)
        {
            var optionsParameterName = facet.Options != null && facet.Options != FacetOptions.Default ? addQueryParameter(facet.Options) : null;

            var token = new FacetToken(facet.Name, facet.DisplayName, facet.Ranges, optionsParameterName);

            foreach (var aggregation in facet.Aggregations)
            {
                FacetAggregationToken aggregationToken;
                switch (aggregation.Key)
                {
                    case FacetAggregation.Max:
                        aggregationToken = FacetAggregationToken.Max(aggregation.Value);
                        break;
                    case FacetAggregation.Min:
                        aggregationToken = FacetAggregationToken.Min(aggregation.Value);
                        break;
                    case FacetAggregation.Average:
                        aggregationToken = FacetAggregationToken.Average(aggregation.Value);
                        break;
                    case FacetAggregation.Sum:
                        aggregationToken = FacetAggregationToken.Sum(aggregation.Value);
                        break;
                    default:
                        throw new InvalidOperationException("TODO ppekrol");
                }

                token._aggregations.Add(aggregationToken);
            }

            return token;
        }

        public override void WriteTo(StringBuilder writer)
        {
            writer
                .Append("facet(");

            if (_facetSetupDocumentKey != null)
            {
                writer
                    .Append("id('")
                    .Append(_facetSetupDocumentKey)
                    .Append("'))");

                return;
            }

            writer.Append(_fieldName);

            foreach (var range in _ranges)
            {
                writer
                    .Append(", ")
                    .Append(range);
            }

            foreach (var aggregation in _aggregations)
            {
                writer.Append(", ");

                aggregation.WriteTo(writer);
            }

            if (string.IsNullOrWhiteSpace(_optionsParameterName) == false)
            {
                writer
                    .Append(", ")
                    .Append(_optionsParameterName);
            }

            writer.Append(")");

            if (string.IsNullOrWhiteSpace(_alias) || string.Equals(_fieldName, _alias))
                return;

            writer
                .Append(" as ")
                .Append(_alias);
        }

        private class FacetAggregationToken : QueryToken
        {
            private readonly string _fieldName;
            private readonly FacetAggregation _aggregation;

            private FacetAggregationToken(string fieldName, FacetAggregation aggregation)
            {
                _fieldName = fieldName;
                _aggregation = aggregation;
            }

            public override void WriteTo(StringBuilder writer)
            {
                switch (_aggregation)
                {
                    case FacetAggregation.Max:
                        writer
                            .Append("max(")
                            .Append(_fieldName)
                            .Append(")");
                        break;
                    case FacetAggregation.Min:
                        writer
                            .Append("min(")
                            .Append(_fieldName)
                            .Append(")");
                        break;
                    case FacetAggregation.Average:
                        writer
                            .Append("avg(")
                            .Append(_fieldName)
                            .Append(")");
                        break;
                    case FacetAggregation.Sum:
                        writer
                            .Append("sum(")
                            .Append(_fieldName)
                            .Append(")");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            public static FacetAggregationToken Max(string fieldName)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new ArgumentNullException(nameof(fieldName));

                return new FacetAggregationToken(fieldName, FacetAggregation.Max);
            }

            public static FacetAggregationToken Min(string fieldName)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new ArgumentNullException(nameof(fieldName));

                return new FacetAggregationToken(fieldName, FacetAggregation.Min);
            }

            public static FacetAggregationToken Average(string fieldName)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new ArgumentNullException(nameof(fieldName));

                return new FacetAggregationToken(fieldName, FacetAggregation.Average);
            }

            public static FacetAggregationToken Sum(string fieldName)
            {
                if (string.IsNullOrWhiteSpace(fieldName))
                    throw new ArgumentNullException(nameof(fieldName));

                return new FacetAggregationToken(fieldName, FacetAggregation.Sum);
            }
        }
    }
}
