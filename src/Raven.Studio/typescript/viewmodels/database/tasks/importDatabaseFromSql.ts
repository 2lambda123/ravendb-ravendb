import viewModelBase = require("viewmodels/viewModelBase");
import sqlMigration = require("models/database/tasks/sql/sqlMigration");
import fetchSqlDatabaseSchemaCommand = require("commands/database/tasks/fetchSqlDatabaseSchemaCommand");

//TODO: consider removing 'Please provide 'Database name' in field below, instead of using' - instead automatically extract this from connection string on blur
class importCollectionFromSql extends viewModelBase {
    
    spinners = {
        schema: ko.observable<boolean>(false)
    };
    
    model = new sqlMigration();
    
    databases = ko.observableArray<string>([]); //TODO: fetch this on databases focus

    validationGroup = ko.validatedObservable({
        //TODO
    });

    constructor() {
        super();

        this.setupValidation();
    }

    private setupValidation() {
        //TODO: validate connection string, database name etc.
    }
    
    nextStep() {
        //TODO: validate !
        const connectionString = this.model.getConnectionString(); 
        console.log("using connection string:" + connectionString);
        console.log("using driver = " + this.model.databaseType());
        
        this.spinners.schema(true);
        
        const schemaRequestDto = {
            Provider: this.model.databaseType(),
            ConnectionString: connectionString
        } as Raven.Server.SqlMigration.Model.SourceSqlDatabase;
        
        new fetchSqlDatabaseSchemaCommand(this.activeDatabase(), schemaRequestDto)
            .execute()
            .done(schema => {
                this.model.onSchemaUpdated(schema);
            })
            .always(() => this.spinners.schema(false));
            
        //TODO: finish
    }


}

export = importCollectionFromSql; 
