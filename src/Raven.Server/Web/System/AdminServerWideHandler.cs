﻿// -----------------------------------------------------------------------
//  <copyright file="ServerWideBackupHandler.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.OngoingTasks;
using Raven.Client.ServerWide.Operations.Configuration;
using Raven.Client.ServerWide.Operations.OngoingTasks;
using Raven.Server.Documents.PeriodicBackup;
using Raven.Server.Rachis;
using Raven.Server.Routing;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Commands;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Sparrow.Json.Parsing;

namespace Raven.Server.Web.System
{
    public class AdminServerWideHandler : ServerRequestHandler
    {
        [RavenAction("/admin/configuration/server-wide", "GET", AuthorizationStatus.ClusterAdmin)]
        public async Task GetConfigurationServerWide()
        {
            // FullPath removes the trailing '/' so adding it back for the studio
            var localRootPath = ServerStore.Configuration.Backup.LocalRootPath;
            var localRootFullPath = localRootPath != null ? localRootPath.FullPath + Path.DirectorySeparatorChar : null;

            var result = new DynamicJsonValue
            {
                [nameof(ServerStore.Configuration.Backup.LocalRootPath)] = localRootFullPath,
                [nameof(ServerStore.Configuration.Backup.AllowedAwsRegions)] = ServerStore.Configuration.Backup.AllowedAwsRegions,
                [nameof(ServerStore.Configuration.Backup.AllowedDestinations)] = ServerStore.Configuration.Backup.AllowedDestinations,
            };

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                context.Write(writer, result);
            }
        }

        // Used for Create, Edit
        [RavenAction("/admin/configuration/server-wide/backup", "PUT", AuthorizationStatus.ClusterAdmin)]
        public async Task PutServerWideBackupConfigurationCommand()
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                var configurationBlittable = await context.ReadForMemoryAsync(RequestBodyStream(), "server-wide-backup-configuration");
                var observedConfiguration = JsonDeserializationCluster.ServerWideBackupConfiguration(configurationBlittable);

                ServerStore.LicenseManager.AssertCanAddPeriodicBackup(observedConfiguration);

                var existingTasks = GetTaskConfigurations(OngoingTaskType.Backup, JsonDeserializationCluster.ServerWideBackupConfiguration);

                if (existingTasks
                    .Where(existingTask => existingTask.Name.Equals(observedConfiguration.Name, StringComparison.OrdinalIgnoreCase))
                    .Any(existingTask => existingTask.TaskId != observedConfiguration.TaskId))
                    throw new InvalidOperationException($"Can't use task name '{observedConfiguration.Name}', there is already a Periodic ServerWide Backup task with that name");

                BackupConfigurationHelper.UpdateLocalPathIfNeeded(observedConfiguration, ServerStore);
                BackupConfigurationHelper.AssertBackupConfiguration(observedConfiguration);
                BackupConfigurationHelper.AssertDestinationAndRegionAreAllowed(observedConfiguration, ServerStore);
                
