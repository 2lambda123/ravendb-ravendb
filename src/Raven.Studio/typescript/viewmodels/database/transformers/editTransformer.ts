import viewModelBase = require("viewmodels/viewModelBase");
import transformer = require("models/database/index/transformer");
import saveTransformerCommand = require("commands/database/transformers/saveTransformerCommand");
import getSingleTransformerCommand = require("commands/database/transformers/getSingleTransformerCommand");
import aceEditorBindingHandler = require("common/bindingHelpers/aceEditorBindingHandler");
import deleteTransformerConfirm = require("viewmodels/database/transformers/deleteTransformerConfirm");
import saveTransformerWithNewNameConfirm = require("viewmodels/database/transformers/saveTransformerWithNewNameConfirm");
import dialog = require("plugins/dialog");
import appUrl = require("common/appUrl");
import jsonUtil = require("common/jsonUtil");
import router = require("plugins/router");
import messagePublisher = require("common/messagePublisher");
import formatIndexCommand = require("commands/database/index/formatIndexCommand");
import eventsCollector = require("common/eventsCollector");

class editTransformer extends viewModelBase {
    editedTransformer = ko.observable<transformer>();
    isEditingExistingTransformer = ko.observable(false);
    popoverOptions = ko.observable<any>();
    static containerSelector = "#editTransformerContainer";
    editorCollection = ko.observableArray<{ alias: string; controller: HTMLElement }>();
    appUrls: computedAppUrls;
    transformerName: KnockoutComputed<string>;
    isSaveEnabled: KnockoutComputed<boolean>;
    loadedTransformerName = ko.observable<string>();
    isSaving = ko.observable<boolean>(false);

    globalValidationGroup: KnockoutValidationGroup;

    constructor() {
        super();

        aceEditorBindingHandler.install();
        this.appUrls = appUrl.forCurrentDatabase();
    }

    canActivate(transformerToEditName: string): JQueryPromise<canActivateResultDto> {
        if (transformerToEditName) {
            var canActivateResult = $.Deferred<canActivateResultDto>();
            this.editExistingTransformer(transformerToEditName)
                .done(() => canActivateResult.resolve({ can: true }))
                .fail(() => {
                    messagePublisher.reportError("Could not find " + transformerToEditName + " transformer");
                    canActivateResult.resolve({ redirect: appUrl.forTransformers(this.activeDatabase()) });
                });

            return canActivateResult;
        } else {
            return $.Deferred().resolve({ can: true });
        }
    }

    activate(transformerToEditName: string) {
        super.activate(transformerToEditName);
        this.updateHelpLink('S467UO');
        if (transformerToEditName) {
            this.isEditingExistingTransformer(true);
        } else {
            this.editedTransformer(transformer.empty());
        }

        this.initializeObservables();
        this.initValidation();
    }

    attached() {
        super.attached();
        this.addTransformerHelpPopover();
        this.createKeyboardShortcut("alt+c", this.focusOnEditor, editTransformer.containerSelector);
        this.createKeyboardShortcut("alt+shift+del", this.deleteTransformer, editTransformer.containerSelector);
    }

    addTransformerHelpPopover() {
        $("#transformerResultsLabel").popover({
            html: true,
            trigger: "hover",
            content: 'The Transform function allows you to change the shape of individual result documents before the server returns them. It uses C# LINQ query syntax <br/> <br/> Example: <pre> <br/> <span class="code-keyword">from</span> result <span class="code-keyword">in</span> results <br/> <span class="code-keyword">let</span> category = LoadDocument(result.Category) <br/> <span class="code-keyword">select new</span> { <br/>    result.Name, <br/>    result.PricePerUnit, <br/>    Category = category.Name, <br/>    CategoryDescription = category.Description <br/>}</pre>',
        });
    }

    focusOnEditor() {
        var editorElement = $("#transformerAceEditor").length == 1 ? $("#transformerAceEditor")[0] : null;
        if (editorElement) {
            var docEditor = ko.utils.domData.get($("#transformerAceEditor")[0], "aceEditor");
            if (docEditor) {
                docEditor.focus();
            }
        }
    }

    editExistingTransformer(unescapedTransformerName: string): JQueryPromise<Raven.Abstractions.Indexing.TransformerDefinition> {
        const transformerName = decodeURIComponent(unescapedTransformerName);
        this.loadedTransformerName(transformerName);
        return this.fetchTransformerToEdit(transformerName)
            .done((trans: Raven.Abstractions.Indexing.TransformerDefinition) => this.editedTransformer(new transformer(trans))); 
    }

