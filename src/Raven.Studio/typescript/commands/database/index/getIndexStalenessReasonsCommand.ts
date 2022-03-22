import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class getIndexStalenessReasonsCommand extends commandBase {

    constructor(private indexName: string, private db: database, private location: databaseLocationSpecifier) {
        super();
    }

    execute(): JQueryPromise<indexStalenessReasonsResponse> {
        const args = {
            name: this.indexName,
            ...this.location
        };
        const url = endpoints.databases.index.indexesStaleness;

        return this.query<indexStalenessReasonsResponse>(url, args, this.db)
            .fail((response: JQueryXHR) => {
                this.reportError("Failed to get index staleness reasons.", response.responseText, response.statusText);
            });
    }
}

export = getIndexStalenessReasonsCommand;
