define(["require", "exports", "durandal/app", "plugins/router", "common/appUrl", "common/raven", "models/database", "viewmodels/createDatabase"], function(require, exports, app, router, appUrl, raven, database, createDatabase) {
    var databases = (function () {
        function databases() {
            this.databases = ko.observableArray();
            this.ravenDb = new raven();
        }
        databases.prototype.activate = function (navigationArgs) {
            var _this = this;
            this.ravenDb.databases().done(function (results) {
                return _this.databasesLoaded(results);
            });
        };

        databases.prototype.navigateToDocuments = function (db) {
            db.activate();
            router.navigate("#documents?db=" + encodeURIComponent(db.name));
        };

        databases.prototype.getDocumentsUrl = function (db) {
            return appUrl.forDocuments(null, db);
        };

        databases.prototype.databasesLoaded = function (results) {
            var _this = this;
            this.databases(results);

            // If we have just a few databases, grab the db stats for all of them.
            // (Otherwise, we'll grab them when we click them.)
            var few = 20;
            if (results.length < 20) {
                results.forEach(function (db) {
                    return _this.fetchStats(db);
                });
            }
        };

        databases.prototype.newDatabase = function () {
            var _this = this;
            var createDatabaseViewModel = new createDatabase();
            createDatabaseViewModel.creationTask.done(function (databaseName) {
                return _this.databases.push(new database(databaseName));
            });
            app.showDialog(createDatabaseViewModel);
        };

        databases.prototype.fetchStats = function (db) {
            return this.ravenDb.databaseStats(db.name).done(function (result) {
                return db.statistics(result);
            });
        };

        databases.prototype.selectDatabase = function (db) {
            this.databases().forEach(function (d) {
                return d.isSelected(d === db);
            });
            db.activate();
        };

        databases.prototype.goToDocuments = function (db) {
            router.navigate("#documents?database=" + db.name);
        };
        return databases;
    })();

    
    return databases;
});
//# sourceMappingURL=databases.js.map
