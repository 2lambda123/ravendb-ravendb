import commandBase = require("commands/commandBase");
import endpoints = require("endpoints");

class getServerWideBackupCommand extends commandBase {
    constructor(private backupName: string) {
        super();
    }

    // Return specific server-wide Backup task by its name
    execute(): JQueryPromise<Raven.Server.Web.System.ServerWideBackupConfigurationForStudio[]> {
        const url = endpoints.global.adminServerWide.adminConfigurationServerWideTasksForStudio + this.urlEncodeArgs({ name: this.backupName });

        const deferred = $.Deferred<Raven.Server.Web.System.ServerWideBackupConfigurationForStudio[]>();

        this.query<Raven.Server.Web.System.ServerWideTaskConfigurations>(url, null)
            .done((result: Raven.Server.Web.System.ServerWideTaskConfigurations) => deferred.resolve(result.Backups))
            .fail((response: JQueryXHR) => {
                this.reportError(`Failed to get Server-Wide Backup: ${this.backupName}`, response.responseText, response.statusText);
                deferred.reject();
            });

        return deferred;
    }
}

export = getServerWideBackupCommand;
