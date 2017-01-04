import viewModelBase = require("viewmodels/viewModelBase");
import getIndexTermsCommand = require("commands/database/index/getIndexTermsCommand");
import getIndexEntriesFieldsCommand = require("commands/database/index/getIndexEntriesFieldsCommand");

type termsForField = {
    name: string;
    terms: KnockoutObservableArray<string>;
    fromValue: string;
    hasMoreTerms: KnockoutObservable<boolean>;
    loadInProgress: KnockoutObservable<boolean>;
}

class indexTerms extends viewModelBase {

    fields = ko.observableArray<termsForField>();
    indexName: string;

    indexPageUrl: KnockoutComputed<string>;

    static readonly termsPageLimit = 100; //TODO: consider higher value?

    activate(indexName: string): JQueryPromise<string[]> {
        super.activate(indexName);

        this.indexName = indexName;
        this.indexPageUrl = this.appUrls.editIndex(this.indexName);
        return this.fetchIndexEntriesFields(indexName);
    }

    fetchIndexEntriesFields(indexName: string) {
        return new getIndexEntriesFieldsCommand(indexName, this.activeDatabase())
            .execute()
            .done((fields: string[]) => this.processFields(fields));
    }

    static createTermsForField(fieldName: string): termsForField {
        return {
            fromValue: null,
            name: fieldName,
            hasMoreTerms: ko.observable<boolean>(true),
            terms: ko.observableArray<string>(),
            loadInProgress: ko.observable<boolean>(false)
        }
    }

    private processFields(fields: string[]) {
        this.fields(fields.map(fieldName => indexTerms.createTermsForField(fieldName)));

        this.fields()
            .forEach(field => this.loadTerms(this.indexName, field));
    }

    private loadTerms(indexName: string, termsForField: termsForField): JQueryPromise<Raven.Server.Documents.Queries.TermsQueryResult> {  // fetch one more to find out if we have more
        return new getIndexTermsCommand(indexName, termsForField.name, this.activeDatabase(), indexTerms.termsPageLimit + 1, termsForField.fromValue)  
            .execute()
            .done((loadedTermsResponse: Raven.Server.Documents.Queries.TermsQueryResult) => {
                let loadedTerms = loadedTermsResponse.Terms;
                if (loadedTerms.length > indexTerms.termsPageLimit) {
                    termsForField.hasMoreTerms(true);
                    loadedTerms = loadedTerms.slice(0, indexTerms.termsPageLimit);
                } else {
                    termsForField.hasMoreTerms(false);
                }
                termsForField.terms.push(...loadedTerms);
                if (loadedTerms.length > 0) {
                    termsForField.fromValue = loadedTerms[loadedTerms.length - 1];
                }
            });
    }

    loadMore(fieldName: string) {
        const field = this.fields().find(x => x.name === fieldName);

        if (!field || !field.hasMoreTerms()) {
            return;
        }
        field.loadInProgress(true);

        this.loadTerms(this.indexName, field)
            .always(() => field.loadInProgress(false));
    }
}

export = indexTerms;
