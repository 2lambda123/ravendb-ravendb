import database = require("models/resources/database");
import document = require("models/database/documents/document");
import getDocumentsPreviewCommand = require("commands/database/documents/getDocumentsPreviewCommand");

class collection {
    static readonly allDocumentsCollectionName = "All Documents";
    static readonly systemDocumentsCollectionName = "@system";
    static readonly zombiesCollectionName = "Zombies";

    documentCount: KnockoutObservable<number> = ko.observable(0);
    name: string;

    private db: database;

    constructor(name: string, ownerDatabase: database, docCount: number = 0) {
        this.name = name;
        this.db = ownerDatabase;
        this.documentCount(docCount);
    }

    get isSystemDocuments() {
        return this.name === collection.systemDocumentsCollectionName;
    }

    get isAllDocuments() {
        return this.name === collection.allDocumentsCollectionName;
    }

    get database() {
        return this.db;
    }

    fetchDocuments(skip: number, take: number, previewColumns?: string[], fullColumns?: string[]): JQueryPromise<pagedResultWithAvailableColumns<document>> {
        if (this.isAllDocuments) {
            return new getDocumentsPreviewCommand(this.db, skip, take, undefined, previewColumns, fullColumns)
                .execute();
        } else {
            return new getDocumentsPreviewCommand(this.db, skip, take, this.name, previewColumns, fullColumns)
                .execute();
        }
    }

    static createAllDocumentsCollection(ownerDatabase: database, documentsCount: number): collection {
        return new collection(collection.allDocumentsCollectionName, ownerDatabase, documentsCount);
    }

}

export = collection;
