// -----------------------------------------------------------------------
//  <copyright file="ReplicationClientConfiguration.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using Sparrow.Json.Parsing;

namespace Raven.Client.Replication
{
    public class ReplicationClientConfiguration
    {
        public FailoverBehavior? FailoverBehavior { get; set; }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(FailoverBehavior)] = FailoverBehavior
            };
        }
    }
}
