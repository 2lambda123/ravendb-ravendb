import confirmViewModelBase = require("viewmodels/confirmViewModelBase");
import resource = require("models/resources/resource");

class disableResourceToggleConfirm extends confirmViewModelBase<confirmDialogResult> {

    desiredAction = ko.observable<string>();
    deletionText: string;
    confirmDeletionText: string;

    constructor(private resources: Array<resource>, private disable: boolean) {
        super(null);

        this.deletionText = disable ? "You're disabling" : "You're enabling";
        this.confirmDeletionText = disable ? "Disable" : "Enable";
    }
}

export = disableResourceToggleConfirm;
