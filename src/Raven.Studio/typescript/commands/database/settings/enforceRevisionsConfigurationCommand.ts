import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class enforceRevisionsConfigurationCommand extends commandBase {
    private readonly db: database | string;
    private readonly includeForceCreated: boolean;
    private readonly collections: string[];
    
    constructor(db: database | string, includeForceCreated = false, collections: string[] = null) {
        super();
        
        this.db = db;
        this.includeForceCreated = includeForceCreated;
        this.collections = collections;
    }

    execute(): JQueryPromise<operationIdDto> {
        const url = endpoints.databases.adminRevisions.adminRevisionsConfigEnforce;
        
        const args:  Raven.Client.Documents.Operations.Revisions.EnforceRevisionsConfigurationOperation.Parameters = {
            IncludeForceCreated: this.includeForceCreated,
            Collections: this.collections ?? undefined
        };

        return this.post<void>(url, JSON.stringify(args), this.db)
            .fail((response: JQueryXHR) => {
                this.reportError("Failed to enforce revisions configuration", response.responseText, response.statusText); 
            });
    }
}

export = enforceRevisionsConfigurationCommand; 

