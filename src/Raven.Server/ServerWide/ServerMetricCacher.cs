﻿using System;
using Raven.Client.Util;
using Raven.Server.Platform.Posix;
using Raven.Server.Utils;
using Sparrow.Platform;
using Sparrow.Server.LowMemory;
using Sparrow.Server.Platform.Posix;
using Sparrow.Server.Utils;

namespace Raven.Server.ServerWide
{
    public sealed class ServerMetricCacher : MetricCacher
    {
        private readonly SmapsReader _smapsReader;
        private readonly RavenServer _server;

        public const int DefaultCpuRefreshRateInMs = 1000;

        public ServerMetricCacher(RavenServer server)
        {
            _server = server;

            if (PlatformDetails.RunningOnLinux)
                _smapsReader = new SmapsReader(new[] { new byte[SmapsReader.BufferSize], new byte[SmapsReader.BufferSize] });
        }

        public void Initialize()
        {
            Register(MetricCacher.Keys.Server.CpuUsage, TimeSpan.FromMilliseconds(DefaultCpuRefreshRateInMs), _server.CpuUsageCalculator.Calculate, asyncRefresh: false);
            Register(MetricCacher.Keys.Server.MemoryInfo, TimeSpan.FromSeconds(1), CalculateMemoryInfo);
            Register(MetricCacher.Keys.Server.MemoryInfoExtended.RefreshRate15Seconds, TimeSpan.FromSeconds(15), CalculateMemoryInfoExtended);
            Register(MetricCacher.Keys.Server.MemoryInfoExtended.RefreshRate5Seconds, TimeSpan.FromSeconds(5), CalculateMemoryInfoExtended);
            Register(MetricCacher.Keys.Server.DiskSpaceInfo, TimeSpan.FromSeconds(15), CalculateDiskSpaceInfo);
            Register(MetricCacher.Keys.Server.MemInfo, TimeSpan.FromSeconds(15), CalculateMemInfo);
            Register(MetricCacher.Keys.Server.MaxServerLimits, TimeSpan.FromHours(1), CalculateMaxServerLimits);
            Register(MetricCacher.Keys.Server.CurrentServerLimits, TimeSpan.FromMinutes(3), CalculateCurrentServerLimits);
            Register(MetricCacher.Keys.Server.GcAny, TimeSpan.FromSeconds(15), () => CalculateGcMemoryInfo(GCKind.Any));
            Register(MetricCacher.Keys.Server.GcBackground, TimeSpan.FromSeconds(15), () => CalculateGcMemoryInfo(GCKind.Background));
            Register(MetricCacher.Keys.Server.GcEphemeral, TimeSpan.FromSeconds(15), () => CalculateGcMemoryInfo(GCKind.Ephemeral));
            Register(MetricCacher.Keys.Server.GcFullBlocking, TimeSpan.FromSeconds(15), () => CalculateGcMemoryInfo(GCKind.FullBlocking));
        }

        private object CalculateMemoryInfo()
        {
            return MemoryInformation.GetMemoryInfo();
        }

        private object CalculateMemoryInfoExtended()
        {
            return MemoryInformation.GetMemoryInfo(_smapsReader, extended: true);
        }

        private object CalculateDiskSpaceInfo()
        {
            return DiskUtils.GetDiskSpaceInfo(_server.ServerStore.Configuration.Core.DataDirectory.FullPath);
        }

        private GCMemoryInfo CalculateGcMemoryInfo(GCKind gcKind)
        {
            return GC.GetGCMemoryInfo(gcKind);
        }

        private static object CalculateMemInfo()
        {
            if (PlatformDetails.RunningOnLinux)
                return MemInfoReader.Read();

            return MemInfo.Invalid;
        }

        private static object CalculateMaxServerLimits()
        {
            if (PlatformDetails.RunningOnLinux)
                return LimitsReader.ReadMaxLimits();

            return LimitsInfo.Invalid;
        }

        private static object CalculateCurrentServerLimits()
        {
            if (PlatformDetails.RunningOnLinux || PlatformDetails.RunningOnWindows)
                return AsyncHelpers.RunSync(LimitsReader.ReadCurrentLimitsAsync);

            return LimitsInfo.Invalid;
        }
    }
}
