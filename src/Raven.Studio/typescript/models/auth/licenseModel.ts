/// <reference path="../../../typings/tsd.d.ts" />

import getLicenseStatusCommand = require("commands/auth/getLicenseStatusCommand");

class licenseModel {
    static licenseStatus = ko.observable<Raven.Server.Commercial.LicenseStatus>();
    static supportCoverage = ko.observable<supportCoverageDto>();

    private static baseUrl = "https://ravendb.net/request-license";

    static generateLicenseRequestUrl(limitType: Raven.Server.Commercial.LimitType = null): string {
        let url = `${licenseModel.baseUrl}?`;

        const status = this.licenseStatus();
        if (status && status.Id) {
            url += `&id=${btoa(status.Id)}`;
        }

        if (limitType) {
            url += `limit=${btoa(limitType)}`;
        }

        return url;
    }

    static fetchLicenseStatus(): JQueryPromise<Raven.Server.Commercial.LicenseStatus> {
        return new getLicenseStatusCommand()
            .execute()
            .done((result: Raven.Server.Commercial.LicenseStatus) => {
                if (result.Status.includes("AGPL")) {
                    result.Status = "Development Only";
                }
                licenseModel.licenseStatus(result);
            });
    }

    static licenseShortDescription = ko.pureComputed(() => {
        const status = licenseModel.licenseStatus();
        if (!status || status.Type === "None") {
            return 'no-license';
        }
       
        const maxMemory = status.MaxMemory === 0 ? "Unlimited" : `${status.MaxMemory} GB RAM` ;
        return `${status.MaxCores} Cores, ${maxMemory}, Cluster size: ${status.MaxClusterSize}`;
    });


    static licenseCssClass = ko.pureComputed(() => {
        const status = licenseModel.licenseStatus();
        if (!status || status.Type === "None") {
            return 'no-license';
        }
        if (status.Status.includes("Expired")) {
            return 'expired';
        } else {
            return 'commercial';
        }
    });

    static supportCssClass = ko.pureComputed(() => {
        const support = licenseModel.supportCoverage();
        if (!support) {
            return 'no-support';
        }
        switch (support.Status) {
            case 'ProductionSupport':
                return 'production-support';
            case 'ProfessionalSupport':
                return 'professional-support';
            case 'PartialSupport':
                return 'partial-support';
            default:
                return 'no-support';
        }
    });
}

export = licenseModel;
