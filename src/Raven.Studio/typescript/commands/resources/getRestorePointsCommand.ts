import commandBase = require("commands/commandBase");
import endpoints = require("endpoints");

class getRestorePointsCommand extends commandBase {

    private readonly path: string;
    private readonly nodeTag: string;
    private readonly shardNumber: number;

    private readonly skipReportingError: boolean;

    private readonly connectionType: Raven.Server.Documents.PeriodicBackup.PeriodicBackupConnectionType;

    private readonly credentials?: Raven.Client.Documents.Operations.Backups.BackupSettings;

    private constructor(path: string,
                        nodeTag: string, 
                        skipReportingError: boolean,
                        connectionType: Raven.Server.Documents.PeriodicBackup.PeriodicBackupConnectionType,
                        credentials?: Raven.Client.Documents.Operations.Backups.BackupSettings,
                        shardNumber?: number) {
        super();
        this.credentials = credentials;
        this.nodeTag = nodeTag;
        this.connectionType = connectionType;
        this.skipReportingError = skipReportingError;
        this.path = path;
        this.shardNumber = shardNumber;
    }

    private preparePayload() {
        if (this.connectionType === "Local") {
            return {
                FolderPath: this.path,
                ShardNumber: this.shardNumber
            };
        }
        
        return {
            ...this.credentials,
            ShardNumber: this.shardNumber
        };
    }
    
    execute(): JQueryPromise<Raven.Server.Documents.PeriodicBackup.Restore.RestorePoints> {
        const args = {
            type: this.connectionType
        };
        
        const url = endpoints.global.adminDatabases.adminRestorePoints + this.urlEncodeArgs(args);
        
        return this.post(url, JSON.stringify(this.preparePayload()))
            .fail((response: JQueryXHR) => {
                if (this.skipReportingError) {
                    return;
                }

                this.reportError(`Failed to get restore points for path: ${this.path}`,
                    response.responseText,
                    response.statusText);
            });
    }
    
    static forServerLocal(path: string, nodeTag: string, skipReportingError: boolean, shardNumber: number | undefined) {
        return new getRestorePointsCommand(path, nodeTag, skipReportingError, "Local", null, shardNumber);
    }
    
    static forS3Backup(credentials: Raven.Client.Documents.Operations.Backups.S3Settings, skipReportingError: boolean, shardNumber: number | undefined) {
        return new getRestorePointsCommand("", null, skipReportingError, "S3", credentials, shardNumber);
    }
    
    static forAzureBackup(credentials: Raven.Client.Documents.Operations.Backups.AzureSettings, skipReportingError: boolean, shardNumber: number | undefined) {
        return new getRestorePointsCommand("", null, skipReportingError, "Azure", credentials, shardNumber);
    }

    static forGoogleCloudBackup(credentials: Raven.Client.Documents.Operations.Backups.GoogleCloudSettings, skipReportingError: boolean, shardNumber: number | undefined) {
        return new getRestorePointsCommand("", null, skipReportingError, "GoogleCloud", credentials, shardNumber);
    }
}

export = getRestorePointsCommand; 
