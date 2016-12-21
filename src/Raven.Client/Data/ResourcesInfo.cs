﻿
using System;
using System.Collections.Generic;
using Raven.Abstractions.Data;
using Raven.Client.Data.Indexes;

namespace Raven.Client.Data
{
    public class ResourcesInfo
    {
        public List<DatabaseInfo> Databases { get; set; }

        public List<FileSystemInfo> FileSystems { get; set; }

        //TODO: ts, cs

    }

    public class ResourceInfo
    {
        public string Name { get; set; }
        public bool Disabled { get; set; }

        public Size TotalSize { get; set; }

        public bool IsAdmin { get; set; }

        public int? Errors { get; set; }

        public int? Alerts { get; set; }

        public TimeSpan? UpTime { get; set; }

        public BackupInfo BackupInfo { get; set; }

        public List<string> Bundles { get; set; }

    }

    public class BackupInfo
    {
        public DateTime LastIncrementalBackup { get; set; }

        public DateTime LastFullBackup { get; set; }        

        public TimeSpan IncrementalBackupInterval { get; set; }

        public TimeSpan FullBackupInterval { get; set; }
        
    }

    public class DatabaseInfo : ResourceInfo
    {
        public bool RejectClients { get; set; }

        public IndexRunningStatus IndexingStatus { get; set; }

        public int? DocumentsCount { get; set; }

        public int? IndexesCount { get; set; }
    }

    public class FileSystemInfo : ResourceInfo
    {
        public int? FilesCount { get; set; }
        //TODO: fill with fs specific properties
    }
}