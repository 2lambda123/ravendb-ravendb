/// <reference path="../../../../typings/tsd.d.ts" />

class timeSeriesValue {
    value = ko.observable<number>();
    validationGroup: KnockoutValidationGroup;
    
    constructor(value: number) {
        this.value(value);
        
        this.initValidation();
    }
    
    initValidation() {
        this.value.extend({
            required: true,
            numberOrNaN: true
        });
        
        this.validationGroup = ko.validatedObservable({
            value: this.value
        });
    }
    
}

export = timeSeriesValue;