    fetchTransformerToEdit(transformerName: string): JQueryPromise<Raven.Abstractions.Indexing.TransformerDefinition> {
        return new getSingleTransformerCommand(transformerName, this.activeDatabase()).execute();
    }

    saveTransformer() {

        if (this.isValid(this.globalValidationGroup)) {
            this.isSaving(true);
            eventsCollector.default.reportEvent("transformer", "save");

            if (this.isEditingExistingTransformer() && this.editedTransformer().nameChanged()) {
                var db = this.activeDatabase();
                var saveTransformerWithNewNameViewModel = new saveTransformerWithNewNameConfirm(this.editedTransformer(), db);
                saveTransformerWithNewNameViewModel.saveTask.done(() => {
                    this.dirtyFlag().reset(); // Resync Changes
                    this.updateUrl(this.editedTransformer().name());
                });
                dialog.show(saveTransformerWithNewNameViewModel);
            } else {
                this.editedTransformer().name(this.editedTransformer().name().trim());
                new saveTransformerCommand(this.editedTransformer(), this.activeDatabase())
                    .execute()
                    .done(() => {
                        this.dirtyFlag().reset();
                        if (!this.isEditingExistingTransformer()) {
                            this.isEditingExistingTransformer(true);
                            this.updateUrl(this.editedTransformer().name());
                        }
                     })
                    .always(() => this.isSaving(false));                    
            }
        }
    }

    updateUrl(transformerName: string) {
        router.navigate(appUrl.forEditTransformer(transformerName, this.activeDatabase()));
    }

    refreshTransformer() {
        eventsCollector.default.reportEvent("transformer", "refresh");

        var canContinue = this.canContinueIfNotDirty("Unsaved Data", "You have unsaved data. Are you sure you want to refresh the transformer from the server?");
        canContinue
            .done(() => {
                var transformerName = this.loadedTransformerName();
                this.fetchTransformerToEdit(transformerName)                    
                    .done((trans: Raven.Abstractions.Indexing.TransformerDefinition) => {
                        this.editedTransformer(new transformer(trans));
                        this.dirtyFlag().reset();
                    })
                    .fail(() => {
                        messagePublisher.reportError("Could not find " + transformerName + " transformer");
                        this.navigate(appUrl.forTransformers(this.activeDatabase()));
                    });
            });
    }

    formatTransformer() {
        eventsCollector.default.reportEvent("transformer", "format");

        var editedTransformer: transformer = this.editedTransformer();

        new formatIndexCommand(this.activeDatabase(), [editedTransformer.transformResults()])
            .execute()
            .done((result: string[]) => {
                var formatedTransformer = result[0];
                if (formatedTransformer.indexOf("Could not format:") == -1) {
                    editedTransformer.transformResults(formatedTransformer);
                } else {
                    messagePublisher.reportError("Failed to format transformer!", formatedTransformer);
                }
            });
    }

    deleteTransformer() {
        eventsCollector.default.reportEvent("transformer", "delete");

        const transformer = this.editedTransformer();

        if (transformer) {
            const db = this.activeDatabase();
            const deleteViewmodel = new deleteTransformerConfirm([transformer.name()], db);
            deleteViewmodel.deleteTask.done(() => {
                router.navigate(appUrl.forTransformers(db));
            });
            dialog.show(deleteViewmodel);
        }
    }

    private initValidation() {
        const rg1 = /^[^\\]*$/; // forbidden character - backslash
        this.editedTransformer().name.extend({
            required: true,
            validation: [
                {
                    validator: (val: string) => rg1.test(val),
                    message: "Can't use backslash in transformer name"
                }]
        });

        this.editedTransformer().transformResults.extend({
            required: true           
        });
    }

    private initializeObservables() {

        this.globalValidationGroup = ko.validatedObservable({
            userTransformerName: this.editedTransformer().name,
            userTransformerContent: this.editedTransformer().transformResults
        });

        this.transformerName = ko.computed(() => (!!this.editedTransformer() && this.isEditingExistingTransformer()) ? this.editedTransformer().name() : null);
        this.dirtyFlag = new ko.DirtyFlag([this.editedTransformer().name, this.editedTransformer().transformResults], false, jsonUtil.newLineNormalizingHashFunction);
        
        this.isSaveEnabled = ko.pureComputed(() => {
            if (!this.dirtyFlag().isDirty() && this.isEditingExistingTransformer()) {
                return false;
            }
            return true;
        });  
    }
}

export = editTransformer;
