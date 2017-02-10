﻿using System;
using System.Collections.Generic;
using Raven.NewClient.Abstractions.Extensions;
using Sparrow.Extensions;

namespace Raven.NewClient.Client.Document
{
    public class RavenLoadConfiguration : ILoadConfiguration
    {
        public Dictionary<string, object> TransformerParameters { get; set; }

        public RavenLoadConfiguration()
        {
            TransformerParameters = new Dictionary<string, object>();
        }

        public void AddQueryParam(string name, object value)
        {
            AddTransformerParameter(name, value);
        }

        public void AddTransformerParameter(string name, object value)
        {
            TransformerParameters[name] = value;
        }
        public void AddTransformerParameter(string name, DateTime value)
        {
            TransformerParameters[name] = value.GetDefaultRavenFormat(isUtc: value.Kind == DateTimeKind.Utc);
        }
    }
}
