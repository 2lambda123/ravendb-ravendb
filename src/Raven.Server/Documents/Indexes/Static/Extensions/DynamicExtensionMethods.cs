﻿using System;
using Raven.NewClient.Client.Data;

namespace Raven.Server.Documents.Indexes.Static.Extensions
{
    public static class DynamicExtensionMethods
    {
        public static BoostedValue Boost(dynamic o, object value)
        {
            return new BoostedValue
            {
                Value = o,
                Boost = Convert.ToSingle(value)
            };
        }
    }
}