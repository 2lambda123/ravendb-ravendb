﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Client.Documents.Operations.TimeSeries
{
    public class TimeSeriesOperation
    {
        public List<AppendOperation> Appends;

        public List<RemoveOperation> Removals;

        public string Name;

        internal static TimeSeriesOperation Parse(BlittableJsonReaderObject input)
        {
            if (input.TryGet(nameof(Name), out string name) == false || name == null)
                ThrowMissingProperty(nameof(Name));

            var result = new TimeSeriesOperation
            {
                Name = name
            };

            if (input.TryGet(nameof(Appends), out BlittableJsonReaderArray operations) && operations != null)
            {
                var sorted = new SortedList<long, AppendOperation>();

                foreach (var op in operations)
                {
                    if (!(op is BlittableJsonReaderObject bjro))
                    {
                        ThrowNotBlittableJsonReaderObjectOperation(op);
                        return null; //never hit
                    }

                    var append = AppendOperation.Parse(bjro);

                    sorted[append.Timestamp.Ticks] = append;
                }

                result.Appends = new List<AppendOperation>(sorted.Values);
            }

            if (input.TryGet(nameof(Removals), out operations) && operations != null)
            {
                result.Removals = new List<RemoveOperation>();

                foreach (var op in operations)
                {
                    if (!(op is BlittableJsonReaderObject bjro))
                    {
                        ThrowNotBlittableJsonReaderObjectOperation(op);
                        return null; //never hit
                    }

                    result.Removals.Add(RemoveOperation.Parse(bjro));
                }
            }

            return result;
        }

        private static void ThrowNotBlittableJsonReaderObjectOperation(object op)
        {
            throw new InvalidDataException($"'Operations' should contain items of type BlittableJsonReaderObject only, but got {op.GetType()}");
        }

        private static void ThrowMissingProperty(string prop)
        {
            throw new InvalidDataException($"Missing '{prop}' property on 'TimeSeriesOperation'");
        }
        
        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Name)] = Name,
                [nameof(Appends)] = Appends?.Select(x => x.ToJson()),
                [nameof(Removals)] = Removals?.Select(x => x.ToJson())
            };
        }

        public class AppendOperation
        {
            public DateTime Timestamp;
            public double[] Values;
            public string Tag;

            internal static AppendOperation Parse(BlittableJsonReaderObject input)
            {
                if (input.TryGet(nameof(Timestamp), out DateTime ts) == false)
                    throw new InvalidDataException($"Missing '{nameof(Timestamp)}' property");

                if (input.TryGet(nameof(Values), out BlittableJsonReaderArray values) == false || values == null)
                    throw new InvalidDataException($"Missing '{nameof(Values)}' property");

                input.TryGet(nameof(Tag), out string tag); // optional

                var doubleValues = new double[values.Length];
                for (int i = 0; i < doubleValues.Length; i++)
                {
                    doubleValues[i] = values.GetByIndex<double>(i);
                }

                var op = new AppendOperation
                {
                    Timestamp = ts,
                    Values = doubleValues,
                    Tag = tag
                };

                return op;
            }

            public DynamicJsonValue ToJson()
            {
                var djv = new DynamicJsonValue
                {
                    [nameof(Timestamp)] = Timestamp,
                    [nameof(Values)] = new DynamicJsonArray(Values.Select(x => (object)x)),
                };

                if (Tag != null)
                    djv[nameof(Tag)] = Tag;

                return djv;
            }
        }

        public class RemoveOperation
        {
            public DateTime From, To;

            internal static RemoveOperation Parse(BlittableJsonReaderObject input)
            {
                if (input.TryGet(nameof(From), out DateTime from) == false)
                    throw new InvalidDataException($"Missing '{nameof(From)}' property");

                if (input.TryGet(nameof(To), out DateTime to) == false)
                    throw new InvalidDataException($"Missing '{nameof(To)}' property");

                var op = new RemoveOperation
                {
                    From = from,
                    To = to
                };

                return op;
            }

            public DynamicJsonValue ToJson()
            {
                return new DynamicJsonValue
                {
                    [nameof(From)] = From,
                    [nameof(To)] = To
                };
            }
        }
    }
}
