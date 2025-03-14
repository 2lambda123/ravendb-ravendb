﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Lextm.SharpSnmpLib;
using Raven.Server.Platform.Posix;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Sparrow.Json.Parsing;

namespace Raven.Server.Monitoring.Snmp
{
    public sealed class SnmpOids
    {
        private SnmpOids()
        {
        }

        public const string Root = "1.3.6.1.4.1.45751.1.1.";

        public sealed class Server
        {
            private Server()
            {
            }

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server URL")]
            public const string Url = "1.1.1";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server Public URL")]
            public const string PublicUrl = "1.1.2";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server TCP URL")]
            public const string TcpUrl = "1.1.3";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server Public TCP URL")]
            public const string PublicTcpUrl = "1.1.4";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server version")]
            public const string Version = "1.2.1";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server full version")]
            public const string FullVersion = "1.2.2";

            [SnmpDataType(SnmpType.TimeTicks)]
            [Description("Server up-time")]
            public const string UpTime = "1.3";

            [SnmpDataType(SnmpType.TimeTicks)]
            [Description("Server up-time")]
            public const string UpTimeGlobal = "1.3.6.1.2.1.1.3.0";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Server process ID")]
            public const string Pid = "1.4";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Process CPU usage in %")]
            public const string ProcessCpu = "1.5.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Machine CPU usage in %")]
            public const string MachineCpu = "1.5.2";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("CPU Credits Base")]
            public const string CpuCreditsBase = "1.5.3.1";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("CPU Credits Max")]
            public const string CpuCreditsMax = "1.5.3.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("CPU Credits Remaining")]
            public const string CpuCreditsRemaining = "1.5.3.3";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("CPU Credits Gained Per Second")]
            public const string CpuCreditsCurrentConsumption = "1.5.3.4";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("CPU Credits Background Tasks Alert Raised")]
            public const string CpuCreditsBackgroundTasksAlertRaised = "1.5.3.5";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("CPU Credits Failover Alert Raised")]
            public const string CpuCreditsFailoverAlertRaised = "1.5.3.6";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("CPU Credits Any Alert Raised")]
            public const string CpuCreditsAlertRaised = "1.5.3.7";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("IO wait in %")]
            public const string MachineIoWait = "1.5.4";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server allocated memory in MB")]
            public const string TotalMemory = "1.6.1";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server low memory flag value")]
            public const string LowMemoryFlag = "1.6.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server total swap size in MB")]
            public const string TotalSwapSize = "1.6.3";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server total swap usage in MB")]
            public const string TotalSwapUsage = "1.6.4";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server working set swap usage in MB")]
            public const string WorkingSetSwapUsage = "1.6.5";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Dirty Memory that is used by the scratch buffers in MB")]
            public const string DirtyMemory = "1.6.6";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server managed memory size in MB")]
            public const string ManagedMemory = "1.6.7";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server unmanaged memory size in MB")]
            public const string UnmanagedMemory = "1.6.8";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server encryption buffers memory being in use in MB")]
            public const string EncryptionBuffersMemoryInUse = "1.6.9";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server encryption buffers memory being in pool in MB")]
            public const string EncryptionBuffersMemoryInPool = "1.6.10";

            [SnmpDataType(SnmpType.OctetString)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Specifies if this is a compacting GC or not.")]
            public const string GcCompacted = "1.6.11.{0}.1";

            [SnmpDataType(SnmpType.OctetString)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Specifies if this is a concurrent GC or not.")]
            public const string GcConcurrent = "1.6.11.{0}.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the number of objects ready for finalization this GC observed.")]
            public const string GcFinalizationPendingCount = "1.6.11.{0}.3";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the total fragmentation (in MB) when the last garbage collection occurred.")]
            public const string GcFragmented = "1.6.11.{0}.4";

            [SnmpDataType(SnmpType.Integer32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the generation this GC collected.")]
            public const string GcGeneration = "1.6.11.{0}.5";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the total heap size (in MB) when the last garbage collection occurred.")]
            public const string GcHeapSize = "1.6.11.{0}.6";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the high memory load threshold (in MB) when the last garbage collection occurred.")]
            public const string GcHighMemoryLoadThreshold = "1.6.11.{0}.7";

            [SnmpDataType(SnmpType.Integer32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. The index of this GC.")]
            public const string GcIndex = "1.6.11.{0}.8";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the memory load (in MB) when the last garbage collection occurred.")]
            public const string GcMemoryLoad = "1.6.11.{0}.9";

            [SnmpDataType(SnmpType.TimeTicks)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the pause durations. First item in the array.")]
            public const string GcPauseDurations1 = "1.6.11.{0}.10.1";

            [SnmpDataType(SnmpType.TimeTicks)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the pause durations. Second item in the array.")]
            public const string GcPauseDurations2 = "1.6.11.{0}.10.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the pause time percentage in the GC so far.")]
            public const string GcPauseTimePercentage = "1.6.11.{0}.11";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the number of pinned objects this GC observed.")]
            public const string GcPinnedObjectsCount = "1.6.11.{0}.12";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the promoted MB for this GC.")]
            public const string GcPromoted = "1.6.11.{0}.13";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the total available memory (in MB) for the garbage collector to use when the last garbage collection occurred.")]
            public const string GcTotalAvailableMemory = "1.6.11.{0}.14";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the total committed MB of the managed heap.")]
            public const string GcTotalCommitted = "1.6.11.{0}.15";

            [SnmpDataType(SnmpType.Gauge32)]
            [SnmpEnumIndex(typeof(GCKind))]
            [Description("GC information for {0}. Gets the large object heap size (in MB) after the last garbage collection of given kind occurred.")]
            public const string GcLohSize = "1.6.11.{0}.16.3";

            public const string MemInfoPrefix = "1.6.12.{0}";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Available memory for processing (in MB)")]
            public const string AvailableMemoryForProcessing = "1.6.13";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of concurrent requests")]
            public const string ConcurrentRequests = "1.7.1";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Total number of requests since server startup")]
            public const string TotalRequests = "1.7.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of requests per second (one minute rate)")]
            public const string RequestsPerSecond1M = "1.7.3";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of requests per second (five second rate)")]
            public const string RequestsPerSecond5S = "1.7.3.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Average request time in milliseconds")]
            public const string RequestAverageDuration = "1.7.4";

            [SnmpDataType(SnmpType.TimeTicks)]
            [Description("Server last request time")]
            public const string LastRequestTime = "1.8";

            [SnmpDataType(SnmpType.TimeTicks)]
            [Description("Server last authorized non cluster admin request time")]
            public const string LastAuthorizedNonClusterAdminRequestTime = "1.8.1";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server license type")]
            public const string ServerLicenseType = "1.9.1";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server license expiration date")]
            public const string ServerLicenseExpiration = "1.9.2";

            [SnmpDataType(SnmpType.TimeTicks)]
            [Description("Server license expiration left")]
            public const string ServerLicenseExpirationLeft = "1.9.3";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Server license utilized CPU cores")]
            public const string ServerLicenseUtilizedCpuCores = "1.9.4";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Server license max CPU cores")]
            public const string ServerLicenseMaxCpuCores = "1.9.5";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server storage used size in MB")]
            public const string StorageUsedSize = "1.10.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Server storage total size in MB")]
            public const string StorageTotalSize = "1.10.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Remaining server storage disk space in MB")]
            public const string StorageDiskRemainingSpace = "1.10.3";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Remaining server storage disk space in %")]
            public const string StorageDiskRemainingSpacePercentage = "1.10.4";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("IO read operations per second")]
            public const string StorageDiskIoReadOperations = "1.10.5";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("IO write operations per second")]
            public const string StorageDiskIoWriteOperations = "1.10.6";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Read throughput in kilobytes per second")]
            public const string StorageDiskReadThroughput = "1.10.7";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Write throughput in kilobytes per second")]
            public const string StorageDiskWriteThroughput = "1.10.8";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Queue length")]
            public const string StorageDiskQueueLength = "1.10.9";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Server certificate expiration date")]
            public const string ServerCertificateExpiration = "1.11.1";

            [SnmpDataType(SnmpType.TimeTicks)]
            [Description("Server certificate expiration left")]
            public const string ServerCertificateExpirationLeft = "1.11.2";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("List of well known admin certificate thumbprints")]
            public const string WellKnownAdminCertificates = "1.11.3";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("List of well known admin certificate issuers")]
            public const string WellKnownAdminIssuers = "1.11.4";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of expiring certificates")]
            public const string CertificateExpiringCount = "1.11.5";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of expired certificates")]
            public const string CertificateExpiredCount = "1.11.6";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of processor on the machine")]
            public const string MachineProcessorCount = "1.12.1";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of assigned processors on the machine")]
            public const string MachineAssignedProcessorCount = "1.12.2";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of backups currently running")]
            public const string ServerBackupsCurrent = "1.13.1";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Max number of backups that can run concurrently")]
            public const string ServerBackupsMax = "1.13.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of available worker threads in the thread pool")]
            public const string ThreadPoolAvailableWorkerThreads = "1.14.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of available completion port threads in the thread pool")]
            public const string ThreadPoolAvailableCompletionPortThreads = "1.14.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of active TCP connections")]
            public const string TcpActiveConnections = "1.15.1";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Indicates if any experimental features are used")]
            public const string FeatureAnyExperimental = "1.16.1";

            public const string ServerLimitsPrefix = "1.17.{0}";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Monitor lock contention count")]
            public const string MonitorLockContentionCount = "1.18.1";

            public static Dictionary<string, string> CreateMapping()
            {
                var dictionary = new Dictionary<string, string>();
                foreach (DynamicJsonValue field in ToJson())
                    dictionary.Add(field["OID"]!.ToString() ?? throw new InvalidOperationException(), field["Description"]!.ToString());
                return dictionary;
            }

            public static DynamicJsonArray ToJson()
            {
                var array = new DynamicJsonArray();
                foreach (var field in typeof(Server).GetFields())
                {
                    var fieldValue = GetFieldValue(field);

                    switch (field.Name)
                    {
                        case nameof(UpTimeGlobal):
                            array.Add(CreateJsonItem(fieldValue.Oid, fieldValue.Description));
                            break;
                        case nameof(MemInfoPrefix):
                            foreach (var propertyInfo in MemInfo.AllProperties.Values)
                            {
                                var index = propertyInfo.GetCustomAttribute<SnmpIndexAttribute>().Index;
                                var oid = Root + string.Format(MemInfoPrefix, index);
                                var name = propertyInfo.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? propertyInfo.Name;


                                array.Add(CreateJsonItem(oid, $"{name} value from '{MemInfoReader.MemInfoFileName}'"));
                            }
                            break;
                        case nameof(ServerLimitsPrefix):
                            foreach (var propertyInfo in LimitsInfo.AllProperties.Values)
                            {
                                var index = propertyInfo.GetCustomAttribute<SnmpIndexAttribute>().Index;
                                var description = propertyInfo.GetCustomAttribute<DescriptionAttribute>();
                                var oid = Root + string.Format(ServerLimitsPrefix, index);

                                array.Add(CreateJsonItem(oid, description.Description));
                            }
                            break;
                        default:
                            var fullOid = field.Name == nameof(UpTimeGlobal) ? fieldValue.Oid : Root + fieldValue.Oid;

                            if (fieldValue.Type == null)
                            {
                                array.Add(CreateJsonItem(fullOid, fieldValue.Description));
                            }
                            else
                            {
                                var enumUnderlyingType = Enum.GetUnderlyingType(fieldValue.Type);
                                foreach (var value in fieldValue.Type.GetEnumValues())
                                {
                                    var enumUnderlyingValue = Convert.ChangeType(value, enumUnderlyingType);

                                    var finalOid = fullOid.Replace("{0}", enumUnderlyingValue.ToString());
                                    var finalDescription = fieldValue.Description.Replace("{0}", $"{fieldValue.Type.Name}.{value}");

                                    array.Add(CreateJsonItem(finalOid, finalDescription));
                                }
                            }

                            break;
                    }
                }

                return array;
            }

            public static async Task WriteMibAsync(SnmpMibWriter writer)
            {
                foreach (var field in typeof(Server).GetFields())
                {
                    switch (field.Name)
                    {
                        case nameof(UpTimeGlobal):
                        case nameof(MemInfoPrefix):
                        case nameof(ServerLimitsPrefix):
                            continue;
                    }

                    var fieldValue = GetFieldValue(field);

                    switch (field.Name)
                    {
                        default:
                            if (fieldValue.Type == null)
                            {
                                await writer.WriteObjectAsync(field.Name, "server", fieldValue.TypeCode.Value, fieldValue.Description, fieldValue.Oid);
                            }
                            else
                            {
                                var enumUnderlyingType = Enum.GetUnderlyingType(fieldValue.Type);
                                foreach (var value in fieldValue.Type.GetEnumValues())
                                {
                                    var enumUnderlyingValue = Convert.ChangeType(value, enumUnderlyingType);

                                    var finalName = field.Name + enumUnderlyingValue;
                                    var finalOid = fieldValue.Oid.Replace("{0}", enumUnderlyingValue.ToString());
                                    var finalDescription = fieldValue.Description.Replace("{0}", $"{fieldValue.Type.Name}.{value}");

                                    await writer.WriteObjectAsync(finalName, "server", fieldValue.TypeCode.Value, finalDescription, finalOid);
                                }
                            }
                            break;
                    }
                }


            }
        }

        public sealed class Cluster
        {
            private Cluster()
            {
            }

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Current node tag")]
            public const string NodeTag = "3.1.1";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Current node state")]
            public const string NodeState = "3.1.2";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Cluster term")]
            public const string Term = "3.2.1";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Cluster index")]
            public const string Index = "3.2.2";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Cluster ID")]
            public const string Id = "3.2.3";
            
            internal static Dictionary<string, string> CreateMapping()
            {
                var dictionary = new Dictionary<string, string>();
                foreach (var field in typeof(Cluster).GetFields())
                {
                    var fieldValue = GetFieldValue(field);
                    dictionary.Add(Root + fieldValue.Oid, fieldValue.Description);
                }

                return dictionary;
            }

            public static DynamicJsonArray ToJson()
            {
                var array = new DynamicJsonArray();
                foreach (var field in typeof(Cluster).GetFields())
                {
                    var fieldValue = GetFieldValue(field);

                    array.Add(CreateJsonItem(Root + fieldValue.Oid, fieldValue.Description));
                }

                return array;
            }

            public static async Task WriteMibAsync(SnmpMibWriter writer)
            {
                foreach (var field in typeof(Cluster).GetFields())
                {
                    var fieldValue = GetFieldValue(field);

                    await writer.WriteObjectAsync(field.Name, "server", fieldValue.TypeCode.Value, fieldValue.Description, fieldValue.Oid);
                }
            }
        }

        public sealed class Databases
        {
            private Databases()
            {
            }

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Database name")]
            public const string Name = "5.2.{0}.1.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of indexes")]
            public const string CountOfIndexes = "5.2.{0}.1.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of stale indexes")]
            public const string CountOfStaleIndexes = "5.2.{0}.1.3";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of documents")]
            public const string CountOfDocuments = "5.2.{0}.1.4";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of revision documents")]
            public const string CountOfRevisionDocuments = "5.2.{0}.1.5";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of attachments")]
            public const string CountOfAttachments = "5.2.{0}.1.6";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of unique attachments")]
            public const string CountOfUniqueAttachments = "5.2.{0}.1.7";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of alerts")]
            public const string Alerts = "5.2.{0}.1.10";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Database ID")]
            public const string Id = "5.2.{0}.1.11";

            [SnmpDataType(SnmpType.TimeTicks)]
            [Description("Database up-time")]
            public const string UpTime = "5.2.{0}.1.12";

            [SnmpDataType(SnmpType.OctetString)]
            [Description("Indicates if database is loaded")]
            public const string Loaded = "5.2.{0}.1.13";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of rehabs")]
            public const string Rehabs = "5.2.{0}.1.14";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of performance hints")]
            public const string PerformanceHints = "5.2.{0}.1.15";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of indexing errors")]
            public const string IndexingErrors = "5.2.{0}.1.16";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Documents storage allocated size in MB")]
            public const string DocumentsStorageAllocatedSize = "5.2.{0}.2.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Documents storage used size in MB")]
            public const string DocumentsStorageUsedSize = "5.2.{0}.2.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Index storage allocated size in MB")]
            public const string IndexStorageAllocatedSize = "5.2.{0}.2.3";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Index storage used size in MB")]
            public const string IndexStorageUsedSize = "5.2.{0}.2.4";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Total storage size in MB")]
            public const string TotalStorageSize = "5.2.{0}.2.5";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Remaining storage disk space in MB")]
            public const string StorageDiskRemainingSpace = "5.2.{0}.2.6";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("IO read operations per second")]
            public const string StorageDiskIoReadOperations = "5.2.{0}.2.7";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("IO write operations per second")]
            public const string StorageDiskIoWriteOperations = "5.2.{0}.2.8";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Read throughput in kilobytes per second")]
            public const string StorageDiskReadThroughput = "5.2.{0}.2.9";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Write throughput in kilobytes per second")]
            public const string StorageDiskWriteThroughput = "5.2.{0}.2.10";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Queue length")]
            public const string StorageDiskQueueLength = "5.2.{0}.2.11";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of document puts per second (one minute rate)")]
            public const string DocPutsPerSecond = "5.2.{0}.3.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of indexed documents per second for map indexes (one minute rate)")]
            public const string MapIndexIndexesPerSecond = "5.2.{0}.3.2";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of maps per second for map-reduce indexes (one minute rate)")]
            public const string MapReduceIndexMappedPerSecond = "5.2.{0}.3.3";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of reduces per second for map-reduce indexes (one minute rate)")]
            public const string MapReduceIndexReducedPerSecond = "5.2.{0}.3.4";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of requests per second (one minute rate)")]
            public const string RequestsPerSecond = "5.2.{0}.3.5";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of requests from database start")]
            public const string RequestsCount = "5.2.{0}.3.6";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Average request time in milliseconds")]
            public const string RequestAverageDuration = "5.2.{0}.3.7";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of indexes")]
            public const string NumberOfIndexes = "5.2.{0}.5.1";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of static indexes")]
            public const string NumberOfStaticIndexes = "5.2.{0}.5.2";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of auto indexes")]
            public const string NumberOfAutoIndexes = "5.2.{0}.5.3";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of idle indexes")]
            public const string NumberOfIdleIndexes = "5.2.{0}.5.4";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of disabled indexes")]
            public const string NumberOfDisabledIndexes = "5.2.{0}.5.5";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of error indexes")]
            public const string NumberOfErrorIndexes = "5.2.{0}.5.6";

            [SnmpDataType(SnmpType.Integer32)]
            [Description("Number of faulty indexes")]
            public const string NumberOfFaultyIndexes = "5.2.{0}.5.7";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of writes (documents, attachments, counters)")]
            public const string WritesPerSecond = "5.2.{0}.6.1";

            [SnmpDataType(SnmpType.Gauge32)]
            [Description("Number of bytes written (documents, attachments, counters)")]
            public const string DataWrittenPerSecond = "5.2.{0}.6.2";

            public sealed class Indexes
            {
                private Indexes()
                {
                }

                [Description("Indicates if index exists")]
                public const string Exists = "5.2.{0}.4.{{0}}.1";

                [Description("Index name")]
                public const string Name = "5.2.{0}.4.{{0}}.2";

                [Description("Index priority")]
                public const string Priority = "5.2.{0}.4.{{0}}.4";

                [Description("Index state")]
                public const string State = "5.2.{0}.4.{{0}}.5";

                [Description("Number of index errors")]
                public const string Errors = "5.2.{0}.4.{{0}}.6";

                [Description("Last query time")]
                public const string LastQueryTime = "5.2.{0}.4.{{0}}.7";

                [Description("Index indexing time")]
                public const string LastIndexingTime = "5.2.{0}.4.{{0}}.8";

                [Description("Time since last query")]
                public const string TimeSinceLastQuery = "5.2.{0}.4.{{0}}.9";

                [Description("Time since last indexing")]
                public const string TimeSinceLastIndexing = "5.2.{0}.4.{{0}}.10";

                [Description("Index lock mode")]
                public const string LockMode = "5.2.{0}.4.{{0}}.11";

                [Description("Indicates if index is invalid")]
                public const string IsInvalid = "5.2.{0}.4.{{0}}.12";

                [Description("Index status")]
                public const string Status = "5.2.{0}.4.{{0}}.13";

                [Description("Number of maps per second (one minute rate)")]
                public const string MapsPerSec = "5.2.{0}.4.{{0}}.14";

                [Description("Number of reduces per second (one minute rate)")]
                public const string ReducesPerSec = "5.2.{0}.4.{{0}}.15";

                [Description("Index type")]
                public const string Type = "5.2.{0}.4.{{0}}.16";

                public static Dictionary<string, string> CreateMapping(long ignoreIndex)
                {
                    var dictionary = new Dictionary<string, string>();
                    foreach (var field in typeof(Indexes).GetFields())
                    {
                        var fieldValue = GetFieldValue(field);
                        var databaseOid = string.Format(fieldValue.Oid, ignoreIndex);
                        var indexOid = string.Format(databaseOid, ignoreIndex);
                        dictionary.Add(Root + indexOid, fieldValue.Description);
                    }

                    return dictionary;
                }
                
                public static DynamicJsonValue ToJson(ServerStore serverStore, TransactionOperationContext context, RawDatabaseRecord record, long databaseIndex)
                {
                    var mapping = SnmpDatabase.GetIndexMapping(context, serverStore, record.DatabaseName);

                    var djv = new DynamicJsonValue();
                    if (mapping.Count == 0)
                        return djv;

                    foreach (var indexName in record.Indexes.Keys)
                    {
                        if (mapping.TryGetValue(indexName, out var index) == false)
                            continue;

                        var array = new DynamicJsonArray();
                        foreach (var field in typeof(Indexes).GetFields())
                        {
                            var fieldValue = GetFieldValue(field);
                            var databaseOid = string.Format(fieldValue.Oid, databaseIndex);
                            var indexOid = string.Format(databaseOid, index);
                            array.Add(CreateJsonItem(Root + indexOid, fieldValue.Description));
                        }

                        djv[indexName] = array;
                    }

                    return djv;
                }
            }

            public sealed class General
            {
                private General()
                {
                }

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of all databases")]
                public const string TotalCount = "5.1.1";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of loaded databases")]
                public const string LoadedCount = "5.1.2";

                [SnmpDataType(SnmpType.TimeTicks)]
                [Description("Time since oldest backup")]
                public const string TimeSinceOldestBackup = "5.1.3";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of disabled databases")]
                public const string DisabledCount = "5.1.4";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of encrypted databases")]
                public const string EncryptedCount = "5.1.5";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of databases for current node")]
                public const string NodeCount = "5.1.6";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of indexes in all loaded databases")]
                public const string TotalNumberOfIndexes = "5.1.7.1";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of stale indexes in all loaded databases")]
                public const string TotalNumberOfStaleIndexes = "5.1.7.2";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of error indexes in all loaded databases")]
                public const string TotalNumberOfErrorIndexes = "5.1.7.3";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of faulty indexes in all loaded databases")]
                public const string TotalNumberOfFaultyIndexes = "5.1.7.4";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of indexed documents per second for map indexes (one minute rate) in all loaded databases")]
                public const string TotalMapIndexIndexesPerSecond = "5.1.8.1";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of maps per second for map-reduce indexes (one minute rate) in all loaded databases")]
                public const string TotalMapReduceIndexMappedPerSecond = "5.1.8.2";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of reduces per second for map-reduce indexes (one minute rate) in all loaded databases")]
                public const string TotalMapReduceIndexReducedPerSecond = "5.1.8.3";

                [SnmpDataType(SnmpType.Gauge32)]
                [Description("Number of writes (documents, attachments, counters) in all loaded databases")]
                public const string TotalWritesPerSecond = "5.1.9.1";

                [SnmpDataType(SnmpType.Gauge32)]
                [Description("Number of bytes written (documents, attachments, counters) in all loaded databases")]
                public const string TotalDataWrittenPerSecond = "5.1.9.2";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of faulted databases")]
                public const string FaultedCount = "5.1.10";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled ongoing tasks for all databases")]
                public const string TotalNumberOfOngoingTasks = "5.1.11.1";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active ongoing tasks for all databases")]
                public const string TotalNumberOfActiveOngoingTasks = "5.1.11.2";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled external replication tasks for all databases")]
                public const string TotalNumberOfExternalReplicationTasks = "5.1.11.3";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active external replication tasks for all databases")]
                public const string TotalNumberOfActiveExternalReplicationTasks = "5.1.11.4";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled RavenDB ETL tasks for all databases")]
                public const string TotalNumberOfRavenEtlTasks = "5.1.11.5";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active RavenDB ETL tasks for all databases")]
                public const string TotalNumberOfActiveRavenEtlTasks = "5.1.11.6";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled SQL ETL tasks for all databases")]
                public const string TotalNumberOfSqlEtlTasks = "5.1.11.7";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active SQL ETL tasks for all databases")]
                public const string TotalNumberOfActiveSqlEtlTasks = "5.1.11.8";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled OLAP ETL tasks for all databases")]
                public const string TotalNumberOfOlapEtlTasks = "5.1.11.9";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active OLAP ETL tasks for all databases")]
                public const string TotalNumberOfActiveOlapEtlTasks = "5.1.11.10";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled Elasticsearch ETL tasks for all databases")]
                public const string TotalNumberOfElasticSearchEtlTasks = "5.1.11.11";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active Elasticsearch ETL tasks for all databases")]
                public const string TotalNumberOfActiveElasticSearchEtlTasks = "5.1.11.12";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled Queue ETL tasks for all databases")]
                public const string TotalNumberOfQueueEtlTasks = "5.1.11.13";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active Queue ETL tasks for all databases")]
                public const string TotalNumberOfActiveQueueEtlTasks = "5.1.11.14";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled Backup tasks for all databases")]
                public const string TotalNumberOfBackupTasks = "5.1.11.15";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active Backup tasks for all databases")]
                public const string TotalNumberOfActiveBackupTasks = "5.1.11.16";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled Subscription tasks for all databases")]
                public const string TotalNumberOfSubscriptionTasks = "5.1.11.17";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active Subscription tasks for all databases")]
                public const string TotalNumberOfActiveSubscriptionTasks = "5.1.11.18";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled Pull Replication As Sink tasks for all databases")]
                public const string TotalNumberOfPullReplicationAsSinkTasks = "5.1.11.19";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active Pull Replication As Sink tasks for all databases")]
                public const string TotalNumberOfActivePullReplicationAsSinkTasks = "5.1.11.20";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of enabled Queue Sink tasks for all databases")]
                public const string TotalNumberOfQueueSinkTasks = "5.1.11.21";

                [SnmpDataType(SnmpType.Integer32)]
                [Description("Number of active Queue Sink tasks for all databases")]
                public const string TotalNumberOfActiveQueueSinkTasks = "5.1.11.22";
                
                internal static Dictionary<string, string> CreateMapping()
                {
                    var dictionary = new Dictionary<string, string>();
                    foreach (var field in typeof(General).GetFields())
                    {
                        var fieldValue = GetFieldValue(field);
                        dictionary.Add(Root + fieldValue.Oid, fieldValue.Description);
                    }

                    return dictionary;
                }
                
                public static DynamicJsonArray ToJson()
                {
                    var array = new DynamicJsonArray();
                    foreach (var field in typeof(General).GetFields())
                    {
                        var fieldValue = GetFieldValue(field);

                        array.Add(CreateJsonItem(Root + fieldValue.Oid, fieldValue.Description));
                    }

                    return array;
                }

                public static async Task WriteMibAsync(SnmpMibWriter writer)
                {
                    foreach (var field in typeof(General).GetFields())
                    {
                        var fieldValue = GetFieldValue(field);

                        await writer.WriteObjectAsync(field.Name, "server", fieldValue.TypeCode.Value, fieldValue.Description, fieldValue.Oid);
                    }
                }
            }

            public static Dictionary<string, string> CreateMapping()
            {
                var dict = General.CreateMapping().Concat(Indexes.CreateMapping(0)).ToDictionary();
                foreach (var field in typeof(Databases).GetFields())
                {
                    var fieldValue = GetFieldValue(field);
                    var oid = string.Format(fieldValue.Oid, 0);
                    dict.Add(Root + oid, fieldValue.Description);
                }

                return dict;
            }
            
            public static DynamicJsonValue ToJson(ServerStore serverStore, TransactionOperationContext context)
            {
                var djv = new DynamicJsonValue
                {
                    [$"@{nameof(General)}"] = General.ToJson()
                };

                var mapping = SnmpWatcher.GetMapping(serverStore, context);

                foreach (var kvp in mapping)
                {
                    using (var record = serverStore.Cluster.ReadRawDatabaseRecord(context, kvp.Key))
                    {
                        if (record == null)
                            continue;

                        var array = new DynamicJsonArray();
                        foreach (var field in typeof(Databases).GetFields())
                        {
                            var fieldValue = GetFieldValue(field);
                            var oid = string.Format(fieldValue.Oid, kvp.Value);
                            array.Add(CreateJsonItem(Root + oid, fieldValue.Description));
                        }

                        djv[kvp.Key] = new DynamicJsonValue
                        {
                            [$"@{nameof(General)}"] = array,
                            [nameof(Indexes)] = Indexes.ToJson(serverStore, context, record, kvp.Value)
                        };
                    }
                }

                return djv;
            }
        }

        public static Dictionary<string, string> CreateMapping()
        {
            return Server.CreateMapping()
                .Union(Databases.CreateMapping())
                .Union(Cluster.CreateMapping())
                .ToDictionary();
        }
        
        private static (string Oid, string Description, Type Type, SnmpType? TypeCode) GetFieldValue(FieldInfo field)
        {
            return (field.GetRawConstantValue().ToString(), field.GetCustomAttribute<DescriptionAttribute>()?.Description, field.GetCustomAttribute<SnmpEnumIndexAttribute>()?.Type, field.GetCustomAttribute<SnmpDataTypeAttribute>()?.TypeCode);
        }

        private static DynamicJsonValue CreateJsonItem(string oid, string description)
        {
            return new DynamicJsonValue
            {
                ["OID"] = oid,
                ["Description"] = description
            };
        }
    }
}
