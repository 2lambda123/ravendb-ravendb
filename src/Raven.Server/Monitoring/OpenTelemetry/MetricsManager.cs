﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Raven.Client.Util;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Monitoring.OpenTelemetry;

public class MetricsManager
{
    private readonly RavenServer _server;
    private readonly SemaphoreSlim _locker = new(1, 1);
    private ServerMetrics _serverMetrics;
    private readonly Dictionary<string, DatabaseWideMetrics> _loadedDatabases = new(StringComparer.OrdinalIgnoreCase);

    public MetricsManager(RavenServer server)
    {
        _server = server;
        _server.ServerStore.LicenseManager.LicenseChanged += OnLicenseChanged;
    }

    private void OnLicenseChanged()
    {
        if (_server.Configuration.Monitoring.OpenTelemetry.Enabled == false)
            return;

        _locker.Wait();
        try
        {
            var activate = _server.ServerStore.LicenseManager.CanUseOpenTelemetryMonitoring(withNotification: true);
            if (activate)
            {
                Execute();
                //turn on
            }
            else
            {
                //when we dispose meter all registered thing will die eventually however metrics still be exported but there will be no instruments
            }
        }
        catch (ObjectDisposedException)
        {
            // ignore
            // we are shutting down the server
        }
        finally
        {
            _locker.Release();
        }
    }

    public void Execute()
    {
        if (_server.Configuration.Monitoring.OpenTelemetry.Enabled == false)
            return;

        _locker.Wait();

        try
        {
            var activate = _server.ServerStore.LicenseManager.CanUseOpenTelemetryMonitoring(withNotification: true);
            if (activate)
            {
                if (_serverMetrics != null)
                    throw new InvalidOperationException("Cannot start SNMP Engine because it is already activated. Should not happen!");

                RegisterServerWideMeters();
            }

            _server.ServerStore.DatabasesLandlord.OnDatabaseLoaded += AddDatabasesIfNecessary;
        }
        catch (Exception e)
        {
            Debugger.Launch();
            Debugger.Break();
            throw;
        }
        finally
        {
            _locker.Release();
        }

        AsyncHelpers.RunSync(AddDatabases);
    }

    private async Task AddDatabases()
    {
        if (_server.Configuration.Monitoring.OpenTelemetry.Databases == false)
            return;

        await _locker.WaitAsync();
        try
        {
            using var _ = _server.ServerStore.ContextPool.AllocateOperationContext(out TransactionOperationContext context);
            using var __ = context.OpenReadTransaction();

            var databases = _server
                .ServerStore
                .Cluster
                .ItemKeysStartingWith(context, Client.Constants.Documents.Prefix, 0, long.MaxValue)
                .Select(x => x.Substring(Client.Constants.Documents.Prefix.Length))
                .ToList();

            if (databases.Count == 0)
                return;

            var databaseNamesToShare = _server.Configuration.Monitoring.OpenTelemetry.ExposedDatabases;
            foreach (var database in databases)
            {
                if ((databaseNamesToShare == null || databaseNamesToShare.Contains(database)) && _loadedDatabases.TryGetValue(database, out var databaseMetrics) == false)
                {
                    _loadedDatabases[database] = new DatabaseWideMetrics(_server.ServerStore.DatabasesLandlord, database, _server.Configuration.Monitoring.OpenTelemetry);
                }
            }
        }
        finally
        {
            _locker.Release();
        }
    }

    private void AddDatabasesIfNecessary(string databaseName)
    {
        if (_server.Configuration.Monitoring.OpenTelemetry.Databases == false)
            return;

        var exposedDatabases = _server.Configuration.Monitoring.OpenTelemetry.ExposedDatabases;
        if (exposedDatabases != null && exposedDatabases.Contains(databaseName) == false)
            return;

        if (string.IsNullOrWhiteSpace(databaseName))
            return;

        if (_loadedDatabases.ContainsKey(databaseName))
            return;

        _loadedDatabases.Add(databaseName, new DatabaseWideMetrics(_server.ServerStore.DatabasesLandlord, databaseName, _server.Configuration.Monitoring.OpenTelemetry));
    }

    private void RegisterServerWideMeters()
    {
        if (_serverMetrics != null)
            throw new InvalidOperationException("Server-wide metrics are already initialized.");

        if (_server.Configuration.Monitoring.OpenTelemetry.ServerWideEnabled == false)
            return;

        _serverMetrics = new(_server);
    }
}
