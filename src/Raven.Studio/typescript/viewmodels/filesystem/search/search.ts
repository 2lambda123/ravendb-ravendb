import app = require("durandal/app");
import router = require("plugins/router");
import appUrl = require("common/appUrl");
import viewModelBase = require("viewmodels/viewModelBase");
import searchByQueryCommand = require("commands/filesystem/searchByQueryCommand");
import pagedResultSet = require("common/pagedResultSet");
import searchInFolderClause = require("viewmodels/filesystem/files/searchInFolderClause");
import searchSingleInputClause = require("viewmodels/filesystem/files/searchSingleInputClause");
import searchFileSizeRangeClause = require("viewmodels/filesystem/files/searchFileSizeRangeClause");
import searchHasMetadataClause = require("viewmodels/filesystem/files/searchHasMetadataClause");
import searchLastModifiedBetweenClause = require("viewmodels/filesystem/files/searchLastModifiedBetweenClause");
import deleteFilesMatchingQueryConfirm = require("viewmodels/filesystem/deleteFilesMatchingQueryConfirm");
import resetIndexConfirm = require("viewmodels/filesystem/search/resetIndexConfirm");
import queryUtil = require("common/queryUtil");
import eventsCollector = require("common/eventsCollector");

class search extends viewModelBase {

    private router = router;

    appUrls: computedAppUrls;

    searchUrl = appUrl.forCurrentDatabase().filesystemSearch;
    searchText = ko.observable("");
    allFilesPagedItems = ko.observable<any>(); //TODO: use type
    selectedFilesIndices = ko.observableArray<number>();

    static gridSelector = "#filesGrid";

    constructor() {
        super();

        this.searchText.extend({ throttle: 200 }).subscribe(s => this.searchFiles(s));
    }

    activate(args: any) {
        super.activate(args);
        this.updateHelpLink("SRTQ8C");
        this.appUrls = appUrl.forCurrentFilesystem();
        this.loadFiles();
    }

    clear() {
        this.searchText("");
    }

    search() {
        this.searchFiles(this.searchText());
    }


    searchFiles(query: string) {
        //TODO: this.allFilesPagedItems(this.createPagedList(query));
    }

    loadFiles() {
        //TODO: this.allFilesPagedItems(this.createPagedList(""));
    }

    /* TODO
    createPagedList(query: string): pagedList {
        var fetcher = (skip: number, take: number) => this.fetchFiles(query, skip, take);
        return new pagedList(fetcher);
    }*/

    fetchFiles(query: string, skip: number, take: number): JQueryPromise<pagedResultSet<any>> {
        var task = new searchByQueryCommand(this.activeFilesystem(), query, skip, take).execute();
        return task;
    }

    fileNameStartsWith() {
        eventsCollector.default.reportEvent("fs-files", "search", "starts-with");

        var searchSingleInputClauseViewModel: searchSingleInputClause = new searchSingleInputClause("Filename starts with: ");
        searchSingleInputClauseViewModel
            .applyFilterTask
                .done((input: string) => this.addToSearchInput("__fileName:" + queryUtil.escapeTerm(input) + "*"));
        app.showBootstrapDialog(searchSingleInputClauseViewModel);
    }

    fileNameEndsWith() {
        eventsCollector.default.reportEvent("fs-files", "search", "ends-with");

        var searchSingleInputClauseViewModel: searchSingleInputClause = new searchSingleInputClause("Filename ends with: ");
        searchSingleInputClauseViewModel
            .applyFilterTask
            .done((input: string) => {
                const stringReversed = input.split("").reverse().join("");
                this.addToSearchInput("__rfileName:" + queryUtil.escapeTerm(stringReversed) + "*");
            });
        app.showBootstrapDialog(searchSingleInputClauseViewModel);
    }

    fileSizeBetween() {
        eventsCollector.default.reportEvent("fs-files", "search", "size-between");

        var searchFileSizeRangeClauseViewModel: searchFileSizeRangeClause = new searchFileSizeRangeClause();
        searchFileSizeRangeClauseViewModel
            .applyFilterTask
            .done((input: string) => this.addToSearchInput(input));
        app.showBootstrapDialog(searchFileSizeRangeClauseViewModel);
    }

    hasMetadata() {
        eventsCollector.default.reportEvent("fs-files", "search", "has-metadata");

        var searchHasMetadataClauseViewModel: searchHasMetadataClause = new searchHasMetadataClause(this.activeFilesystem());
        searchHasMetadataClauseViewModel
            .applyFilterTask
                .done((input: string) => this.addToSearchInput(queryUtil.escapeTerm(input)));
        app.showBootstrapDialog(searchHasMetadataClauseViewModel);
    }

    inFolder() {
        eventsCollector.default.reportEvent("fs-files", "search", "in-folder");

        var inFolderViewModel: searchInFolderClause = new searchInFolderClause(this.activeFilesystem());
        inFolderViewModel 
            .applyFilterTask
            .done((input: string) => {
                if (!input.startsWith("/")) {
                    input = "/" + input;
                }
                var escaped = queryUtil.escapeTerm(input);
                this.addToSearchInput("__directoryName:" + escaped);
            });
        app.showBootstrapDialog(inFolderViewModel);
    }

    lastModifiedBetween() {
        eventsCollector.default.reportEvent("fs-files", "search", "modified-between");

        var searchLastModifiedBetweenClauseViewModel: searchLastModifiedBetweenClause = new searchLastModifiedBetweenClause();
        searchLastModifiedBetweenClauseViewModel
            .applyFilterTask
            .done((input: string) => this.addToSearchInput(input));
        app.showBootstrapDialog(searchLastModifiedBetweenClauseViewModel);
    }

    addToSearchInput(input: string) {
        var currentSearchText = this.searchText();
        if (currentSearchText != null && currentSearchText.trim().length > 0)
            currentSearchText += " AND ";
        this.searchText(currentSearchText + input);
    }

    deleteFilesMatchingQuery() {
        eventsCollector.default.reportEvent("fs-files", "delete");

        // Run the query so that we have an idea of what we'll be deleting.
        this.search();
        this.allFilesPagedItems()
            .fetch(0, 1)
            .done((results: pagedResultSet<any>) => {
                if (results.totalResultCount === 0) {
                    app.showBootstrapMessage("There are no files matching your query.", "Nothing to do");
                } else {
                    this.promptDeleteFilesMatchingQuery(results.totalResultCount);
                }
            });
    }

    promptDeleteFilesMatchingQuery(resultCount: number) {
        var viewModel = new deleteFilesMatchingQueryConfirm(this.searchText(), resultCount, this.activeFilesystem());
        app
            .showBootstrapDialog(viewModel)
            .done(() => this.search());
    }

    resetIndex() {
        eventsCollector.default.reportEvent("fs-index", "reset");

        var resetIndexVm = new resetIndexConfirm(this.activeFilesystem());
        app.showBootstrapDialog(resetIndexVm);
    }
}

export = search;
