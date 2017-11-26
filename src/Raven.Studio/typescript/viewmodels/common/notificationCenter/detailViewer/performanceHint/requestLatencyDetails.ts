import app = require("durandal/app");
import abstractNotification = require("common/notifications/models/abstractNotification");
import notificationCenter = require("common/notifications/notificationCenter");
import performanceHint = require("common/notifications/models/performanceHint");
import abstractPerformanceHintDetails = require("viewmodels/common/notificationCenter/detailViewer/performanceHint/abstractPerformanceHintDetails");
import virtualGridController = require("widgets/virtualGrid/virtualGridController");
import textColumn = require("widgets/virtualGrid/columns/textColumn");
import columnPreviewPlugin = require("widgets/virtualGrid/columnPreviewPlugin");

class requestLatencyDetails extends abstractPerformanceHintDetails {

    
    tableItems = [] as Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo[];
    private gridController = ko.observable<virtualGridController<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>>();
    private columnPreview = new columnPreviewPlugin<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>();

    constructor(hint: performanceHint, notificationCenter: notificationCenter) {
        super(hint, notificationCenter);

        this.tableItems = this.mapItems(hint.details() as Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyDetail);
    }

    compositionComplete() {
        super.compositionComplete();

        const grid = this.gridController();
        grid.headerVisible(true);

        grid.init((s, t) => this.fetcher(s, t), () => {
            return [
                new textColumn<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>(grid, x => x.Action, "Action", "20%"),
                new textColumn<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>(grid, x => x.Date, "Date", "15%"),
                new textColumn<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>(grid, x => x.Duration, "Duration (ms)", "15%"),
                new textColumn<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>(grid, x => x.QueryString, "Query string", "30%")
            ];
        });

        this.columnPreview.install(".requestLatencyDetails", ".request-latency-details-tooltip", (details: Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo, column: textColumn<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>, e: JQueryEventObject, onValue: (context: any) => void) => {
            const value = column.getCellValue(details);
            if (!_.isUndefined(value)) {
                const json = JSON.stringify(value, null, 4);
                const html = Prism.highlight(json, (Prism.languages as any).javascript);
                onValue(html);
            }
        });
    }

    private fetcher(skip: number, take: number): JQueryPromise<pagedResult<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>> {
        return $.Deferred<pagedResult<Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo>>()
            .resolve({
                items: this.tableItems,
                totalResultCount: this.tableItems.length
            });
    }

    private mapItems(details: Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyDetail): Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo[] {
        return _.flatMap(details.RequestLatencies, (value, key) => {
            return value.map((item: Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo) =>
                ({
                    Action: key,
                    Date: item.Date,
                    Duration: item.Duration,
                    QueryString: item.QueryString
                } as Raven.Server.NotificationCenter.Notifications.Details.RequestLatencyInfo));
        });
    }

    static supportsDetailsFor(notification: abstractNotification) {
        return (notification instanceof performanceHint) && notification.hintType() == "RequestLatency";
    }

    static showDetailsFor(hint: performanceHint, center: notificationCenter) {
        return app.showBootstrapDialog(new requestLatencyDetails(hint, center));
    }
}

export = requestLatencyDetails;
