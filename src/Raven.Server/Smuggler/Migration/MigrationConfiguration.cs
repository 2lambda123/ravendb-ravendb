using System.Collections.Generic;
using Raven.Client.Documents.Smuggler;

namespace Raven.Server.Smuggler.Migration
{
    public class SingleDatabaseMigrationConfiguration : MigrationConfigurationBase
    {
        public DatabaseMigrationSettings MigrationSettings { get; set; }
    }

    public class DatabasesMigrationConfiguration : MigrationConfigurationBase
    {
        public DatabasesMigrationConfiguration()
        {
            Databases = new List<DatabaseMigrationSettings>();
        }

        public List<DatabaseMigrationSettings> Databases { get; set; }
    }

    public class DatabaseMigrationSettings
    {
        public string DatabaseName { get; set; }

        public DatabaseItemType OperateOnTypes { get; set; }

        public bool RemoveAnalyzers { get; set; }

        public bool ImportRavenFs { get; set; }
    }

    public abstract class MigrationConfigurationBase
    {
        public MajorVersion BuildMajorVersion { get; set; }

        public int BuildVersion { get; set; }

        public string ServerUrl { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public string Domain { get; set; }
    }
}
