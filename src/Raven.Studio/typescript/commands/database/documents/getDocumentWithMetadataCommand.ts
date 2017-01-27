import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import document = require("models/database/documents/document");
import endpoints = require("endpoints");

class getDocumentWithMetadataCommand extends commandBase {

    constructor(private id: string, private db: database, private shouldResolveNotFoundAsNull: boolean = false) {
        super();

        if (!id) {
            throw new Error("Must specify ID");
        }
    }

    // we can't use JQueryPromise<document> here as it actually can return any schema
    execute(): JQueryPromise<any> {
        const documentResult = $.Deferred<any>();
        const postResult = this.post(endpoints.databases.document.docs, JSON.stringify([this.id]), this.db);
        postResult.fail((xhr: JQueryXHR) => {
            if (this.shouldResolveNotFoundAsNull && xhr.status === 404) {
                documentResult.resolve(null);
            } else {
                documentResult.reject(xhr);
            }
        });
        postResult.done((queryResult: queryResultDto<documentDto>) => {
            if (queryResult.Results.length === 0) {
                if (this.shouldResolveNotFoundAsNull) {
                    documentResult.resolve(null);
                } else {
                    documentResult.reject("Unable to find document with ID " + this.id);
                }
            } else {
                documentResult.resolve(new document(queryResult.Results[0]));
            }
        });

        return documentResult;
    }
 }

 export = getDocumentWithMetadataCommand;
