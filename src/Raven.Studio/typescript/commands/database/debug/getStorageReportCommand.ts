import commandBase = require("commands/commandBase");
import endpoints = require("endpoints");
import database = require("models/resources/database");

class getStorageReportCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<storageReportItemDto[]> {
        const url = endpoints.databases.storage.debugStorageReport;
        return this.query<storageReportItemDto[]>(url, null, this.db)
            .fail((response: JQueryXHR) => this.reportError("Failed to load storage report", response.responseText, response.statusText));
    }
}

export = getStorageReportCommand;
