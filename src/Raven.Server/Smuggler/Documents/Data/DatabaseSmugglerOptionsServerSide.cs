﻿using System;
using Microsoft.AspNetCore.Http;
using Raven.Client.Documents.Smuggler;
using Sparrow.Json;

namespace Raven.Server.Smuggler.Documents.Data
{
    public class DatabaseSmugglerOptionsServerSide : DatabaseSmugglerOptions
    {
        public static DatabaseSmugglerOptionsServerSide Create(HttpContext httpContext, JsonOperationContext context)
        {
            var result = new DatabaseSmugglerOptionsServerSide();

            foreach (var item in httpContext.Request.Query)
            {
                try
                {
                    var key = item.Key;
                    if (string.Equals(key, "operateOnTypes", StringComparison.OrdinalIgnoreCase))
                        result.OperateOnTypes = (DatabaseItemType)Enum.Parse(typeof(DatabaseItemType), item.Value[0]);
                    else if (string.Equals(key, "revisionDocumentsLimit", StringComparison.OrdinalIgnoreCase))
                        result.RevisionDocumentsLimit = int.Parse(item.Value[0]);
                    else if (string.Equals(key, "includeExpired", StringComparison.OrdinalIgnoreCase))
                        result.IncludeExpired = bool.Parse(item.Value[0]);
                    else if (string.Equals(key, "removeAnalyzers", StringComparison.OrdinalIgnoreCase))
                        result.RemoveAnalyzers = bool.Parse(item.Value[0]);
                    else if (string.Equals(key, "transformScript", StringComparison.OrdinalIgnoreCase))
                        result.TransformScript = Uri.UnescapeDataString(item.Value[0]);
                    else if (string.Equals(key, "maxStepsForTransformScript", StringComparison.OrdinalIgnoreCase))
                        result.MaxStepsForTransformScript = int.Parse(item.Value[0]);
                    else if (string.Equals(key, "collection", StringComparison.OrdinalIgnoreCase))
                        result.CollectionsToExport.AddRange(item.Value);
                }
                catch (Exception e)
                {
                    throw new ArgumentException($"Could not handle query string parameter '{item.Key}' (value: {item.Value})", e);
                }
            }

            return result;
        }
    }
}