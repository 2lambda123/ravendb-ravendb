using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;

namespace Raven.Client.Documents.Queries
{
    public class RavenQuery
    {
        //public static TimeSeriesAggregationMethods TimeSeriesAggregation;

        public static T Load<T>(string id)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static IEnumerable<T> Load<T>(IEnumerable<string> ids)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static T Raw<T>(string js)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static T Raw<T>(T path, string js)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static IMetadataDictionary Metadata<T>(T instance)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static T CmpXchg<T>(string key)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static long? Counter(string name)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static long? Counter(string docId, string name)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static long? Counter(object documentInstance, string name)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static IRavenQueryable<TimeSeriesValue> TimeSeries(string name)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static IRavenQueryable<TimeSeriesValue> TimeSeries(object documentInstance, string name)
        {
            throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
        }

        public static class TimeSeriesAggregations
        {
            public static double?[] Max()
            {
                throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
            }

            public static double?[] Min()
            {
                throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
            }

            public static double?[] Sum()
            {
                throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
            }

            public static double?[] Average()
            {
                throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
            }

            public static double?[] First()
            {
                throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
            }

            public static double?[] Last()
            {
                throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
            }

            public static double?[] Count()
            {
                throw new NotSupportedException("This method is here for strongly type support of server side call during Linq queries and should never be directly called");
            }
        }
    }
}
