import commandBase = require("commands/commandBase");

//TODO: rename to lower case
class activateLicenseCommand extends commandBase {

    execute(): JQueryPromise<any> {
        return this.query("/admin/activate-hotspare", null, null, null);//TODO: use endpoints
    }
}

export = activateLicenseCommand; 
