﻿using System;
using System.Threading.Tasks;
using Raven.Client.Documents.Operations.Backups;
using Raven.Client.Exceptions;
using Raven.Client.ServerWide.Operations;
using Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Server.Documents.PeriodicBackup.Restore
{
    public class RestoreFromAwsS3 : RestoreFromS3
    {
        public RestoreFromAwsS3(ITestOutputHelper output) : base(output)
        {
        }

        [Fact, Trait("Category", "Smuggler")]
        public void restore_s3_settings_tests()
        {
            using (var store = GetDocumentStore(new Options
            {
                ModifyDatabaseName = s => $"{s}_2"
            }))
            {
                var databaseName = $"restored_database-{Guid.NewGuid()}";
                var restoreConfiguration = new RestoreFromS3Configuration
                {
                    DatabaseName = databaseName
                };

                var restoreBackupTask = new RestoreBackupOperation(restoreConfiguration);

                var e = Assert.Throws<RavenException>(() => store.Maintenance.Server.Send(restoreBackupTask));
                Assert.Contains("AWS Access Key cannot be null or empty", e.InnerException.Message);

                restoreConfiguration.Settings.AwsAccessKey = "test";
                restoreBackupTask = new RestoreBackupOperation(restoreConfiguration);
                e = Assert.Throws<RavenException>(() => store.Maintenance.Server.Send(restoreBackupTask));
                Assert.Contains("AWS Secret Key cannot be null or empty", e.InnerException.Message);

                restoreConfiguration.Settings.AwsSecretKey = "test";
                restoreBackupTask = new RestoreBackupOperation(restoreConfiguration);
                e = Assert.Throws<RavenException>(() => store.Maintenance.Server.Send(restoreBackupTask));
                Assert.Contains("AWS Bucket Name cannot be null or empty", e.InnerException.Message);

                restoreConfiguration.Settings.BucketName = "test";
                restoreBackupTask = new RestoreBackupOperation(restoreConfiguration);
                e = Assert.Throws<RavenException>(() => store.Maintenance.Server.Send(restoreBackupTask));
                Assert.Contains("AWS Region Name cannot be null or empty", e.InnerException.Message);
            }
        }

        [AmazonS3RetryTheory, Trait("Category", "Smuggler")]
        [InlineData(BackupUploadMode.Default)]
        [InlineData(BackupUploadMode.DirectUpload)]
        public async Task can_backup_and_restore(BackupUploadMode backupUploadMode) => await can_backup_and_restore_internal(backupUploadMode: backupUploadMode);

        [AmazonS3RetryFact, Trait("Category", "Smuggler")]
        public async Task can_backup_and_restore_no_ascii() => await can_backup_and_restore_internal("żżżרייבן");

        [AmazonS3RetryTheory, Trait("Category", "Smuggler")]
        [InlineData(BackupUploadMode.Default)]
        [InlineData(BackupUploadMode.DirectUpload)]
        public async Task can_backup_and_restore_snapshot(BackupUploadMode backupUploadMode) => await can_backup_and_restore_snapshot_internal(backupUploadMode);

        [AmazonS3RetryTheory, Trait("Category", "Smuggler")]
        [InlineData(BackupUploadMode.Default)]
        [InlineData(BackupUploadMode.DirectUpload)]
        public async Task incremental_and_full_backup_encrypted_db_and_restore_to_encrypted_DB_with_database_key(BackupUploadMode backupUploadMode) => 
            await incremental_and_full_backup_encrypted_db_and_restore_to_encrypted_DB_with_database_key_internal(backupUploadMode);

        [AmazonS3RetryFact, Trait("Category", "Smuggler")]
        public async Task incremental_and_full_check_last_file_for_backup() => await incremental_and_full_check_last_file_for_backup_internal();

        [AmazonS3RetryFact, Trait("Category", "Smuggler")]
        public async Task incremental_and_full_backup_encrypted_db_and_restore_to_encrypted_DB_with_provided_key() => 
            await incremental_and_full_backup_encrypted_db_and_restore_to_encrypted_DB_with_provided_key_internal();

        [AmazonS3RetryTheory, Trait("Category", "Smuggler")]
        [InlineData(BackupUploadMode.Default)]
        [InlineData(BackupUploadMode.DirectUpload)]
        public async Task snapshot_encrypted_db_and_restore_to_encrypted_DB(BackupUploadMode backupUploadMode) => 
            await snapshot_encrypted_db_and_restore_to_encrypted_DB_internal(backupUploadMode);
    }
}
