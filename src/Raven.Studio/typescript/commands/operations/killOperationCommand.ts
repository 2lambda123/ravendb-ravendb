import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class killOperationCommand extends commandBase {

    private db: database | string;

    private taskId: number;

    constructor(db: database | string, taskId: number) {
        super();
        this.taskId = taskId;
        this.db = db;
    }

    execute(): JQueryPromise<void> {
        const args = {
            id: this.taskId
        }
        const url = this.db ? endpoints.databases.operations.operationsKill :
            endpoints.global.operationsServer.adminOperationsKill;

        return this.post(url + this.urlEncodeArgs(args), null, this.db, { dataType: undefined });
    }
}

export = killOperationCommand;
