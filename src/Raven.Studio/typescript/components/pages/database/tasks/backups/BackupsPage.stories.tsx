﻿import React from "react";
import { ComponentMeta, ComponentStory } from "@storybook/react";
import { withBootstrap5, forceStoryRerender, withStorybookContexts } from "test/storybookTestUtils";
import { DatabasesStubs } from "test/stubs/DatabasesStubs";
import clusterTopologyManager from "common/shell/clusterTopologyManager";
import { mockServices } from "test/mocks/services/MockServices";
import { TasksStubs } from "test/stubs/TasksStubs";
import { boundCopy } from "components/utils/common";
import OngoingTaskBackup = Raven.Client.Documents.Operations.OngoingTasks.OngoingTaskBackup;
import { BackupsPage } from "./BackupsPage";
import { mockStore } from "test/mocks/store/MockStore";

export default {
    title: "Pages/Backups",
    component: BackupsPage,
    decorators: [withStorybookContexts, withBootstrap5],
    excludeStories: /Template$/,
} as ComponentMeta<typeof BackupsPage>;

function commonInit() {
    const { accessManager } = mockStore;
    accessManager.with_securityClearance("ClusterAdmin");

    clusterTopologyManager.default.localNodeTag = ko.pureComputed(() => "A");
}

export const EmptyView: ComponentStory<typeof BackupsPage> = () => {
    const db = DatabasesStubs.shardedDatabase();

    commonInit();

    const { tasksService } = mockServices;

    tasksService.withGetTasks((dto) => {
        dto.SubscriptionsCount = 0;
        dto.OngoingTasks = [];
        dto.PullReplications = [];
    });
    tasksService.withGetProgress((dto) => {
        dto.Results = [];
    });

    tasksService.withGetManualBackup((x) => (x.Status = null));

    return <BackupsPage db={db} />;
};

export const FullView: ComponentStory<typeof BackupsPage> = () => {
    const db = DatabasesStubs.shardedDatabase();

    commonInit();

    const { tasksService } = mockServices;

    tasksService.withGetTasks();
    tasksService.withGetProgress();
    tasksService.withGetManualBackup();

    return <BackupsPage db={db} />;
};

export const PeriodicBackupTemplate = (args: {
    disabled?: boolean;
    customizeTask?: (x: OngoingTaskBackup) => void;
}) => {
    const db = DatabasesStubs.shardedDatabase();

    commonInit();

    const { tasksService } = mockServices;

    tasksService.withGetTasks((x) => {
        const ongoingTask = TasksStubs.getPeriodicBackupListItem();
        if (args.disabled) {
            ongoingTask.TaskState = "Disabled";
        }
        args.customizeTask?.(ongoingTask);
        x.OngoingTasks = [ongoingTask];
        x.PullReplications = [];
        x.SubscriptionsCount = 0;
    });

    tasksService.withGetManualBackup();

    return <BackupsPage {...forceStoryRerender()} db={db} />;
};

export const PeriodicBackupDisabled = boundCopy(PeriodicBackupTemplate, {
    disabled: true,
});

export const PeriodicBackupEnabledEncrypted = boundCopy(PeriodicBackupTemplate, {
    disabled: false,
    customizeTask: (x) => (x.IsEncrypted = true),
});
