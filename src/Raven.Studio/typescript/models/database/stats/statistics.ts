/// <reference path="../../../../typings/tsd.d.ts"/>
import indexStatistics = require("models/database/stats/indexStatistics");

class statistics {
    dataBaseId: string;
    lastDocEtag?: number;
    countOfIndexes: string;
    countOfDocuments: string;
    countOfTransformers: string;
    is64Bit: boolean;
    indexPerformanceURL: string;

    // The observable indexes array, ordered by type
    indexesByType = ko.observableArray<indexesWithType>(); 
    
    constructor(dbStats: Raven.Client.Data.DatabaseStatistics, indexStats: Raven.Client.Data.Indexes.IndexStats[]) {
        this.dataBaseId = dbStats.DatabaseId; 
        this.lastDocEtag = dbStats.LastDocEtag;
        this.countOfIndexes = dbStats.CountOfIndexes.toLocaleString();
        this.countOfDocuments = dbStats.CountOfDocuments.toLocaleString();
        this.countOfTransformers = dbStats.CountOfTransformers.toLocaleString();
        this.is64Bit = dbStats.Is64Bit;
        
        // 1. The array with all indexes from endpoint
        //TODO: use index stats
        const allIndexes = dbStats.Indexes.map(x => new indexStatistics(x)); 

        // 2. Create an array where indexes are ordered by type
        let indexesByTypeTemp = Array<indexesWithType>();
        allIndexes.forEach(index => {
            let existingEntry = indexesByTypeTemp.find(x => x.indexType === index.type);
            if (!existingEntry) {
                // A new type encountered
                const newType = new indexesWithType(index.type);
                newType.add(index);
                indexesByTypeTemp.push(newType);
            }
            else {
                // Type already exists, only add the index
                existingEntry.add(index);
            }
        });

        // 3. Sort by index name & type
        indexesByTypeTemp.forEach(x => { x.indexes.sort((a, b) => a.name > b.name ? 1 : -1); });
        indexesByTypeTemp.sort((a, b) => a.indexType > b.indexType ? 1 : -1);

        // 4. Update the observable array 
        this.indexesByType(indexesByTypeTemp);
    }
}

class indexesWithType {
    indexType: string;
    indexes: indexStatistics[];

    constructor(type: string) {
        this.indexType = type;
        this.indexes = [];
    }

    add(index: indexStatistics) {
        this.indexes.push(index);
    }
}

export = statistics;
