import pagedList = require("common/pagedList");
import getDocumentsFromCollectionCommand = require("commands/database/documents/getDocumentsFromCollectionCommand");
import getAllDocumentsCommand = require("commands/database/documents/getAllDocumentsCommand");
import pagedResultSet = require("common/pagedResultSet");
import database = require("models/resources/database");
import cssGenerator = require("common/cssGenerator");

class collection implements ICollectionBase {
    colorClass = ""; 
    documentCount: KnockoutObservable<number> = ko.observable(0);
    documentsCountWithThousandsSeparator = ko.computed(() => this.documentCount().toLocaleString());
    isAllDocuments = false;
    bindings = ko.observable<string[]>();

    name: string;
    private documentsList: pagedList;
    static readonly allDocsCollectionName = "All Documents";
    static readonly systemDocusCollectionName = "@system";
    private static collectionColorMaps: resourceStyleMap[] = [];

    constructor(name: string, public ownerDatabase: database, docCount: number = 0) {
        this.name = name;
        this.isAllDocuments = name === collection.allDocsCollectionName;
        this.colorClass = collection.getCollectionCssClass(name, ownerDatabase);
        this.documentCount(docCount);
    }

    get isSystemDocuments() {
        return this.name === collection.systemDocusCollectionName;
    }

    prettyLabel(text: string) {
        return text.replace(/__/g, '/');
    }

    getDocuments(): pagedList {
        if (!this.documentsList) {
            this.documentsList = this.createPagedList();
        }

        return this.documentsList;
    }

    invalidateCache() {
        var documentsList = this.getDocuments();
        documentsList.invalidateCache();
    }

    clearCollection() {
        if (this.isAllDocuments && !!this.documentsList) {
            this.documentsList.clear();
        }
    }

    fetchDocuments(skip: number, take: number): JQueryPromise<pagedResultSet<any>> {
        if (this.isAllDocuments) {
            return new getAllDocumentsCommand(this.ownerDatabase, skip, take).execute();
        } else {
            return new getDocumentsFromCollectionCommand(this, skip, take).execute();
        }
    }

    static createAllDocsCollection(ownerDatabase: database, documentsCount: number): collection {
        return new collection(collection.allDocsCollectionName, ownerDatabase, documentsCount);
    }

    static getCollectionCssClass(entityName: string, db: database): string {
        if (entityName === collection.allDocsCollectionName) {
            return "all-documents-collection";
        }

        //TODO: any special color for system documents?

        return cssGenerator.getCssClass(entityName, collection.collectionColorMaps, db);
    }

    private createPagedList(): pagedList {
        var fetcher = (skip: number, take: number) => this.fetchDocuments(skip, take);
        var list = new pagedList(fetcher);
        list.collectionName = this.name;
        return list;
    }
}

export = collection;
