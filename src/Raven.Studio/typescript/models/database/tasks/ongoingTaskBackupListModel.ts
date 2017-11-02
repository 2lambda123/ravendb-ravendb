﻿/// <reference path="../../../../typings/tsd.d.ts"/>
import app = require("durandal/app");
import appUrl = require("common/appUrl");
import router = require("plugins/router");
import ongoingTask = require("models/database/tasks/ongoingTaskModel"); 
import ongoingTaskInfoCommand = require("commands/database/tasks/getOngoingTaskInfoCommand");
import backupNowCommand = require("commands/database/tasks/backupNowCommand");
import activeDatabaseTracker = require("common/shell/activeDatabaseTracker");
import backupNow = require("viewmodels/database/tasks/backupNow");
import generalUtils = require("common/generalUtils");
import timeHelpers = require("common/timeHelpers");

class ongoingTaskBackupListModel extends ongoingTask {
    private static neverBackedUpText = "Never backed up";

    editUrl: KnockoutComputed<string>;
    activeDatabase = activeDatabaseTracker.default.database;

    backupType = ko.observable<Raven.Client.ServerWide.PeriodicBackup.BackupType>();
    nextBackup = ko.observable<Raven.Client.ServerWide.Operations.NextBackup>();
    lastFullBackup = ko.observable<string>();
    lastIncrementalBackup = ko.observable<string>();
    onGoingBackup = ko.observable<Raven.Client.ServerWide.Operations.RunningBackup>();

    showBackupDetails = ko.observable(false);
    textClass = ko.observable<string>();

    noBackupDestinations = ko.observable<boolean>(false);
    backupNowInProgress = ko.observable<boolean>(false);
    disabledBackupNowReason = ko.observable<string>();
    isBackupNowEnabled: KnockoutComputed<boolean>;
    neverBackedUp = ko.observable<boolean>(false);
    fullBackupTypeName: KnockoutComputed<string>;

    lastFullBackupHumanized: KnockoutComputed<string>;
    lastIncrementalBackupHumanized: KnockoutComputed<string>;
    nextBackupHumanized: KnockoutComputed<string>;
    onGoingBackupHumanized: KnockoutComputed<string>;

    constructor(dto: Raven.Client.ServerWide.Operations.OngoingTaskBackup) {
        super();

        this.update(dto);
        this.initializeObservables();

        this.isBackupNowEnabled = ko.pureComputed(() => {
            if (this.nextBackupHumanized() === "N/A") {
                this.disabledBackupNowReason("No backup destinations");
                return false;
            }

            if (this.onGoingBackupHumanized()) {
                this.disabledBackupNowReason("Backup is already in progress");
                return false;
            }

            this.disabledBackupNowReason(null);
            return true;
        });

        this.lastFullBackupHumanized = ko.pureComputed(() => {
            const lastFullBackup = this.lastFullBackup();
            if (!lastFullBackup) {
                return ongoingTaskBackupListModel.neverBackedUpText;
            }

            const fromDuration = generalUtils.formatDurationByDate(moment.utc(lastFullBackup), true);
            return `${fromDuration} ago`;
        });

        this.lastIncrementalBackupHumanized = ko.pureComputed(() => {
            const lastIncrementalBackup = this.lastIncrementalBackup();
            if (!lastIncrementalBackup) {
                return ongoingTaskBackupListModel.neverBackedUpText;
            }

            const fromDuration = generalUtils.formatDurationByDate(moment.utc(lastIncrementalBackup), true);
            return `${fromDuration} ago`;
        });

        this.nextBackupHumanized = ko.pureComputed(() => {
            const nextBackup = this.nextBackup();
            if (!nextBackup) {
                this.textClass("text-warning");
                return "N/A";
            }

            this.textClass("text-details");
            const now = timeHelpers.utcNowWithSecondPrecision();
            const diff = moment.utc(nextBackup.DateTime).diff(now);
            const formatDuration = generalUtils.formatDuration(moment.duration(diff), true, 2, true);

            if (diff <= 0) {
                this.refreshBackupInfo();
            }

            return `in ${formatDuration} (${this.getBackupType(this.backupType(), nextBackup.IsFull)})`;
        });

        this.onGoingBackupHumanized = ko.pureComputed(() => {
            const onGoingBackup = this.onGoingBackup();
            if (!onGoingBackup) {
                return null;
            }

            const fromDuration = generalUtils.formatDurationByDate(moment.utc(onGoingBackup.StartTime), true);
            return `${fromDuration} ago (${this.getBackupType(this.backupType(), onGoingBackup.IsFull)})`;
        });

        this.fullBackupTypeName = ko.pureComputed(() => this.getBackupType(this.backupType(), true));
    }

    initializeObservables() {
        super.initializeObservables();

        const urls = appUrl.forCurrentDatabase();
        this.editUrl = urls.editPeriodicBackupTask(this.taskId); 
    }

    update(dto: Raven.Client.ServerWide.Operations.OngoingTaskBackup) {
        super.update(dto);

        this.backupType(dto.BackupType);
        this.neverBackedUp(!dto.LastFullBackup);
        this.lastFullBackup(dto.LastFullBackup);
        this.lastIncrementalBackup(dto.LastIncrementalBackup);
        this.nextBackup(dto.NextBackup);
        this.onGoingBackup(dto.OnGoingBackup);
    }

    private getBackupType(backupType: Raven.Client.ServerWide.PeriodicBackup.BackupType, isFull: boolean): string {
        if (!isFull) {
            return "Incremental";
        }

        if (backupType === "Snapshot") {
            return "Snapshot";
        }

        return "Full";
    }

    editTask() {
        router.navigate(this.editUrl());
    }

    toggleDetails() {
        this.showBackupDetails.toggle();

        if (this.showBackupDetails()) {
            this.refreshBackupInfo();
        } 
    }

    refreshBackupInfo() {
        return ongoingTaskInfoCommand.forBackup(this.activeDatabase(), this.taskId)
            .execute()
            .done((result: Raven.Client.ServerWide.Operations.OngoingTaskBackup) => this.update(result));
    }

    backupNow() {
        const confirmDeleteViewModel = new backupNow(this.getBackupType(this.backupType(), true));

        confirmDeleteViewModel
            .result
            .done((confirmResult: backupNowConfirmResult) => {
                if (confirmResult.can) {
                    this.backupNowInProgress(true);

                    const task = new backupNowCommand(this.activeDatabase(), this.taskId, confirmResult.isFullBackup);
                    task.execute().always(() => this.refreshBackupInfo().always(() => this.backupNowInProgress(false)));
                }
            });

        app.showBootstrapDialog(confirmDeleteViewModel);
    }
}

export = ongoingTaskBackupListModel;
