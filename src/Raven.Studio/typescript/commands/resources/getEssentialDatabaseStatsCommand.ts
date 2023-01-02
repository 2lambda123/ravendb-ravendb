import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class getEssentialDatabaseStatsCommand extends commandBase {

    private db: database;

    constructor(db: database) {
        super();
        this.db = db;
    }

    execute(): JQueryPromise<Raven.Client.Documents.Operations.EssentialDatabaseStatistics> {
        const url = endpoints.databases.stats.statsEssential;
        return this.query<Raven.Client.Documents.Operations.EssentialDatabaseStatistics>(url, null, this.db)
            .fail((response: JQueryXHR) => this.reportError("Failed to load database statistics", response.responseText, response.statusText));
    }
}

export = getEssentialDatabaseStatsCommand;
