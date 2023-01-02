﻿
class requestExecution {

    spinnerVisible = false;
    alertVisible = false;
    completed = false;

    private spinnerTimeout: ReturnType<typeof setTimeout>;
    private alertTimeout: ReturnType<typeof setTimeout>;
    private readonly sync: () => void;

    private timeForSpinner: number;

    private timeToAlert = 0;

    constructor(timeForSpinner: number, timeToAlert = 0, sync: () => void) {
        this.timeToAlert = timeToAlert;
        this.timeForSpinner = timeForSpinner;
        this.setTimeouts();
        this.sync = sync;
    }

    markCompleted() {
        this.cleanState();
        this.sync();
        this.completed = true;
    }

    markProgress() {
        this.cleanState();
        this.setTimeouts();
        this.sync();
    }

    private cleanState() {
        clearTimeout(this.spinnerTimeout);
        if (this.alertTimeout) {
            clearTimeout(this.alertTimeout);
        }
        this.spinnerVisible = false;
        this.alertVisible = false;
    }

    private setTimeouts() {
        this.spinnerTimeout = setTimeout(() => {
            this.spinnerVisible = true;
            this.sync();
        }, this.timeForSpinner);

        if (this.timeToAlert > 0) {
            this.alertTimeout = setTimeout(() => {
                this.alertVisible = true;
                this.sync();
            }, this.timeToAlert);
        }
    }
}

export = requestExecution;
