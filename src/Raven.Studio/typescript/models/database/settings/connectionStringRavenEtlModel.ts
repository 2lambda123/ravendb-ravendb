﻿/// <reference path="../../../../typings/tsd.d.ts"/>

class connectionStringRavenEtlModel { 

    connectionStringName = ko.observable<string>(); 
    url = ko.observable<string>();                 
    database = ko.observable<string>();            

    validationGroup: KnockoutValidationGroup;

    constructor(dto: Raven.Client.ServerWide.ETL.RavenConnectionString) {
        this.update(dto);
        this.initValidation();
    }    

    update(dto: Raven.Client.ServerWide.ETL.RavenConnectionString) {
        this.connectionStringName(dto.Name); 
        this.database(dto.Database);
        this.url(dto.Url);
    }

    initValidation() {
        this.connectionStringName.extend({
            required: true
        });

        this.database.extend({
            required: true,
            maxLength: 230,
            validDatabaseName: true            
        })

        this.url.extend({
            required: true,
            validUrl: true
        });

        this.validationGroup = ko.validatedObservable({
            connectionStringName: this.connectionStringName,
            database: this.database,
            url: this.url
        });
    }

    static empty(): connectionStringRavenEtlModel {
        return new connectionStringRavenEtlModel({
            Type: "Raven",
            Name: "", 
            Url: "",
            Database: ""
        } as Raven.Client.ServerWide.ETL.RavenConnectionString);
    }
}

export = connectionStringRavenEtlModel;
