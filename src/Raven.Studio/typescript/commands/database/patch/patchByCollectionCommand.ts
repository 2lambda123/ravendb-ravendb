import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class patchByCollectionCommand extends commandBase {

    constructor(private collectionName: string, private patchRequest: Raven.Server.Documents.Patch.PatchRequest, private db: database) {
        super();
    }

    execute(): JQueryPromise<operationIdDto> {
        const url = endpoints.databases.collections.collectionsDocs;
        const args = {
            name: this.collectionName
        };

        return this.patch(url + this.urlEncodeArgs(args), JSON.stringify(this.patchRequest), this.db)
            .done((response: operationIdDto) => {
                this.reportSuccess("Scheduled patch of collection: " + this.collectionName);
            })
            .fail((response: JQueryXHR) => this.reportError("Failed to schedule patch of collection " + this.collectionName, response.responseText, response.statusText));
    }

}

export = patchByCollectionCommand; 
