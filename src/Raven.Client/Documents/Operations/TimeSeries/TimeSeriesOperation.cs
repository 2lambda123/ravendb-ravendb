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

        private const string TimeSeriesFormat = "TimeFormat";
        private static readonly long EpochTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;

        internal enum TimeFormat
        {
            DotNetTicks,
            UnixTimeInMs,
            UnixTimeInNs
        }

        private static long FromUnixMs(long unixMs)
        {
            return unixMs * 10_000 + EpochTicks;
        }

        private static long FromUnixNs(long unixNs)
        {
            return unixNs / 100 + EpochTicks;
        }

        internal static TimeSeriesOperation ParseForBulkInsert(BlittableJsonReaderObject input)
        {
            if (input.TryGet(nameof(Name), out string name) == false || name == null)
                ThrowMissingProperty(nameof(Name));

            input.TryGet(TimeSeriesFormat, out TimeFormat format);

            var result = new TimeSeriesOperation
            {
                Name = name
            };

            if (input.TryGet(nameof(Appends), out BlittableJsonReaderArray operations) == false || operations == null)
                ThrowMissingProperty(nameof(Appends));

            var sorted = new SortedList<long, AppendOperation>();

            foreach (var op in operations)
            {
                if (!(op is BlittableJsonReaderArray bjro))
                {
                    ThrowNotBlittableJsonReaderArrayOperation(op);
                    return null; //never hit
                }

                var time = GetLong(bjro[0]);

                switch (format)
                {
                    case TimeFormat.UnixTimeInMs:
                        time = FromUnixMs(time);
                        break;
                    case TimeFormat.UnixTimeInNs:
                        time = FromUnixNs(time);
                        break;
                    case TimeFormat.DotNetTicks:
                        break;
                    default:
                        throw new ArgumentException($"Unknown time-format '{format}'");
                }

                var append = new AppendOperation
                {
                    Timestamp = new DateTime(time)
                };

                var numberOfValues = GetLong(bjro[1]);
                var doubleValues = new double[numberOfValues];

                for (var i = 0; i < numberOfValues; i++)
                {
                    var obj = bjro[i + 2];
                    switch (obj)
                    {
                        case long l:
                            // when we send the number without the decimal point
                            // this is the same as what Convert.ToDouble is doing
                            doubleValues[i] = l;
                            break;

                        case LazyNumberValue lnv:
                            doubleValues[i] = lnv;
                            break;

                        default:
                            doubleValues[i] = Convert.ToDouble(obj);
                            break;
                    }
                }

                append.Values = doubleValues;

                var tagIndex = 2 + numberOfValues;
                if (bjro.Length > tagIndex)
                {
                    if (BlittableJsonReaderObject.ChangeTypeToString(bjro[(int)tagIndex], out string tagAsString) == false)
                        ThrowNotString(bjro[0]);

                    append.Tag = tagAsString;
                }

                sorted[append.Timestamp.Ticks] = append;
            }

            result.Appends = new List<AppendOperation>(sorted.Values);

            return result;

            static long GetLong(object value)
            {
                return value switch
                {
                    long l => l,
                    LazyNumberValue lnv => lnv,
                    _ => throw new NotSupportedException($"Not supported type. Was expecting number, but got '{value}'."),
                };
            }
        }

        private static void ThrowNotBlittableJsonReaderObjectOperation(object op)
        {
            throw new InvalidDataException($"'Operations' should contain items of type BlittableJsonReaderObject only, but got {op.GetType()}");
        }

        private static void ThrowNotBlittableJsonReaderArrayOperation(object op)
        {
            throw new InvalidDataException($"'Appends' should contain items of type BlittableJsonReaderArray only, but got {op.GetType()}");
        }

        private static void ThrowNotString(object obj)
        {
            throw new InvalidDataException($"Expected a string but got: {obj.GetType()}");
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
