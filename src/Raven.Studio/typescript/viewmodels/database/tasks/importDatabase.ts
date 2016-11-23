import aceEditorBindingHandler = require("common/bindingHelpers/aceEditorBindingHandler");
import viewModelBase = require("viewmodels/viewModelBase");
import database = require("models/resources/database");
import messagePublisher = require("common/messagePublisher");
import importDatabaseCommand = require("commands/database/studio/importDatabaseCommand");
import importDatabaseModel = require("models/database/tasks/importDatabaseModel");
import notificationCenter = require("common/notifications/notificationCenter");
import eventsCollector = require("common/eventsCollector");

class importDatabase extends viewModelBase {

    private static readonly filePickerTag = "#importDatabaseFilePicker";

    model = new importDatabaseModel();

    static isImporting = ko.observable(false);
    isImporting = importDatabase.isImporting;

    showAdvancedOptions = ko.observable(false);
    showTransformScript = ko.observable(false);

    hasFileSelected = ko.observable(false);
    importedFileName = ko.observable<string>();

    isUploading = ko.observable<boolean>(false);
    uploadStatus = ko.observable<number>();

    importCommand: KnockoutComputed<string>;

    constructor() {
        super();
        aceEditorBindingHandler.install();
        this.isUploading.subscribe(v => {
            if (!v) {
                this.uploadStatus(0);
            }
        });

        this.importCommand = ko.pureComputed(() => 'TODO impl actual command generation');
    }

    attached() {
        super.attached();
        $("#transformScriptPopover").popover({
            html: true,
            trigger: "hover",
            content: "Transform scripts are written in JavaScript. <br /><br/>Example:<pre><span class=\"code-keyword\">function</span>(doc) {<br/>&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;<span class=\"code-keyword\">var</span> id = doc['@metadata']['@id'];<br />&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;<span class=\"code-keyword\">if</span> (id === 'orders/999')<br />&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;<span class=\"code-keyword\">return null</span>;<br /><br/>&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;<span class=\"code-keyword\">return</span> doc;<br />}</pre>"
        });
        this.updateHelpLink("YD9M1R");
    }

    canDeactivate(isClose: boolean) {
        super.canDeactivate(isClose);

        if (this.isUploading()) {
            this.confirmationMessage("Upload is in progress", "Please wait until uploading is complete.", ["OK"]);
            return false;
        }

        return true;
    }

    createPostboxSubscriptions(): Array<KnockoutSubscription> {
        return [
            ko.postbox.subscribe("UploadProgress", (percentComplete: number) => {
                var db = this.activeDatabase();
                if (!db) {
                    return;
                }

                if (!this.isUploading()) {
                    return;
                }

                this.uploadStatus(percentComplete);
            }),
            ko.postbox.subscribe("ChangesApiReconnected", (db: database) => {
                this.isUploading(false);
            })
        ];
    }

    fileSelected(fileName: string) {
        const isFileSelected = fileName ? !!fileName.trim() : false;
        const importFileName = $(importDatabase.filePickerTag).val().split(/(\\|\/)/g).pop();
		this.hasFileSelected(isFileSelected);
		this.importedFileName(importFileName ? importFileName : null);
    }

    importDb() {
        eventsCollector.default.reportEvent("database", "import");
        this.isUploading(true);
        const formData = new FormData();
        const fileInput = document.querySelector(importDatabase.filePickerTag) as HTMLInputElement;
        formData.append("file", fileInput.files[0]);

        new importDatabaseCommand(formData, this.model, this.activeDatabase())
            .execute()
            .done((result: operationIdDto) => {
                const operationId = result.OperationId;
                notificationCenter.instance.monitorOperation(this.activeDatabase(), operationId);
            })
            .always(() => this.isUploading(false));
    }

    copyCommandToClipboard() {
        copyToClipboard.copy(this.importCommand(), "Command was copied to clipboard.");
    }

}

export = importDatabase; 
