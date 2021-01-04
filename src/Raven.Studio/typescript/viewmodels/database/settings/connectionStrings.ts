import viewModelBase = require("viewmodels/viewModelBase");
import connectionStringRavenEtlModel = require("models/database/settings/connectionStringRavenEtlModel");
import connectionStringSqlEtlModel = require("models/database/settings/connectionStringSqlEtlModel");
import saveConnectionStringCommand = require("commands/database/settings/saveConnectionStringCommand");
import getConnectionStringsCommand = require("commands/database/settings/getConnectionStringsCommand");
import getConnectionStringInfoCommand = require("commands/database/settings/getConnectionStringInfoCommand");
import deleteConnectionStringCommand = require("commands/database/settings/deleteConnectionStringCommand");
import ongoingTasksCommand = require("commands/database/tasks/getOngoingTasksCommand");
import discoveryUrl = require("models/database/settings/discoveryUrl");
import eventsCollector = require("common/eventsCollector");
import generalUtils = require("common/generalUtils");
import appUrl = require("common/appUrl");

class connectionStrings extends viewModelBase {

    ravenEtlConnectionStringsNames = ko.observableArray<string>([]);
    sqlEtlConnectionStringsNames = ko.observableArray<string>([]);

    // Mapping from { connection string } to { taskId, taskName, taskType }
    connectionStringsTasksInfo: dictionary<Array<{ TaskId: number, TaskName: string, TaskType: Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskType }>> = {}; 
    
    editedRavenEtlConnectionString = ko.observable<connectionStringRavenEtlModel>(null);
    editedSqlEtlConnectionString = ko.observable<connectionStringSqlEtlModel>(null);

    testConnectionResult = ko.observable<Raven.Server.Web.System.NodeConnectionTestResult>();
    testConnectionHttpSuccess: KnockoutComputed<boolean>;
    spinners = { 
        test: ko.observable<boolean>(false)
    };
    fullErrorDetailsVisible = ko.observable<boolean>(false);

    shortErrorText: KnockoutObservable<string>;

    constructor() {
        super();

        this.initObservables();
        this.bindToCurrentInstance("onEditSqlEtl", "onEditRavenEtl", "confirmDelete", "isConnectionStringInUse",
                                   "onTestConnectionRaven", "removeDiscoveryUrl");
        
        const currentlyEditedObjectIsDirty = ko.pureComputed(() => {
            const ravenEtl = this.editedRavenEtlConnectionString();
            if (ravenEtl) {
                return ravenEtl.dirtyFlag().isDirty();
            }
            
            const sqlEtl = this.editedSqlEtlConnectionString();
            if (sqlEtl) {
                return sqlEtl.dirtyFlag().isDirty();
            }
            
            return false;
        });
        
        this.dirtyFlag = new ko.DirtyFlag([currentlyEditedObjectIsDirty], false);
    }
    
    private initObservables() {
        this.shortErrorText = ko.pureComputed(() => {
            const result = this.testConnectionResult();
            if (!result || result.Success) {
                return "";
            }
            return generalUtils.trimMessage(result.Error);
        });
        
        this.testConnectionHttpSuccess = ko.pureComputed(() => {
            const testResult = this.testConnectionResult();
            
            if (!testResult) {
                return false;
            }
            
            return testResult.HTTPSuccess || false;
        })
    }

    activate(args: any) {
        super.activate(args);
        
        return $.when<any>(this.getAllConnectionStrings(), this.fetchOngoingTasks())
                .done(()=>{
                    if (args.name) {
                        if (args.type === "sql") {
                            this.onEditSqlEtl(args.name);
                        } else {
                            this.onEditRavenEtl(args.name);
                        }
                    }
                });
    }

    compositionComplete() {
        super.compositionComplete();
        this.setupDisableReasons();
    }
    
    private clearTestResult() {
        this.testConnectionResult(null);
    }

