﻿// -----------------------------------------------------------------------
//  <copyright file="AdminResourcesStudioTasksHandler.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Raven.Abstractions.Data;
using Raven.Server.Routing;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Web.System
{
    public class AdminResourcesStudioTasksHandler : RequestHandler
    {
        [RavenAction("/admin/*/toggle-disable", "POST", "/admin/{resourceType:databases|fs|cs|ts}/toggle-disable?name={resourceName:string|multiple}&isDisabled={isDisabled:bool}")]
        public Task PostToggleDisableDatabases()
        {
            var resourceType = RouteMatch.Url.Substring(RouteMatch.CaptureStart, RouteMatch.CaptureLength);
            string resourcePrefix;
            switch (resourceType)
            {
                case Constants.Database.UrlPrefix:
                    resourcePrefix = Constants.Database.Prefix;
                    break;
                case Constants.FileSystem.UrlPrefix:
                    resourcePrefix = Constants.FileSystem.Prefix;
                    break;
                case Constants.Counter.UrlPrefix:
                    resourcePrefix = Constants.Counter.Prefix;
                    break;
                case Constants.TimeSeries.UrlPrefix:
                    resourcePrefix = Constants.TimeSeries.Prefix;
                    break;
                default:
                    throw new InvalidOperationException($"Resource type is not valid: '{resourceType}'");
            }

            var names = GetStringValuesQueryString("name");
            var disableRequested = GetBoolValueQueryString("disable").Value;

            var databasesToUnload = new List<string>();

            TransactionOperationContext context;
            using (ServerStore.ContextPool.AllocateOperationContext(out context))
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            using (var tx = context.OpenWriteTransaction())
            {
                writer.WriteStartArray();
                var first = true;
                foreach (var name in names)
                {
                    if (first == false)
                        writer.WriteComma();
                    first = false;

                    var dbId = resourcePrefix + name;
                    var dbDoc = ServerStore.Read(context, dbId);

                    if (dbDoc == null)
                    {
                        context.Write(writer, new DynamicJsonValue
                        {
                            ["Name"] = name,
                            ["Success"] = false,
                            ["Reason"] = "database not found",
                        });
                        continue;
                    }

                    object disabledValue;
                    var disabled = false;
                    if (dbDoc.TryGetMember("Disabled", out disabledValue))
                    {
                        disabled = (bool)disabledValue;
                    }

                    if (disabled == disableRequested)
                    {
                        var state = disableRequested ? "disabled" : "enabled";
                        context.Write(writer, new DynamicJsonValue
                        {
                            ["Name"] = name,
                            ["Success"] = false,
                            ["Disabled"] = disableRequested,
                            ["Reason"] = $"Database already {state}",
                        });
                        continue;
                    }

                    var newDoc2 = context.ReadObject(dbDoc, dbId, BlittableJsonDocumentBuilder.UsageMode.ToDisk);

                    ServerStore.Write(context, dbId, newDoc2);
                    databasesToUnload.Add(name);

                    context.Write(writer, new DynamicJsonValue
                    {
                        ["Name"] = name,
                        ["Success"] = true,
                        ["Disabled"] = disableRequested,
                    });
                }

                tx.Commit();

                writer.WriteEndArray();
            }

            foreach (var name in databasesToUnload)
            {
                /* Right now only database resource is supported */
                ServerStore.DatabasesLandlord.UnloadAndLock(name, () =>
                {
                    // empty by design
                });
            }

            return Task.CompletedTask;
        }
    }
}