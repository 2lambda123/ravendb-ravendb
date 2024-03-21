import commandBase = require("commands/commandBase");
import database = require("models/resources/database");
import endpoints = require("endpoints");

class getOngoingTasksCommand extends commandBase {

    private readonly db: database | string;

    private readonly location: databaseLocationSpecifier;

    constructor(db: database | string, location: databaseLocationSpecifier) {
        super();
        this.location = location;
        this.db = db;
    }

    execute(): JQueryPromise<Raven.Server.Web.System.OngoingTasksResult> {
        const url = endpoints.databases.ongoingTasks.tasks;
        
        const args = {
            ...this.location
        };

        return this.query<Raven.Server.Web.System.OngoingTasksResult>(url, args, this.db, result => {
            // since in 6.0 property OngoingTasksList was renamed to OngoingTasks
            // we unify format here to support mixed clusters
            if ("OngoingTasksList" in result) {
                result.OngoingTasks = result.OngoingTasksList;
            }
            
            return result;
        });
    }
}

export = getOngoingTasksCommand;