    private fetchOngoingTasks(): JQueryPromise<Raven.Server.Web.System.OngoingTasksResult> {
        const db = this.activeDatabase();
        return new ongoingTasksCommand(db)
            .execute()
            .done((info) => {
                this.processData(info);
            });
    }
    
    private processData(result: Raven.Server.Web.System.OngoingTasksResult) {
        const tasksThatUseConnectionStrings = result.OngoingTasksList.filter((task) => 
                                                                              task.TaskType === "RavenEtl"    ||
                                                                              task.TaskType === "SqlEtl"      ||
                                                                              task.TaskType === "Replication" ||
                                                                              task.TaskType === "PullReplicationAsSink");
        for (let i = 0; i < tasksThatUseConnectionStrings.length; i++) {
            const task = tasksThatUseConnectionStrings[i];
            
            let taskData = { TaskId: task.TaskId,
                             TaskName: task.TaskName,
                             TaskType: task.TaskType };
            let stringName: string;
            
            switch (task.TaskType) {
                case "RavenEtl":
                    stringName = (task as Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskRavenEtlListView).ConnectionStringName;
                    break;
                case "SqlEtl":
                    stringName = (task as Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskSqlEtlListView).ConnectionStringName;
                    break;
                case "Replication":
                    stringName = (task as Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskReplication).ConnectionStringName;
                    break;
                case "PullReplicationAsSink":
                    stringName = (task as Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskPullReplicationAsSink).ConnectionStringName;
                    break;
            }

            if (this.connectionStringsTasksInfo[stringName]) {
                this.connectionStringsTasksInfo[stringName].push(taskData);
            } else {
                this.connectionStringsTasksInfo[stringName] = [taskData];
            }
        }
    }

    isConnectionStringInUse(connectionStringName: string, connectionStringType: Raven.Client.Documents.Operations.ConnectionStrings.ConnectionStringType): boolean {
        const possibleTasksTypes = this.getTasksTypes(connectionStringType);
        const tasksUsingConnectionString = this.connectionStringsTasksInfo[connectionStringName];
        
        const isInUse = _.includes(Object.keys(this.connectionStringsTasksInfo), connectionStringName);
        return isInUse && !!tasksUsingConnectionString.find(x => _.includes(possibleTasksTypes, x.TaskType));
    }
    
    private getAllConnectionStrings() {
        return new getConnectionStringsCommand(this.activeDatabase())
            .execute()
            .done((result: Raven.Client.Documents.Operations.ConnectionStrings.GetConnectionStringsResult) => {
                // ravenEtl
                this.ravenEtlConnectionStringsNames(Object.keys(result.RavenConnectionStrings));
                const groupedRavenEtlNames = _.groupBy(this.ravenEtlConnectionStringsNames(), x => this.hasServerWidePrefix(x));
                const serverWideNames = _.sortBy(groupedRavenEtlNames.true, x => x.toUpperCase());
                const regularNames = _.sortBy(groupedRavenEtlNames.false, x => x.toUpperCase());
                this.ravenEtlConnectionStringsNames([...regularNames, ...serverWideNames]);
                
                // sqlEtl
                this.sqlEtlConnectionStringsNames(Object.keys(result.SqlConnectionStrings));
                this.sqlEtlConnectionStringsNames(_.sortBy(this.sqlEtlConnectionStringsNames(), x => x.toUpperCase()));
            });
    }

    confirmDelete(connectionStringName: string, connectionStringtype: Raven.Client.Documents.Operations.ConnectionStrings.ConnectionStringType) {
        const stringType = connectionStringtype === "Raven" ? "RavenDB" : "SQL";
        this.confirmationMessage("Are you sure?",
            `Do you want to delete ${stringType} ETL connection string: <br><strong>${generalUtils.escapeHtml(connectionStringName)}</strong>`, {
            buttons: ["Cancel", "Delete"],
            html: true
        })
            .done(result => {
                if (result.can) {
                    this.deleteConnectionSring(connectionStringtype, connectionStringName);
                }
        });
    }

