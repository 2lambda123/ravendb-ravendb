import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");
import document = require("models/database/documents/document");
import documentsPreviewCommand = require("commands/database/documents/getDocumentsPreviewCommand");

class testSubscriptionTaskCommand extends commandBase {

    constructor(private db: database, private subscriptionSettings: subscriptionDataFromUI, private resultsLimit: number) {
        super();
    }

    execute(): JQueryPromise<pagedResult<document>> {
        return this.testSubscription()
            .fail((response: JQueryXHR) => {
                this.reportError("Failed to test subscription task", response.responseText, response.statusText);
            });
    }

    private testSubscription(): JQueryPromise<pagedResult<document>> {
        
        const args = { pageSize: this.resultsLimit }; 

        const url = endpoints.databases.subscriptions.subscriptionsTry + this.urlEncodeArgs(args);

        const testTask = $.Deferred<pagedResult<document>>();

        const subscriptionToTest = {
            ChangeVector: this.subscriptionSettings.ChangeVectorEntry,
            Collection: this.subscriptionSettings.Collection,
            Script: this.subscriptionSettings.Script,
            IsVersioned: this.subscriptionSettings.IsVersioned
        };

        this.post(url, JSON.stringify(subscriptionToTest), this.db)
            .done((dto: resultsDto<documentDto>) => { 

                const result = {
                    items: dto.Results.map(x => this.mapToDocument(x)),
                    totalResultCount: dto.Results.length
                } as pagedResult<document>;

                testTask.resolve(result);
            })
            .fail(response => testTask.reject(response));

        return testTask;
    }

    private mapToDocument(docDto: documentDto) {
        const doc = new document(docDto);

        const metadata = doc.__metadata as any;

        const objectStubs = metadata[documentsPreviewCommand.ObjectStubsKey] as string[];
        if (objectStubs) {
            objectStubs.forEach(stub => (doc as any)[stub] = {});
        }

        const arrayStubs = metadata[documentsPreviewCommand.ArrayStubsKey] as string[];
        if (arrayStubs) {
            arrayStubs.forEach(stub => (doc as any)[stub] = []);
        }

        const trimmedValues = metadata[documentsPreviewCommand.TrimmedValueKey] as string[];
        if (trimmedValues) {
            trimmedValues.forEach(trimmedKey => {
                (doc as any)[trimmedKey] += "...";
            });
        }

        return doc;
    }
}

export = testSubscriptionTaskCommand; 

