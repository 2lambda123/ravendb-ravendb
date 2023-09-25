﻿using Raven.Client.Documents.Queries.Sorting;
using Raven.Server.Documents.Indexes.Sorting;
using Raven.Server.ServerWide.Context;
using Sparrow.Json.Parsing;

namespace Raven.Server.ServerWide.Commands.Sorters
{
    internal sealed class PutServerWideSorterCommand : PutValueCommand<SorterDefinition>
    {
        public const string Prefix = "sorter/";

        public PutServerWideSorterCommand()
        {
            // for deserialization
        }

        public PutServerWideSorterCommand(SorterDefinition value, string uniqueRequestId)
            : base(uniqueRequestId)
        {
            if (value is null)
                throw new System.ArgumentNullException(nameof(value));

            Name = GetName(value.Name);
            Value = value;
        }

        public override void UpdateValue(ClusterOperationContext context, long index)
        {
            context.Transaction.InnerTransaction.LowLevelTransaction.BeforeCommitFinalization += _ => SorterCompilationCache.Instance.AddServerWideItem(Value.Name, Value.Code);
        }

        public override DynamicJsonValue ValueToJson()
        {
            return Value?.ToJson();
        }

        internal static string GetName(string name)
        {
            return $"{Prefix}{name}";
        }

        public static string ExtractName(string name)
        {
            return name.Substring(Prefix.Length);
        }
    }
}
