/// <reference path="../../Scripts/typings/nprogress/nprogress.d.ts" />
/// <reference path="../../Scripts/typings/bootstrap/bootstrap.d.ts" />
define(["require", "exports", "plugins/router", "durandal/app", "durandal/system", "models/database", "common/raven", "models/document", "common/appUrl", "models/collection", "common/dialogResult", "common/alertArgs", "common/alertType", "common/pagedList", "commands/getDatabaseStatsCommand", "commands/getDatabasesCommand", "commands/getBuildVersionCommand", "commands/getLicenseStatusCommand"], function(require, exports, __router__, __app__, __sys__, __database__, __raven__, __document__, __appUrl__, __collection__, __dialogResult__, __alertArgs__, __alertType__, __pagedList__, __getDatabaseStatsCommand__, __getDatabasesCommand__, __getBuildVersionCommand__, __getLicenseStatusCommand__) {
    var router = __router__;
    var app = __app__;
    var sys = __sys__;

    var database = __database__;
    var raven = __raven__;
    var document = __document__;
    var appUrl = __appUrl__;
    var collection = __collection__;
    
    var dialogResult = __dialogResult__;
    var alertArgs = __alertArgs__;
    var alertType = __alertType__;
    var pagedList = __pagedList__;
    var getDatabaseStatsCommand = __getDatabaseStatsCommand__;
    var getDatabasesCommand = __getDatabasesCommand__;
    var getBuildVersionCommand = __getBuildVersionCommand__;
    var getLicenseStatusCommand = __getLicenseStatusCommand__;

    var shell = (function () {
        function shell() {
            var _this = this;
            this.router = router;
            this.databases = ko.observableArray();
            this.activeDatabase = ko.observable().subscribeTo("ActivateDatabase");
            this.currentAlert = ko.observable();
            this.queuedAlerts = ko.observableArray();
            this.buildVersion = ko.observable();
            this.licenseStatus = ko.observable();
            ko.postbox.subscribe("Alert", function (alert) {
                return _this.showAlert(alert);
            });
            ko.postbox.subscribe("ActivateDatabaseWithName", function (databaseName) {
                return _this.activateDatabaseWithName(databaseName);
            });
            ko.postbox.subscribe("ActivateDatabase", function (db) {
                return _this.databaseChanged(db);
            });

            //ko.postbox.subscribe("EditDocument", args => this.launchDocEditor(args.doc.getId(), args.docsList));
            NProgress.set(.5);
        }
        shell.prototype.activate = function () {
            NProgress.set(.8);

            router.map([
                { route: ['', 'databases'], title: 'Databases', moduleId: 'viewmodels/databases', nav: false },
                { route: 'documents', title: 'Documents', moduleId: 'viewmodels/documents', nav: true, hash: appUrl.forCurrentDatabase().documents },
                { route: 'indexes', title: 'Indexes', moduleId: 'viewmodels/indexes', nav: true },
                { route: 'query', title: 'Query', moduleId: 'viewmodels/query', nav: true },
                { route: 'tasks', title: 'Tasks', moduleId: 'viewmodels/tasks', nav: true },
                { route: 'settings*details', title: 'Settings', moduleId: 'viewmodels/settings', nav: true, hash: appUrl.forCurrentDatabase().settings },
                { route: 'status*details', title: 'Status', moduleId: 'viewmodels/status', nav: true, hash: appUrl.forCurrentDatabase().status },
                { route: 'edit', title: 'Edit Document', moduleId: 'viewmodels/editDocument', nav: false }
            ]).buildNavigationModel();

            router.isNavigating.subscribe(function (isNavigating) {
                if (isNavigating)
                    NProgress.start();
else
                    NProgress.done();
            });

            this.connectToRavenServer();
        };

        // The view must be attached to the DOM before we can hook up keyboard shortcuts.
        shell.prototype.attached = function () {
            var _this = this;
            NProgress.remove();
            jwerty.key("ctrl+alt+n", function (e) {
                e.preventDefault();
                _this.newDocument();
            });
        };

        shell.prototype.databasesLoaded = function (databases) {
            var systemDatabase = new database("<system>");
            systemDatabase.isSystem = true;
            this.databases(databases.concat([systemDatabase]));
            this.databases()[0].activate();
        };

        shell.prototype.launchDocEditor = function (docId, docsList) {
            var editDocUrl = appUrl.forEditDoc(docId, docsList ? docsList.collectionName : null, docsList ? docsList.currentItemIndex() : null);
            router.navigate(editDocUrl);
        };

        shell.prototype.connectToRavenServer = function () {
            var _this = this;
            this.databasesLoadedTask = new getDatabasesCommand().execute().fail(function (result) {
                return _this.handleRavenConnectionFailure(result);
            }).done(function (results) {
                _this.databasesLoaded(results);
                router.activate();
                _this.fetchBuildVersion();
                _this.fetchLicenseStatus();
            });
        };

        shell.prototype.handleRavenConnectionFailure = function (result) {
            var _this = this;
            NProgress.done();
            sys.log("Unable to connect to Raven.", result);
            var tryAgain = 'Try again';
            var messageBoxResultPromise = app.showMessage("Couldn't connect to Raven. Details in the browser console.", ":-(", [tryAgain]);
            messageBoxResultPromise.done(function (messageBoxResult) {
                if (messageBoxResult === tryAgain) {
                    NProgress.start();
                    _this.connectToRavenServer();
                }
            });
        };

        shell.prototype.showAlert = function (alert) {
            var _this = this;
            var currentAlert = this.currentAlert();
            if (currentAlert) {
                // Maintain a 500ms time between alerts; otherwise successive alerts can fly by too quickly.
                this.queuedAlerts.push(alert);
                if (currentAlert.type !== alertType.danger) {
                    setTimeout(function () {
                        return _this.closeAlertAndShowNext(_this.currentAlert());
                    }, 500);
                }
            } else {
                this.currentAlert(alert);
                var fadeTime = 3000;
                if (alert.type === alertType.danger || alert.type === alertType.warning) {
                    fadeTime = 5000;
                }
                setTimeout(function () {
                    return _this.closeAlertAndShowNext(alert);
                }, fadeTime);
            }
        };

        shell.prototype.closeAlertAndShowNext = function (alertToClose) {
            var _this = this;
            $('#' + alertToClose.id).alert('close');
            var nextAlert = this.queuedAlerts.pop();
            setTimeout(function () {
                return _this.currentAlert(nextAlert);
            }, 500);
        };

        shell.prototype.newDocument = function () {
            this.launchDocEditor(null);
        };

        shell.prototype.activateDatabaseWithName = function (databaseName) {
            var _this = this;
            if (this.databasesLoadedTask) {
                this.databasesLoadedTask.done(function () {
                    var matchingDatabase = _this.databases().first(function (d) {
                        return d.name == databaseName;
                    });
                    if (matchingDatabase && _this.activeDatabase() !== matchingDatabase) {
                        ko.postbox.publish("ActivateDatabase", matchingDatabase);
                    }
                });
            }
        };

        shell.prototype.databaseChanged = function (db) {
            if (db) {
                new getDatabaseStatsCommand(db).execute().done(function (result) {
                    return db.statistics(result);
                });
            }
        };

        shell.prototype.fetchBuildVersion = function () {
            var _this = this;
            new getBuildVersionCommand().execute().done(function (result) {
                return _this.buildVersion(result);
            });
        };

        shell.prototype.fetchLicenseStatus = function () {
            var _this = this;
            new getLicenseStatusCommand().execute().done(function (result) {
                return _this.licenseStatus(result);
            });
        };
        return shell;
    })();

    
    return shell;
});
//# sourceMappingURL=shell.js.map
