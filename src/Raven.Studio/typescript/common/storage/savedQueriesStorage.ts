/// <reference path="../../../typings/tsd.d.ts" />

import database = require("models/resources/database");
import storageKeyProvider = require("common/storage/storageKeyProvider");

class savedQueriesStorage {

    static getSavedQueriesWithIndexNameCheck(db: database): JQueryPromise<storedQueryDto[]> {
        const saved = this.getSavedQueries(db);
        return $.when(saved);
    }

    static getSavedQueries(db: database): storedQueryDto[] {
        const localStorageName = savedQueriesStorage.getLocalStorageKey(db.name);
        let savedQueriesFromLocalStorage: storedQueryDto[] = this.getSavedQueriesFromLocalStorage(localStorageName);

        if (savedQueriesFromLocalStorage == null || savedQueriesFromLocalStorage instanceof Array === false) {
            localStorage.setObject(localStorageName, []);
            savedQueriesFromLocalStorage = [];
        }

        return savedQueriesFromLocalStorage;
    }

    static storeSavedQueries(db: database, savedQueries: storedQueryDto[]) {
        const localStorageName = savedQueriesStorage.getLocalStorageKey(db.name);
        localStorage.setObject(localStorageName, savedQueries);
    }

    static removeRecentQueryByQueryText(db: database, queryText: string) {
        const localStorageName = savedQueriesStorage.getLocalStorageKey(db.name);
        const savedQueriesFromLocalStorage: storedQueryDto[] = this.getSavedQueriesFromLocalStorage(localStorageName);
        if (savedQueriesFromLocalStorage == null)
            return;

        const newSavedQueries = savedQueriesFromLocalStorage.filter((query: storedQueryDto) => query.queryText !== queryText);
        localStorage.setObject(localStorageName, newSavedQueries);
    }

    static removeSavedQueries(db: database) {
        const localStorageName = savedQueriesStorage.getLocalStorageKey(db.name);
        localStorage.setObject(localStorageName, []);
    }

    private static getLocalStorageKey(dbName: string) {
        return storageKeyProvider.storageKeyFor("savedQueries." + dbName);
    }
   
    private static getSavedQueriesFromLocalStorage(localStorageName: string): storedQueryDto[]  {
        let savedQueriesFromLocalStorage: storedQueryDto[] = null;
        try {
            savedQueriesFromLocalStorage = localStorage.getObject(localStorageName);
        } catch(err) {
            //no need to do anything
        }
        return savedQueriesFromLocalStorage;
    }

    static appendQuery(query: storedQueryDto, savedQueries: KnockoutObservableArray<storedQueryDto>): void {
        const existing = savedQueries().find(q => q.hash === query.hash);
        if (existing) {
            savedQueries.remove(existing);
            savedQueries.unshift(existing);
        } else {
            savedQueries.unshift(query);
        }

        // Limit us to 15 query recent runs.
        if (savedQueries().length > 15) {
            savedQueries.pop();
        }
    }

    static onDatabaseDeleted(qualifer: string, name: string) {
        const localStorageName = savedQueriesStorage.getLocalStorageKey(name);
        localStorage.removeItem(localStorageName);
    }
}

export = savedQueriesStorage;
