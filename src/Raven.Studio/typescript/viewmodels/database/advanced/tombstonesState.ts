import viewModelBase = require("viewmodels/viewModelBase");
import accessManager = require("common/shell/accessManager");
import appUrl = require("common/appUrl");
import getTombstonesStateCommand = require("commands/database/debug/getTombstonesStateCommand");
import virtualGridController = require("widgets/virtualGrid/virtualGridController");
import textColumn = require("widgets/virtualGrid/columns/textColumn");
import SubscriptionInfo = Raven.Server.Documents.TombstoneCleaner.TombstonesState.SubscriptionInfo;
import forceTombstonesCleanup = require("commands/database/debug/forceTombstonesCleanupCommand");

class tombstonesState extends viewModelBase {

    private collectionsStateController = ko.observable<virtualGridController<TombstoneItem>>();
    private subscriptionsStateController = ko.observable<virtualGridController<SubscriptionInfo>>();
    
    isForbidden = ko.observable<boolean>(false);
    
    state = ko.observable<TombstonesStateOnWire>();
    
    spinners = {
        force: ko.observable<boolean>(false),
        refresh: ko.observable<boolean>(false)
    };

    canActivate(args: any) {
        return $.when<any>(super.canActivate(args))
            .then(() => {
                const deferred = $.Deferred<canActivateResultDto>();

                this.isForbidden(!accessManager.default.isOperatorOrAbove());

                if (this.isForbidden()) {
                    deferred.resolve({ can: true });
                } else {
                   this.fetchState()
                       .then(() => deferred.resolve({ can: true }))
                       .fail(() => deferred.resolve({ redirect: appUrl.forStatus(this.activeDatabase()) }));
                }

                return deferred;
            });
    }
    
    compositionComplete() {
        super.compositionComplete();

        const collectionsGrid = this.collectionsStateController();
        collectionsGrid.headerVisible(true);

        collectionsGrid.init(() => this.collectionsFetcher(), () => {
            return [
                new textColumn<TombstoneItem>(collectionsGrid, x => x.Collection, "Collection", "26%", {
                    sortable: "string"
                }),
                new textColumn<TombstoneItem>(collectionsGrid, x => x.Documents.Component, "Doc Component", "15%", {
                    sortable: "string"
                }),
                new textColumn<TombstoneItem>(collectionsGrid, x => this.formatEtag(x.Documents.Etag), "Doc Etag", "8%", {
                    sortable: "number"
                }),

                new textColumn<TombstoneItem>(collectionsGrid, x => x.TimeSeries.Component, "Time Series Component", "15%", {
                    sortable: "string"
                }),
                new textColumn<TombstoneItem>(collectionsGrid, x => this.formatEtag(x.TimeSeries.Etag), "Time Series Etag", "8%", {
                    sortable: "number"
                }),

                new textColumn<TombstoneItem>(collectionsGrid, x => x.Counters.Component, "Counter Component", "15%", {
                    sortable: "string"
                }),
                new textColumn<TombstoneItem>(collectionsGrid, x => this.formatEtag(x.Counters.Etag), "Counter Etag", "8%", {
                    sortable: "number"
                }),
            ]
        });

        const subscriptionsGrid = this.subscriptionsStateController();
        subscriptionsGrid.headerVisible(true);

        subscriptionsGrid.init(() => this.subscriptionsFetcher(), () => {
            return [
                new textColumn<SubscriptionInfo>(subscriptionsGrid, x => x.Identifier, "Identifier", "30%", {
                    sortable: "string"
                }),
                new textColumn<SubscriptionInfo>(subscriptionsGrid, x => x.Type, "Type", "20%", {
                    sortable: "string"
                }),
                new textColumn<SubscriptionInfo>(subscriptionsGrid, x => x.Collection, "Collection", "25%", {
                    sortable: "string"
                }),
                new textColumn<SubscriptionInfo>(subscriptionsGrid, x => this.formatEtag(x.Etag), "Etag", "25%", {
                    sortable: "string"
                }),
            ]
        });
    }

    private collectionsFetcher(): JQueryPromise<pagedResult<TombstoneItem>> {
        const info = this.state().Results;
        return $.Deferred<pagedResult<any>>()
            .resolve({
                items: info,
                totalResultCount: info.length
            });
    }

    private subscriptionsFetcher(): JQueryPromise<pagedResult<SubscriptionInfo>> {
        const info = this.state().PerSubscriptionInfo;
        return $.Deferred<pagedResult<any>>()
            .resolve({
                items: info,
                totalResultCount: info.length
            });
    }

    private fetchState(): JQueryPromise<TombstonesStateOnWire> {
        return new getTombstonesStateCommand(this.activeDatabase())
            .execute()
            .done(state => {
                this.state(state);
            });
    }
    
    refresh() {
        this.spinners.refresh(true);
        this.refreshInternal()
            .always(() => {
                this.spinners.refresh(false);
            });
    }
    
    private refreshInternal() {
        return this.fetchState()
            .then(() => {
                this.collectionsStateController().reset();
                this.subscriptionsStateController().reset();
            });
    }

    forceCleanupDialog() {
        this.confirmationMessage("Are you sure?", "Do you want to force tombstones cleanup? ", {
            buttons: ["Cancel", "Yes, cleanup"]
        })
            .done(result => {
                    if (result.can) {
                        this.spinners.force(true);
                        
                        this.forceCleanup()
                            .then(() => this.refreshInternal())
                            .always(() => {
                                this.spinners.force(false);
                            });
                    }
                });
    }
    
    private forceCleanup(): JQueryPromise<number> {
        return new forceTombstonesCleanup(this.activeDatabase())
            .execute();
    }
    
    formatEtag(value: number) {
        if (value === 9223372036854776000) { 
            // in general Long.MAX_Value is 9223372036854775807 but we loose precision here
            return "(max value)";
        }
        
        return value;
    }
}

export = tombstonesState;