                var (newIndex, _) = await ServerStore.PutServerWideBackupConfigurationAsync(observedConfiguration, GetRaftRequestIdFromQuery());
                await ServerStore.Cluster.WaitForIndexNotification(newIndex);

                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                using (context.OpenReadTransaction())
                {
                    var backupName = ServerStore.Cluster.GetServerWideTaskNameByTaskId(context, ClusterStateMachine.ServerWideConfigurationKey.Backup, newIndex);
                    if (backupName == null)
                        throw new InvalidOperationException($"Backup name is null for server-wide backup with task id: {newIndex}");

                    var putResponse = new PutServerWideBackupConfigurationResponse
                    {
                        Name = backupName,
                        RaftCommandIndex = newIndex
                    };

                    HttpContext.Response.StatusCode = (int)HttpStatusCode.Created;
                    context.Write(writer, putResponse.ToJson());
                }
            }
        }

        [RavenAction("/admin/configuration/server-wide/external-replication", "PUT", AuthorizationStatus.ClusterAdmin)]
        public async Task PutServerWideExternalReplicationCommand()
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                var configurationBlittable = await context.ReadForMemoryAsync(RequestBodyStream(), "server-wide-external-replication-configuration");
                var configuration = JsonDeserializationCluster.ServerWideExternalReplication(configurationBlittable);

                ServerStore.LicenseManager.AssertCanAddExternalReplication(configuration.DelayReplicationFor);

                var (newIndex, _) = await ServerStore.PutServerWideExternalReplicationAsync(configuration, GetRaftRequestIdFromQuery());
                await ServerStore.Cluster.WaitForIndexNotification(newIndex);

                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                using (context.OpenReadTransaction())
                {
                    var taskName = ServerStore.Cluster.GetServerWideTaskNameByTaskId(context, ClusterStateMachine.ServerWideConfigurationKey.ExternalReplication, newIndex);
                    if (taskName == null)
                        throw new InvalidOperationException($"External replication name is null for server-wide external replication with task id: {newIndex}");

                    var putResponse = new ServerWideExternalReplicationResponse
                    {
                        Name = taskName,
                        RaftCommandIndex = newIndex
                    };

                    HttpContext.Response.StatusCode = (int)HttpStatusCode.Created;
                    context.Write(writer, putResponse.ToJson());
                }
            }
        }

        [RavenAction("/admin/configuration/server-wide/backup", "DELETE", AuthorizationStatus.ClusterAdmin)]
        public async Task DeleteServerWideBackupConfigurationCommand()
        {
            // backward compatibility
            await DeleteServerWideTaskCommand(OngoingTaskType.Backup);
        }

        [RavenAction("/admin/configuration/server-wide/task", "DELETE", AuthorizationStatus.ClusterAdmin)]
        public async Task DeleteServerWideTaskCommand()
        {
            var typeAsString = GetStringQueryString("type", required: true);

            if (Enum.TryParse(typeAsString, out OngoingTaskType type) == false)
                throw new ArgumentException($"{typeAsString} is unknown task type.");

            await DeleteServerWideTaskCommand(type);
        }

        [RavenAction("/admin/configuration/server-wide/backup", "GET", AuthorizationStatus.ClusterAdmin)]
        public Task GetServerWideBackupConfigurations()
        {
            // backward compatibility
            return WriteTaskConfigurationsAsync(OngoingTaskType.Backup, JsonDeserializationCluster.ServerWideBackupConfiguration);
        }

        [RavenAction("/admin/configuration/server-wide/tasks", "GET", AuthorizationStatus.ClusterAdmin)]
        public async Task GetServerWideTasks()
        {
            var typeAsString = GetStringQueryString("type", required: true);
            if (Enum.TryParse(typeAsString, out OngoingTaskType type) == false)
                throw new ArgumentException($"{typeAsString} is unknown task type.");

            Func<BlittableJsonReaderObject, IDynamicJsonValueConvertible> converter;
            switch (type)
            {
                case OngoingTaskType.Backup:
                    converter = JsonDeserializationCluster.ServerWideBackupConfiguration;
                    break;

                case OngoingTaskType.Replication:
                    converter = JsonDeserializationCluster.ServerWideExternalReplication;
                    break;

                default:
                    throw new ArgumentOutOfRangeException($"Task type '{type} isn't suppported");
            }

            await WriteTaskConfigurationsAsync(type, converter);
        }

        [RavenAction("/admin/configuration/server-wide/state", "POST", AuthorizationStatus.ClusterAdmin)]
        public async Task ToggleServerWideTaskState()
        {
            var typeAsString = GetStringQueryString("type", required: true);
            var taskName = GetStringQueryString("name", required: true);
            var disable = GetBoolValueQueryString("disable") ?? true;

            if (Enum.TryParse(typeAsString, out OngoingTaskType type) == false)
                throw new ArgumentException($"{typeAsString} is unknown task type.");

            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            using (context.OpenReadTransaction())
            {
                var configuration = new ToggleServerWideTaskStateCommand.Parameters
                {
                    Type = type,
                    TaskName = taskName,
                    Disable = disable
                };
                var (newIndex, _) = await ServerStore.ToggleServerWideTaskStateAsync(configuration, GetRaftRequestIdFromQuery());
                await ServerStore.Cluster.WaitForIndexNotification(newIndex);

                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                {
                    var toggleResponse = new ServerWideTaskResponse
                    {
                        Name = taskName,
                        RaftCommandIndex = newIndex
                    };

                    context.Write(writer, toggleResponse.ToJson());
                }
            }
        }

        private async Task DeleteServerWideTaskCommand(OngoingTaskType taskType)
        {
            var name = GetStringQueryString("name", required: true);
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            {
                var deleteConfiguration = new DeleteServerWideTaskCommand.DeleteConfiguration
                {
                    TaskName = name,
                    Type = taskType
                };

                var (newIndex, _) = await ServerStore.DeleteServerWideTaskAsync(deleteConfiguration, GetRaftRequestIdFromQuery());
                await ServerStore.Cluster.WaitForIndexNotification(newIndex);

                await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
                using (context.OpenReadTransaction())
                {
                    var deleteResponse = new ServerWideTaskResponse
                    {
                        Name = name,
                        RaftCommandIndex = newIndex
                    };

                    HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Write(writer, deleteResponse.ToJson());
                }
            }
        }

        private ServerWideTasksResult<T> GetTaskConfigurationsInternal<T>(TransactionOperationContext context, OngoingTaskType type, Func<BlittableJsonReaderObject, T> converter) 
            where T : IDynamicJsonValueConvertible
        {
            var taskName = GetStringQueryString("name", required: false);

            using (context.OpenReadTransaction())
            {
                var blittables = ServerStore.Cluster.GetServerWideConfigurations(context, type, taskName);
                var result = new ServerWideTasksResult<T>();

                foreach (var blittable in blittables)
                {
                    var configuration = converter(blittable);
                    result.Results.Add(configuration);
                }

                return result;
            }
        }

        private List<T> GetTaskConfigurations<T>(OngoingTaskType type, Func<BlittableJsonReaderObject, T> converter)
            where T : IDynamicJsonValueConvertible
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
                return GetTaskConfigurationsInternal(context, type, converter).Results;
        }

        private async Task WriteTaskConfigurationsAsync<T>(OngoingTaskType type, Func<BlittableJsonReaderObject, T> converter) 
            where T : IDynamicJsonValueConvertible
        {
            using (ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context))
            await using (var writer = new AsyncBlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                var result = GetTaskConfigurationsInternal(context, type, converter);
                context.Write(writer, result.ToJson());
            }
        }
    }

    public class ServerWideTasksResult<T> : IDynamicJsonValueConvertible
        where T : IDynamicJsonValueConvertible
    {
        public List<T> Results;

        public ServerWideTasksResult()
        {
            Results = new List<T>();
        }

        public DynamicJsonValue ToJson()
        {
            return new DynamicJsonValue
            {
                [nameof(Results)] = new DynamicJsonArray(Results.Select(x => x.ToJson()))
            };
        }
    }
}
