﻿using System;
using System.IO;
using System.Runtime.Serialization;
using CsvHelper;
using Raven.Server.Utils;
using Raven.Server.Utils.Cpu;
using Sparrow;
using Sparrow.LowMemory;
using Vibrant.InfluxDB.Client;

namespace Tests.Infrastructure
{
    public class TestResourceSnapshotWriter : IDisposable
    {
        private readonly ICpuUsageCalculator _cpuUsageCalculator;
        private readonly TestResourcesAnalyzerMetricCacher _metricCacher;
        private readonly CsvWriter _csvWriter;
        private readonly object _syncObject = new object();
        
        public TestResourceSnapshotWriter(string filename = null)
        {
            lock (_syncObject)
            {
                _cpuUsageCalculator = CpuHelper.GetOSCpuUsageCalculator();
                _metricCacher = new TestResourcesAnalyzerMetricCacher(_cpuUsageCalculator);

                FileStream file;
                filename ??= $"TestResources_{DateTime.Now:dd_MM_yyyy_HH_mm_ss}.csv";
                try
                {
                    file = File.OpenWrite(filename ?? filename);
                }
                catch (UnauthorizedAccessException) //just in case, don't think it will ever be needed
                {
                    throw new InvalidOperationException($"Tried to open '{filename}' for write, but failed with {nameof(UnauthorizedAccessException)}. This is weird, and is probably a bug.");
                }

                file.Position = 0;
                file.SetLength(0);

                _csvWriter = new CsvWriter(new StreamWriter(file));
                _csvWriter.WriteHeader(typeof(TestResourceSnapshot));
            }
        }

        public void WriteResourceSnapshot(TestStage testStage, string comment = "")
        {
            lock (_syncObject)
            {
                _csvWriter.NextRecord();
                _csvWriter.WriteRecord(new TestResourceSnapshot(this, testStage, comment));
            }
        }

        public class TestResourceSnapshot
        {
            [InfluxTag(nameof(TestStage))]
            public TestStage TestStage { get; set; }

            [InfluxTag(nameof(Comment))]
            public string Comment { get; set; }

            [InfluxTimestamp]
            public DateTime InfluxTimestamp => DateTime.Parse(TimeStamp);

            [InfluxField(nameof(TimeStamp))]
            public string TimeStamp { get; set; }
            
            [InfluxField(nameof(MachineCpuUsage))]
            public long MachineCpuUsage { get; set; }
            
            [InfluxField(nameof(ProcessCpuUsage))]
            public long ProcessCpuUsage { get; set; }
            
            [InfluxField(nameof(ProcessMemoryUsageInMb))]
            public long ProcessMemoryUsageInMb { get; set; }
            
            [InfluxField(nameof(TotalMemoryInMb))]
            public long TotalMemoryInMb { get; set; }
            
            [InfluxField(nameof(AvailableMemoryInMb))]
            public long AvailableMemoryInMb { get; set; }
            
            [InfluxField(nameof(TotalCommittableMemoryInMb))]
            public long TotalCommittableMemoryInMb { get; set; }
            
            [InfluxField(nameof(CurrentCommitChargeInMb))]
            public long CurrentCommitChargeInMb { get; set; }
            
            [InfluxField(nameof(SharedCleanMemoryInMb))]
            public long SharedCleanMemoryInMb { get; set; }
            
            [InfluxField(nameof(TotalScratchDirtyMemory))]
            public long TotalScratchDirtyMemory { get; set; }
            
            [InfluxField(nameof(TotalScratchAllocatedMemory))]
            public long TotalScratchAllocatedMemory { get; set; }
            
            [InfluxField(nameof(TotalDirtyMemory))]
            public long TotalDirtyMemory { get; set; }
            
            [InfluxField(nameof(IsHighDirty))]
            public bool IsHighDirty { get; set; }

            [Obsolete("Needed for serialization, should not be used directly", true)]
            public TestResourceSnapshot()
            {
                
            }

            public static TestResourceSnapshot GetEmpty() => (TestResourceSnapshot)FormatterServices.GetUninitializedObject(typeof(TestResourceSnapshot));

            internal TestResourceSnapshot(TestResourceSnapshotWriter parent, TestStage testStage, string comment)
            {
                var timeStamp = DateTime.Now;
                var cpuUsage = parent._metricCacher.GetValue(
                    MetricCacher.Keys.Server.CpuUsage, 
                    parent._cpuUsageCalculator.Calculate);
                
                var memoryInfo = parent._metricCacher.GetValue<MemoryInfoResult>(MetricCacher.Keys.Server.MemoryInfoExtended);

                TotalScratchAllocatedMemory = new Size(MemoryInformation.GetTotalScratchAllocatedMemory(), SizeUnit.Bytes).GetValue(SizeUnit.Megabytes);
                TotalDirtyMemory = new Size(MemoryInformation.GetDirtyMemoryState().TotalDirtyInBytes, SizeUnit.Bytes).GetValue(SizeUnit.Megabytes);
                IsHighDirty = MemoryInformation.GetDirtyMemoryState().IsHighDirty;
                TestStage = testStage;
                TimeStamp = timeStamp.ToString("o");
                Comment = comment;
                MachineCpuUsage = (long)cpuUsage.MachineCpuUsage;
                ProcessCpuUsage = (long)cpuUsage.ProcessCpuUsage;
                ProcessMemoryUsageInMb = memoryInfo.WorkingSet.GetValue(SizeUnit.Megabytes);
                TotalMemoryInMb = memoryInfo.TotalPhysicalMemory.GetValue(SizeUnit.Megabytes);
                TotalCommittableMemoryInMb = memoryInfo.TotalCommittableMemory.GetValue(SizeUnit.Megabytes);
                AvailableMemoryInMb = memoryInfo.AvailableMemory.GetValue(SizeUnit.Megabytes);
                CurrentCommitChargeInMb = memoryInfo.CurrentCommitCharge.GetValue(SizeUnit.Megabytes);
                SharedCleanMemoryInMb = memoryInfo.SharedCleanMemory.GetValue(SizeUnit.Megabytes);
                TotalScratchDirtyMemory = memoryInfo.TotalScratchDirtyMemory.GetValue(SizeUnit.Megabytes);
            }
        }

        public void Dispose()
        {
            _csvWriter.Flush();
            _csvWriter.Dispose();
        }
    }

    public enum TestStage
    {
        TestAssemblyStarted,
        TestAssemblyEnded,
        TestClassStarted,
        TestClassEnded,
        TestStarted,
        TestFinishedBeforeGc,
        TestFinishedAfterGc
    }
}
