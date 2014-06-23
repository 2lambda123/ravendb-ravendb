import app = require("durandal/app");
import router = require("plugins/router");
import appUrl = require("common/appUrl");
import database = require("models/database");
import viewModelBase = require("viewmodels/viewModelBase");
import createDefaultSettingsCommand = require("commands/createDefaultSettingsCommand");
import createEncryptionConfirmation = require("viewmodels/createEncryptionConfirmation");
import changesApi = require('common/changesApi');
import shell = require('viewmodels/shell');
import databaseSettingsDialog = require("viewmodels/databaseSettingsDialog");

class databases extends viewModelBase {

    databases = ko.observableArray<database>();
    searchText = ko.observable("");
    selectedDatabase = ko.observable<database>();
    systemDb: database;
    docsForSystemUrl: string;

    constructor() {
        super();

        this.databases = shell.databases;
        this.systemDb = appUrl.getSystemDatabase();
        this.docsForSystemUrl = appUrl.forDocuments(null, this.systemDb);
        this.searchText.extend({ throttle: 200 }).subscribe(s => this.filterDatabases(s));
        ko.postbox.subscribe("ActivateDatabase", (db: database) => this.selectDatabase(db, false));
    }

    // Override canActivate: we can always load this page, regardless of any system db prompt.
    canActivate(args: any): any {
        return true;
    }

    attached() {
        ko.postbox.publish("SetRawJSONUrl", appUrl.forDatabasesRawData());
        this.databasesLoaded();
    }

    private databasesLoaded() {
        // If we have no databases (except system db), show the "create a new database" screen.
        if (this.databases().length === 1) {
            this.newDatabase();
        } else {
            // If we have just a few databases, grab the db stats for all of them.
            // (Otherwise, we'll grab them when we click them.)
            var few = 20;
            var enabledDatabases: database[] = this.databases().filter((db: database) => !db.disabled());
            if (enabledDatabases.length < few) {
                enabledDatabases.forEach(db => shell.fetchDbStats(db, true));
            }
        }
    }

    getDocumentsUrl(db: database) {
        return appUrl.forDocuments(null, db);
    }

    selectDatabase(db: database, activateDatabase: boolean = true) {
        this.databases().forEach((d: database)=> d.isSelected(d.name === db.name));
        if (activateDatabase) {
            db.activate();
        }
        this.selectedDatabase(db);
    }

    newDatabase() {
        // Why do an inline require here? Performance.
        // Since the database page is the common landing page, we want it to load quickly.
        // Since the createDatabase page isn't required up front, we pull it in on demand.
        require(["viewmodels/createDatabase"], createDatabase => {
            var createDatabaseViewModel = new createDatabase(this.databases);
            createDatabaseViewModel
                .creationTask
                .done((databaseName: string, bundles: string[], databasePath: string, databaseLogs: string, databaseIndexes: string) => {
                    var settings = {
                        "Raven/ActiveBundles": bundles.join(";")
                    };
                    settings["Raven/DataDir"] = (!this.isEmptyStringOrWhitespace(databasePath)) ? databasePath : "~/Databases/" + databaseName;
                    if (!this.isEmptyStringOrWhitespace(databaseLogs)) {
                        settings["Raven/Esent/LogsPath"] = databaseLogs;
                    }
                    if (!this.isEmptyStringOrWhitespace(databaseIndexes)) {
                        settings["Raven/IndexStoragePath"] = databaseIndexes;
                    }

                    this.showDbCreationAdvancedStepsIfNecessary(databaseName, bundles, settings);
                });
            app.showDialog(createDatabaseViewModel);
        });
    }

    showDbCreationAdvancedStepsIfNecessary(databaseName: string, bundles: string[], settings: {}) {
        var securedSettings = {};
        var savedKey;

        var encryptionDeferred = $.Deferred();

        if (bundles.contains("Encryption")) {
            require(["viewmodels/createEncryption"], createEncryption => {
                var createEncryptionViewModel = new createEncryption();
                createEncryptionViewModel
                    .creationEncryption
                    .done((key: string, encryptionAlgorithm: string, encryptionBits: string, isEncryptedIndexes: string) => {
                        savedKey = key;
                        securedSettings = {
                            'Raven/Encryption/Key': key,
                            'Raven/Encryption/Algorithm': this.getEncryptionAlgorithmFullName(encryptionAlgorithm),
                            'Raven/Encryption/KeyBitsPreference': encryptionBits,
                            'Raven/Encryption/EncryptIndexes': isEncryptedIndexes
                        };
                        encryptionDeferred.resolve(securedSettings);
                    });
                app.showDialog(createEncryptionViewModel);
            });
        } else {
            encryptionDeferred.resolve();
        }

        encryptionDeferred.done(() => {
            require(["commands/createDatabaseCommand"], createDatabaseCommand => {
                new createDatabaseCommand(databaseName, settings, securedSettings)
                    .execute()
                    .done(() => {
                        var newDatabase = this.addNewDatabase(databaseName);
                        this.selectDatabase(newDatabase);

                        var encryptionConfirmationDialogPromise = $.Deferred();
                        if (!jQuery.isEmptyObject(securedSettings)) {
                            require(["viewmodels/createEncryptionConfirmation"], createEncryptionConfirmation => {
                                var createEncryptionConfirmationViewModel = new createEncryptionConfirmation(savedKey);
                                createEncryptionConfirmationViewModel.dialogPromise.done(() => encryptionConfirmationDialogPromise.resolve());
                                createEncryptionConfirmationViewModel.dialogPromise.fail(() => encryptionConfirmationDialogPromise.reject());
                                app.showDialog(createEncryptionConfirmationViewModel);
                            });
                        } else {
                            encryptionConfirmationDialogPromise.resolve();
                        }

                        this.createDefaultSettings(newDatabase, bundles).always(() => {
                            if (bundles.contains("Quotas") || bundles.contains("Versioning")) {
                                encryptionConfirmationDialogPromise.always(() => {
                                    var settingsDialog = new databaseSettingsDialog(bundles);
                                    app.showDialog(settingsDialog);
                                });
                            }
                        });
                    });
            });
        });
    }

