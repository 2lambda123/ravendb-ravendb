﻿import indexDefinition from "models/database/index/indexDefinition";
import testIndexCommand from "commands/database/index/testIndexCommand";
import database from "models/resources/database";
import virtualGridController from "widgets/virtualGrid/virtualGridController";
import documentObject = require("models/database/documents/document");
import columnPreviewPlugin from "widgets/virtualGrid/columnPreviewPlugin";
import columnsSelector = require("viewmodels/partial/columnsSelector");
import eventsCollector from "common/eventsCollector";
import documentBasedColumnsProvider = require("widgets/virtualGrid/columns/providers/documentBasedColumnsProvider");
import textColumn from "widgets/virtualGrid/columns/textColumn";
import virtualColumn from "widgets/virtualGrid/columns/virtualColumn";
import TestIndexResult = Raven.Server.Documents.Indexes.Test.TestIndexResult;
import assertUnreachable from "components/utils/assertUnreachable";
import { highlight, languages } from "prismjs";

type testTabName = "queryResults" | "indexEntries" | "mapResults" | "reduceResults";
type fetcherType = (skip: number, take: number) => JQueryPromise<pagedResult<documentObject>>;

class testIndex {
    spinners = {
        testing: ko.observable<boolean>(false)
    };

    private readonly indexDefinitionProvider: () => indexDefinition;
    private readonly dbProvider: () => database;

    testTimeLimit = ko.observable<number>();
    testScanLimit = ko.observable<number>(10_000);

    gridController = ko.observable<virtualGridController<any>>();
    columnsSelector = new columnsSelector<documentObject>();
    fetchTask: JQueryPromise<TestIndexResult>;
    resultsFetcher = ko.observable<fetcherType>();
    effectiveFetcher = ko.observable<fetcherType>();
    private columnPreview = new columnPreviewPlugin<documentObject>();

    isFirstRun = true;

    resultsCount = ko.observable<Record<testTabName, number>>({
        "queryResults": 0,
        "indexEntries": 0,
        "mapResults": 0,
        "reduceResults": 0
    });

    currentTab = ko.observable<testTabName>(null);

    constructor(dbProvider: () => database, indexDefinitionProvider: () => indexDefinition) {
        this.dbProvider = dbProvider;
        this.indexDefinitionProvider = indexDefinitionProvider;
    }

    toDto(): Raven.Server.Documents.Indexes.Test.TestIndexParameters {
        return {
            IndexDefinition: this.indexDefinitionProvider().toDto(),
            WaitForNonStaleResultsTimeoutInSeconds: this.testTimeLimit() ?? 15,
            Query: null, //TODO:
            QueryParameters: null, //TODO:
            MaxDocumentsToProcess: this.testScanLimit()
        }
    }

    goToTab(tabToUse: testTabName) {
        this.currentTab(tabToUse);

        switch (tabToUse) {
            case "queryResults": {
                this.effectiveFetcher(() => this.fetchTask.then((result): pagedResult<any> => {
                    return {
                        items: result.QueryResults.map(x => new documentObject(x)),
                        totalResultCount: result.QueryResults.length
                    }
                }));
                return;
            }
            case "indexEntries": {
                this.effectiveFetcher(() => this.fetchTask.then((result): pagedResult<any> => {
                    return {
                        items: result.IndexEntries.map(x => new documentObject(x)),
                        totalResultCount: result.IndexEntries.length
                    }
                }));
                return;
            }
            case "mapResults": {
                this.effectiveFetcher(() => this.fetchTask.then((result): pagedResult<any> => {
                    return {
                        items: result.MapResults.map(x => new documentObject(x)),
                        totalResultCount: result.MapResults.length
                    }
                }));
                return;
            }
            case "reduceResults": {
                this.effectiveFetcher(() => this.fetchTask.then((result): pagedResult<any> => {
                    return {
                        items: result.ReduceResults.map(x => new documentObject(x)),
                        totalResultCount: result.ReduceResults.length
                    }
                }));
                return;
            }
            default:
                assertUnreachable(tabToUse);
        }
    }

    runTest() {
        const db = this.dbProvider();

        eventsCollector.default.reportEvent("index", "test");

        this.columnsSelector.reset();

        this.fetchTask = this.fetchTestDocuments(db);

        this.goToTab("queryResults");

        if (this.isFirstRun) {
            const documentsProvider = new documentBasedColumnsProvider(db, this.gridController(), {
                showRowSelectionCheckbox: false,
                showSelectAllCheckbox: false,
                enableInlinePreview: true,
                createHyperlinks: false,
                customInlinePreview: doc => documentBasedColumnsProvider.showPreview(doc, "Entry preview")
            });

            this.columnsSelector.init(this.gridController(),
                (s, t) => this.effectiveFetcher()(s, t),
                (w, r) => documentsProvider.findColumns(w, r, ["__metadata"]),
                (results: pagedResult<documentObject>) => documentBasedColumnsProvider.extractUniquePropertyNames(results));

            const grid = this.gridController();
            grid.headerVisible(true);

            this.columnPreview.install("virtual-grid", ".js-index-test-tooltip", 
                (doc: documentObject, column: virtualColumn, e: JQueryEventObject, onValue: (context: any, valueToCopy: string) => void) => {
                if (column instanceof textColumn) {
                    const value = column.getCellValue(doc);
                    if (!_.isUndefined(value)) {
                        const json = JSON.stringify(value, null, 4);
                        const html = highlight(json, languages.javascript, "js");
                        onValue(html, json);
                    }
                }
            });

            this.effectiveFetcher.subscribe(() => {
                this.columnsSelector.reset();
                grid.reset();
            });
        }

        this.isFirstRun = false;
    }

    private fetchTestDocuments(db: database): JQueryPromise<TestIndexResult> {
        this.spinners.testing(true);

        const dto = this.toDto();

        return new testIndexCommand(dto, db).execute()
            .done(result => {
                this.resultsCount({
                    queryResults: result.QueryResults.length,
                    indexEntries: result.IndexEntries.length,
                    mapResults: result.MapResults.length,
                    reduceResults: result.ReduceResults.length
                });
            })
            .always(() => this.spinners.testing(false));
    }
}


export = testIndex;
