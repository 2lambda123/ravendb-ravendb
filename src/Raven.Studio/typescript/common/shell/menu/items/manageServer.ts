﻿import appUrl = require("common/appUrl");
import settingsAccessAuthorizer = require("common/settingsAccessAuthorizer");
import accessHelper = require("viewmodels/shell/accessHelper");
import intermediateMenuItem = require("common/shell/menu/intermediateMenuItem");
import leafMenuItem = require("common/shell/menu/leafMenuItem");

export = getManageServerMenuItem;

function getManageServerMenuItem() {
    let canReadOrWrite = settingsAccessAuthorizer.canReadOrWrite;
    var items: menuItem[] = [
        new leafMenuItem({
            route: 'admin/settings/cluster',
            moduleId: "viewmodels/manage/cluster",
            title: "Cluster",
            nav: true,
            css: 'icon-cluster',
            dynamicHash: appUrl.forCluster,
            enabled: canReadOrWrite
        }),
        new leafMenuItem({
            route: 'admin/settings/addClusterNode',
            moduleId: "viewmodels/manage/addClusterNode",
            title: "Add Cluster Node",
            nav: false,
            dynamicHash: appUrl.forAddClusterNode,
            enabled: canReadOrWrite,
            itemRouteToHighlight: 'admin/settings/cluster'
        }),
        new leafMenuItem({
            route: 'admin/settings/debugInfo',
            moduleId: 'viewmodels/manage/infoPackage',
            title: 'Gather Debug Info',
            nav: true,
            css: 'icon-gather-debug-information',
            dynamicHash: appUrl.forDebugInfo,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/adminJsConsole',
            moduleId: "viewmodels/manage/adminJsConsole",
            title: "Administrator JS Console",
            nav: true,
            css: 'icon-administrator-js-console',
            dynamicHash: appUrl.forAdminJsConsole,
            enabled: accessHelper.isGlobalAdmin
        })
        /* TODO
        new leafMenuItem({
            route: 'admin/settings/backup',
            moduleId: 'viewmodels/manage/backup',
            title: 'Backup',
            nav: true,
            css: 'icon-backup',
            dynamicHash: appUrl.forBackup,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/compact',
            moduleId: 'viewmodels/manage/compact',
            title: 'Compact',
            nav: true,
            css: 'icon-compact',
            dynamicHash: appUrl.forCompact,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/restore',
            moduleId: 'viewmodels/manage/restore',
            title: 'Restore',
            nav: true,
            css: 'icon-restore',
            dynamicHash: appUrl.forRestore,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/adminLogs',
            moduleId: 'viewmodels/manage/adminLogs',
            title: 'Admin Logs',
            nav: true,
            css: 'icon-admin-logs',
            dynamicHash: appUrl.forAdminLogs,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/topology',
            moduleId: 'viewmodels/manage/topology',
            title: 'Server Topology',
            nav: true,
            css: 'icon-server-topology',
            dynamicHash: appUrl.forServerTopology,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/trafficWatch',
            moduleId: 'viewmodels/manage/trafficWatch',
            title: 'Traffic Watch',
            nav: true,
            css: 'icon-trafic-watch',
            dynamicHash: appUrl.forTrafficWatch,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/licenseInformation',
            moduleId: 'viewmodels/manage/licenseInformation',
            title: 'License Information',
            nav: true,
            css: 'icon-license-information',
            dynamicHash: appUrl.forLicenseInformation,
            enabled: canReadOrWrite
        }),*/
        /*
        new leafMenuItem({
            route: 'admin/settings/ioTest',
            moduleId: 'viewmodels/manage/ioTest',
            title: 'IO Test',
            nav: true,
            css: 'icon-io-test',
            dynamicHash: appUrl.forIoTest,
            enabled: accessHelper.isGlobalAdmin
        }),
        new leafMenuItem({
            route: 'admin/settings/diskIoViewer',
            moduleId: 'viewmodels/manage/diskIoViewer',
            title: 'Disk IO Viewer',
            nav: true,
            css: 'icon-disk-io-viewer',
            dynamicHash: appUrl.forDiskIoViewer,
            enabled: canReadOrWrite
        }),
        
        new leafMenuItem({
            route: 'admin/settings/studioConfig',
            moduleId: 'viewmodels/manage/studioConfig',
            title: 'Studio Config',
            nav: true,
            css: 'icon-studio-config',
            dynamicHash: appUrl.forStudioConfig,
            enabled: canReadOrWrite
        }),
        new leafMenuItem({
            route: 'admin/settings/hotSpare',
            moduleId: 'viewmodels/manage/hotSpare',
            title: 'Hot Spare',
            nav: true,
            css: 'icon-hot-spare',
            dynamicHash: appUrl.forHotSpare,
            enabled: accessHelper.isGlobalAdmin
        })*/
    ];

    return new intermediateMenuItem('Manage server', items, 'icon-manage-server');
}

