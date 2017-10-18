﻿/// <reference path="../../../../typings/tsd.d.ts"/>

class importDatabaseModel {
    includeDocuments = ko.observable(true);
    includeConflicts = ko.observable(true);
    includeIndexes = ko.observable(true);
    includeIdentities = ko.observable(true);
    includeRevisionDocuments = ko.observable(true);
    revisionsAreConfigured: KnockoutComputed<boolean>;

    includeExpiredDocuments = ko.observable(true);
    removeAnalyzers = ko.observable(false);
    
    transformScript = ko.observable<string>();

    validationGroup: KnockoutValidationGroup;
    importDefinitionHasIncludes: KnockoutComputed<boolean>;
    
    constructor() {
        this.initValidation();
    }

    toDto(): Raven.Client.Documents.Smuggler.DatabaseSmugglerImportOptions {
        const operateOnTypes: Array<Raven.Client.Documents.Smuggler.DatabaseItemType> = [];
        if (this.includeDocuments()) {
            operateOnTypes.push("Documents");
        }
        if (this.includeConflicts()) {
            operateOnTypes.push("Conflicts");
        }
        if (this.includeIndexes()) {
            operateOnTypes.push("Indexes");
        }
        if (this.includeRevisionDocuments()) {
            operateOnTypes.push("RevisionDocuments");
        }
        if (this.includeIdentities()){
            operateOnTypes.push("Identities");
        }

        return {
            IncludeExpired: this.includeExpiredDocuments(),
            TransformScript: this.transformScript(),
            RemoveAnalyzers: this.removeAnalyzers(),
            OperateOnTypes: operateOnTypes.join(",") as Raven.Client.Documents.Smuggler.DatabaseItemType
        } as Raven.Client.Documents.Smuggler.DatabaseSmugglerImportOptions;
    }

    private initValidation() {
        this.importDefinitionHasIncludes = ko.pureComputed(() => {
            return this.includeDocuments()  ||
                   this.includeIndexes()    ||
                   this.includeIdentities() ||
                   (this.includeRevisionDocuments() && this.revisionsAreConfigured());
        });

        this.transformScript.extend({
            aceValidation: true
        });

        this.importDefinitionHasIncludes.extend({
            validation: [
                {
                    validator: () => this.importDefinitionHasIncludes(),
                    message: "Note: At least one 'include' option must be checked..."
                }
            ]
        });

        this.validationGroup = ko.validatedObservable({
            transformScript: this.transformScript,
            importDefinitionHasIncludes: this.importDefinitionHasIncludes
        });
    }
}

export = importDatabaseModel;
