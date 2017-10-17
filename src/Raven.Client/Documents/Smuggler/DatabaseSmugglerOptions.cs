﻿namespace Raven.Client.Documents.Smuggler
{
    public class DatabaseSmugglerOptions : IDatabaseSmugglerOptions
    {
        private const DatabaseItemType DefaultOperateOnTypes = DatabaseItemType.Indexes |
                                                               DatabaseItemType.Documents | DatabaseItemType.RevisionDocuments |
                                                               DatabaseItemType.Conflicts |
                                                               DatabaseItemType.Identities;

        private const int DefaultMaxStepsForTransformScript = 10 * 1000;

        public DatabaseSmugglerOptions()
        {
            OperateOnTypes = DefaultOperateOnTypes;
            MaxStepsForTransformScript = DefaultMaxStepsForTransformScript;
            IncludeExpired = true;
        }

        public DatabaseItemType OperateOnTypes { get; set; }

        public bool IncludeExpired { get; set; }

        public bool RemoveAnalyzers { get; set; }

        public string TransformScript { get; set; }

        public int MaxStepsForTransformScript { get; set; }
    }

    internal interface IDatabaseSmugglerOptions
    {
        DatabaseItemType OperateOnTypes { get; set; }
        bool IncludeExpired { get; set; }
        bool RemoveAnalyzers { get; set; }
        string TransformScript { get; set; }
        int MaxStepsForTransformScript { get; set; }
    }
}