    private createDefaultSettings(db: database, bundles: Array<string>): JQueryPromise<any> {
        return new createDefaultSettingsCommand(db, bundles).execute();
    }

    private isEmptyStringOrWhitespace(str: string) {
        return !$.trim(str);
    }

    private getEncryptionAlgorithmFullName(encrytion: string) {
        var fullEncryptionName: string = null;
        switch (encrytion) {
            case "DES":
                fullEncryptionName = "System.Security.Cryptography.DESCryptoServiceProvider, mscorlib";
                break;
            case "R2C2":
                fullEncryptionName = "System.Security.Cryptography.RC2CryptoServiceProvider, mscorlib";
                break;
            case "Rijndael":
                fullEncryptionName = "System.Security.Cryptography.RijndaelManaged, mscorlib";
                break;
            default: //case "Triple DESC":
                fullEncryptionName = "System.Security.Cryptography.TripleDESCryptoServiceProvider, mscorlib";
        }
        return fullEncryptionName;
    }

    deleteSelectedDatabase() {
        var db = this.selectedDatabase();
        if (db) {
            require(["viewmodels/deleteDatabaseConfirm"], deleteDatabaseConfirm => {
                var confirmDeleteViewModel = new deleteDatabaseConfirm(db, this.systemDb);
                confirmDeleteViewModel.deleteTask.done(() => {
                    this.onDatabaseDeleted(db.name);
                });
                app.showDialog(confirmDeleteViewModel);
            });
        }
    }

    toggleSelectedDatabase() {
        var db = this.selectedDatabase();
        if (db) {
            var desiredAction = db.disabled() ? "enable" : "disable";
            var desiredActionCapitalized = desiredAction.charAt(0).toUpperCase() + desiredAction.slice(1);
            var action = !db.disabled();

            var confirmationMessageViewModel = this.confirmationMessage(desiredActionCapitalized + ' Database', 'Are you sure you want to ' + desiredAction + ' the database?');
            confirmationMessageViewModel
                .done(() => {
                    if (shell.currentDbChangesApi()) {
                        shell.currentDbChangesApi().dispose();
                    }
                    require(["commands/toggleDatabaseDisabledCommand"], toggleDatabaseDisabledCommand => {
                        new toggleDatabaseDisabledCommand(db)
                            .execute()
                            .done(() => {
                                db.isSelected(false);
                                db.disabled(action);
                                this.selectDatabase(db);
                            });
                    });
                });
        }
    }

    private addNewDatabase(databaseName: string): database {
        var databaseInArray = this.databases.first((db: database) => db.name == databaseName);

        if (!databaseInArray) {
            var newDatabase = new database(databaseName);
            this.databases.unshift(newDatabase);
            return newDatabase;
        }

        return databaseInArray;
    }

    private onDatabaseDeleted(databaseName: string) {
        var databaseInArray = this.databases.first((db: database) => db.name == databaseName);
        if (!!databaseInArray) {
            this.databases.remove(databaseInArray);

            if ((this.databases().length > 0) && (this.databases.contains(this.selectedDatabase()) === false)) {
                this.selectDatabase(this.databases().first());
            }
        }
    }

    private filterDatabases(filter: string) {
        var filterLower = filter.toLowerCase();
        this.databases().forEach(d=> {
            var isMatch = !filter || (d.name.toLowerCase().indexOf(filterLower) >= 0);
            d.isVisible(isMatch);
        });

        var selectedDatabase = this.selectedDatabase();
        if (selectedDatabase && !selectedDatabase.isVisible()) {
            selectedDatabase.isSelected(false);
            this.selectedDatabase(null);
        }
    }
}

export = databases;