    private deleteConnectionSring(connectionStringType: Raven.Client.Documents.Operations.ConnectionStrings.ConnectionStringType, connectionStringName: string) {
        new deleteConnectionStringCommand(this.activeDatabase(), connectionStringType, connectionStringName)
            .execute()
            .done(() => {
                this.getAllConnectionStrings();
                this.onCloseEdit();
            });
    }
    
    onAddRavenEtl() {
        eventsCollector.default.reportEvent("connection-strings", "add-raven-etl");
        this.editedRavenEtlConnectionString(connectionStringRavenEtlModel.empty());
        this.editedRavenEtlConnectionString().inputUrl().discoveryUrlName.subscribe(() => this.clearTestResult());

        this.editedSqlEtlConnectionString(null);
        this.clearTestResult();
    }

    onAddSqlEtl() {
        eventsCollector.default.reportEvent("connection-strings", "add-sql-etl");
        this.editedSqlEtlConnectionString(connectionStringSqlEtlModel.empty());
        this.editedSqlEtlConnectionString().connectionString.subscribe(() => this.clearTestResult());

        this.editedRavenEtlConnectionString(null);
        this.clearTestResult();
    }

    onEditRavenEtl(connectionStringName: string) {
        this.clearTestResult();
        
        return getConnectionStringInfoCommand.forRavenEtl(this.activeDatabase(), connectionStringName)
            .execute()
            .done((result: Raven.Client.Documents.Operations.ConnectionStrings.GetConnectionStringsResult) => {
                this.editedRavenEtlConnectionString(new connectionStringRavenEtlModel(result.RavenConnectionStrings[connectionStringName], false, this.getTasksThatUseThisString(connectionStringName, "Raven")));
                this.editedRavenEtlConnectionString().inputUrl().discoveryUrlName.subscribe(() => this.clearTestResult());
                this.editedSqlEtlConnectionString(null);
            });
    }

    onEditSqlEtl(connectionStringName: string) {
        this.clearTestResult();
        
        return getConnectionStringInfoCommand.forSqlEtl(this.activeDatabase(), connectionStringName)
            .execute()
            .done((result: Raven.Client.Documents.Operations.ConnectionStrings.GetConnectionStringsResult) => {
                this.editedSqlEtlConnectionString(new connectionStringSqlEtlModel(result.SqlConnectionStrings[connectionStringName], false, this.getTasksThatUseThisString(connectionStringName, "Sql")));
                this.editedSqlEtlConnectionString().connectionString.subscribe(() => this.clearTestResult());
                this.editedRavenEtlConnectionString(null);
            });
    }
    
    private getTasksThatUseThisString(connectionStringName: string, connectionStringType: Raven.Client.Documents.Operations.ConnectionStrings.ConnectionStringType): { taskName: string; taskId: number }[] {
        const tasksUsingConnectionString = this.connectionStringsTasksInfo[connectionStringName];
        
        if (!tasksUsingConnectionString) {
            return [];
        } else {
            const possibleTasksTypes = this.getTasksTypes(connectionStringType);
            const tasks = tasksUsingConnectionString.filter(x => _.includes(possibleTasksTypes, x.TaskType));
            
            const tasksData = tasks.map((task) => { return { taskName: task.TaskName, taskId: task.TaskId }; });
            return tasksData ? _.sortBy(tasksData, x => x.taskName.toUpperCase()) : [];
        }
    }
    
    private getTasksTypes(connectionType: Raven.Client.Documents.Operations.ConnectionStrings.ConnectionStringType): Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskType[] {
        if (connectionType === "Sql") {
            return ["SqlEtl"]
        }
        
        return ["RavenEtl", "Replication", "PullReplicationAsSink"];
    }

