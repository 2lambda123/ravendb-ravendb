import copyToClipboard = require("common/copyToClipboard");
import viewModelBase = require("viewmodels/viewModelBase");
import createSampleDataCommand = require("commands/database/studio/createSampleDataCommand");
import createSampleDataClassCommand = require("commands/database/studio/createSampleDataClassCommand");
import aceEditorBindingHandler = require("common/bindingHelpers/aceEditorBindingHandler");
import eventsCollector = require("common/eventsCollector");
import getCollectionsStatsCommand = require("commands/database/documents/getCollectionsStatsCommand");
import collectionsStats = require("models/database/documents/collectionsStats");
import appUrl = require("common/appUrl");

class createSampleData extends viewModelBase {

    classData = ko.observable<string>();
    canCreateSampleData = ko.observable<boolean>(false);
    justCreatedSampleData = ko.observable<boolean>(false);
    classesVisible = ko.observable<boolean>(false);

    constructor() {
        super();
        aceEditorBindingHandler.install();
    }

    generateSampleData() {
        eventsCollector.default.reportEvent("sample-data", "create");
        this.isBusy(true);

        new createSampleDataCommand(this.activeDatabase())
            .execute()
            .done(() => {
                this.canCreateSampleData(false);
                this.justCreatedSampleData(true);
            })
            .always(() => this.isBusy(false));
    }

    activate(args: any) {
        super.activate(args);
        this.updateHelpLink('OGRN53');

        return $.when<any>(this.fetchSampleDataClasses(), this.fetchCollectionsStats());
    }

    showCode() {
        this.classesVisible(true);

        const $pageHostRoot = $("#page-host-root");
        const $sampleDataMain = $(".sample-data-main");

        $pageHostRoot.animate({
            scrollTop: $sampleDataMain.height()
        }, 'fast');
    }

    copyClasses() {
        eventsCollector.default.reportEvent("sample-data", "copy-classes");
        copyToClipboard.copy(this.classData(), "Copied C# classes to clipboard.");
    }

    private fetchCollectionsStats() {
        new getCollectionsStatsCommand(this.activeDatabase())
            .execute()
            .done(stats => this.onCollectionsFetched(stats));
    }

    private onCollectionsFetched(stats: collectionsStats) {
        const nonSystemCollectionsCount = stats.collections.filter(x => !x.isSystemDocuments).length;
        this.canCreateSampleData(nonSystemCollectionsCount === 0);
    }

    private fetchSampleDataClasses(): JQueryPromise<string> {
        return new createSampleDataClassCommand(this.activeDatabase())
            .execute()
            .done((results: string) => {
                this.classData(results);
            });
    }

    private urlForDatabaseDocuments() {
        return appUrl.forDocuments("", this.activeDatabase());
    }
}

export = createSampleData; 
