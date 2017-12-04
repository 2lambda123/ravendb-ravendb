using System;
using System.Linq.Expressions;
using Raven.Client.Extensions;

namespace Raven.Client.Documents.Queries.Facets
{
    public interface IFacetOperations<T>
    {
        IFacetOperations<T> WithDisplayName(string displayName);

        IFacetOperations<T> WithOptions(FacetOptions options);

        IFacetOperations<T> SumOn(Expression<Func<T, object>> path);

        IFacetOperations<T> MinOn(Expression<Func<T, object>> path);

        IFacetOperations<T> MaxOn(Expression<Func<T, object>> path);

        IFacetOperations<T> AverageOn(Expression<Func<T, object>> path);
    }

    public interface IFacetBuilder<T>
    {
        IFacetOperations<T> ByRanges(Expression<Func<T, bool>> path, params Expression<Func<T, bool>>[] paths);

        IFacetOperations<T> ByField(Expression<Func<T, object>> path);

        IFacetOperations<T> ByField(string fieldName);
    }

    internal class FacetBuilder<T> : IFacetBuilder<T>, IFacetOperations<T>
    {
        private RangeFacet _range;
        private Facet _default;

        public IFacetOperations<T> ByRanges(Expression<Func<T, bool>> path, params Expression<Func<T, bool>>[] paths)
        {
            if (path == null) 
                throw new ArgumentNullException(nameof(path));

            if (_range == null)
                _range = new RangeFacet();

            _range.Ranges.Add(RangeFacet<T>.Parse(path));

            if (paths != null)
            {
                foreach (var p in paths)
                {
                    _range.Ranges.Add(RangeFacet<T>.Parse(p));
                }
            }

            return this;
        }

        public IFacetOperations<T> ByField(Expression<Func<T, object>> path)
        {
            return ByField(path.ToPropertyPath('_'));
        }

        public IFacetOperations<T> ByField(string fieldName)
        {
            if (_default == null)
                _default = new Facet();

            _default.FieldName = fieldName;

            return this;
        }

        public IFacetOperations<T> WithOptions(FacetOptions options)
        {
            Facet.Options = options;
            return this;
        }

        public IFacetOperations<T> WithDisplayName(string displayName)
        {
            Facet.DisplayFieldName = displayName;
            return this;
        }

        public IFacetOperations<T> SumOn(Expression<Func<T, object>> path)
        {
            Facet.Aggregations[FacetAggregation.Sum] = path.ToPropertyPath();
            return this;
        }

        public IFacetOperations<T> MinOn(Expression<Func<T, object>> path)
        {
            Facet.Aggregations[FacetAggregation.Min] = path.ToPropertyPath();
            return this;
        }

        public IFacetOperations<T> MaxOn(Expression<Func<T, object>> path)
        {
            Facet.Aggregations[FacetAggregation.Max] = path.ToPropertyPath();
            return this;
        }

        public IFacetOperations<T> AverageOn(Expression<Func<T, object>> path)
        {
            Facet.Aggregations[FacetAggregation.Average] = path.ToPropertyPath();
            return this;
        }

        internal FacetBase Facet
        {
            get
            {
                if (_default != null)
                    return _default;

                return _range;
            }
        }
    }
}