    onTestConnectionSql() {
        this.clearTestResult();
        const sqlConnectionString = this.editedSqlEtlConnectionString();

        if (sqlConnectionString) {
            if (this.isValid(sqlConnectionString.testConnectionValidationGroup)) {
                eventsCollector.default.reportEvent("ravenDB-SQL-connection-string", "test-connection");

                this.spinners.test(true);
                sqlConnectionString.testConnection(this.activeDatabase())
                    .done((testResult) => this.testConnectionResult(testResult))
                    .always(() => {
                        this.spinners.test(false);
                    });
            }
        }
    }
    
    onTestConnectionRaven(urlToTest: string) {
        this.clearTestResult();
        const ravenConnectionString = this.editedRavenEtlConnectionString();
        eventsCollector.default.reportEvent("ravenDB-ETL-connection-string", "test-connection");
        
        this.spinners.test(true);
        ravenConnectionString.selectedUrlToTest(urlToTest);

        ravenConnectionString.testConnection(urlToTest)
            .done(result => {
                this.testConnectionResult(result);
                if (result.Error) {
                    const url = ravenConnectionString.topologyDiscoveryUrls().find(x => x.discoveryUrlName() === urlToTest);
                    url.hasTestError(true);
                }
            })
            .always(() => { 
                this.spinners.test(false);
                this.fullErrorDetailsVisible(false);
            });
    }

    removeDiscoveryUrl(url: discoveryUrl) {
        const ravenConnectionString = this.editedRavenEtlConnectionString();
        if (url.discoveryUrlName() === ravenConnectionString.selectedUrlToTest() && url.hasTestError()) {
            this.clearTestResult();
        }

        ravenConnectionString.removeDiscoveryUrl(url);
    }
    
    onCloseEdit() {
        this.editedRavenEtlConnectionString(null);
        this.editedSqlEtlConnectionString(null);
    }

    onSave() {
        let model: connectionStringRavenEtlModel | connectionStringSqlEtlModel;

        // 1. Validate model
        if (this.editedRavenEtlConnectionString()) {
            let isValid = true;
            
            const discoveryUrl = this.editedRavenEtlConnectionString().inputUrl().discoveryUrlName;
            if (discoveryUrl()) {
                if (discoveryUrl.isValid()) {
                    // user probably forgot to click on 'Add Url' button 
                    this.editedRavenEtlConnectionString().addDiscoveryUrlWithBlink();
                } else {
                    isValid = false;
                }
            }
            
            if (!this.isValid(this.editedRavenEtlConnectionString().validationGroup)) { 
                isValid = false;
            }
            
            if (!isValid) {
                return;
            }
            
            model = this.editedRavenEtlConnectionString();
        } else {
            if (!this.isValid(this.editedSqlEtlConnectionString().validationGroup)) {
                return;
            }
            model = this.editedSqlEtlConnectionString();
        }

        // 2. Create/add the new connection string
        new saveConnectionStringCommand(this.activeDatabase(), model)
            .execute()
            .done(() => {
                // 3. Refresh list view....
                this.getAllConnectionStrings();

                this.editedRavenEtlConnectionString(null);
                this.editedSqlEtlConnectionString(null);

                this.dirtyFlag().reset();
            });
    }

    taskEditLink(taskId: number, connectionStringName: string) : string {
        const task = _.find(this.connectionStringsTasksInfo[connectionStringName], task => task.TaskId === taskId);
        const urls = appUrl.forCurrentDatabase();

        switch (task.TaskType) {
            case "SqlEtl":
                return urls.editSqlEtl(task.TaskId)();
            case "RavenEtl": 
                return urls.editRavenEtl(task.TaskId)();
            case "Replication":
               return urls.editExternalReplication(task.TaskId)();
        }
    }
    
    isServerWide(name: string) {
        return ko.pureComputed(() => {
            return this.hasServerWidePrefix(name);
        })
    } 
    
    private hasServerWidePrefix(name: string) {
        return name.startsWith(connectionStringRavenEtlModel.serverWidePrefix);
    }
}

export = connectionStrings
