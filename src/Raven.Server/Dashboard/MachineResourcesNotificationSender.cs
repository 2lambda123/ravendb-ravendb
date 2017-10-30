﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Raven.Server.Background;
using Raven.Server.NotificationCenter;
using Raven.Server.Utils;
using Sparrow;
using Sparrow.Collections;
using Sparrow.LowMemory;
using Sparrow.Platform;

namespace Raven.Server.Dashboard
{
    public class MachineResourcesNotificationSender : BackgroundWorkBase
    {
        private readonly ConcurrentSet<ConnectedWatcher> _watchers;
        private readonly TimeSpan _notificationsThrottle;

        private DateTime _lastSentNotification = DateTime.MinValue;

        public MachineResourcesNotificationSender(string resourceName,
            ConcurrentSet<ConnectedWatcher> watchers, TimeSpan notificationsThrottle, CancellationToken shutdown)
            : base(resourceName, shutdown)
        {
            _watchers = watchers;
            _notificationsThrottle = notificationsThrottle;
        }

        protected override async Task DoWork()
        {
            var now = DateTime.UtcNow;
            var timeSpan = now - _lastSentNotification;
            if (timeSpan < _notificationsThrottle)
            {
                await WaitOrThrowOperationCanceled(_notificationsThrottle - timeSpan);
            }

            try
            {
                if (CancellationToken.IsCancellationRequested)
                    return;

                if (_watchers.Count == 0)
                    return;

                var machineResources = GetMachineResources();
                foreach (var watcher in _watchers)
                {
                    // serialize to avoid race conditions
                    // please notice we call ToJson inside a loop since DynamicJsonValue is not thread-safe
                    watcher.NotificationsQueue.Enqueue(machineResources.ToJson());
                }
            }
            finally
            {
                _lastSentNotification = DateTime.UtcNow;
            }
        }

        public static MachineResources GetMachineResources()
        {
            var currentProcess = Process.GetCurrentProcess();
            var workingSet =
                PlatformDetails.RunningOnPosix == false || PlatformDetails.RunningOnMacOsx
                    ? currentProcess.WorkingSet64
                    : MemoryInformation.GetRssMemoryUsage(currentProcess.Id);
            var memoryInfoResult = MemoryInformation.GetMemoryInfo();
            var installedMemory = memoryInfoResult.InstalledMemory.GetValue(SizeUnit.Bytes);
            var availableMemory = memoryInfoResult.AvailableMemory.GetValue(SizeUnit.Bytes);

            var cpuInfo = CpuUsage.Calculate();
            var machineResources = new MachineResources
            {
                TotalMemory = installedMemory,
                MachineMemoryUsage = installedMemory - availableMemory,
                ProcessMemoryUsage = workingSet,
                MachineCpuUsage = cpuInfo.MachineCpuUsage,
                ProcessCpuUsage = cpuInfo.ProcessCpuUsage
            };

            return machineResources;
        }
    }
}
