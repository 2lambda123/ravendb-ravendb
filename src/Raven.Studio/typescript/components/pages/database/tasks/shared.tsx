﻿import database from "models/resources/database";
import {
    AnyEtlOngoingTaskInfo,
    OngoingEtlTaskNodeInfo,
    OngoingTaskInfo,
    OngoingTaskSharedInfo,
} from "../../../models/tasks";
import useBoolean from "hooks/useBoolean";
import React, { useCallback } from "react";
import router from "plugins/router";
import { withPreventDefault } from "../../../utils/common";
import { RichPanelDetailItem } from "../../../common/RichPanel";
import ongoingTaskModel from "models/database/tasks/ongoingTaskModel";
import viewHelpers from "common/helpers/view/viewHelpers";
import genUtils from "common/generalUtils";

export interface BaseOngoingTaskPanelProps<T extends OngoingTaskInfo> {
    db: database;
    data: T;
    onDelete: (task: OngoingTaskSharedInfo) => void;
    toggleState: (task: OngoingTaskSharedInfo, enable: boolean) => void;
    onToggleDetails?: () => void;
}

export interface ICanShowTransformationScriptPreview {
    showItemPreview: (task: OngoingTaskInfo, scriptName: string) => void;
}

export function useTasksOperations(editUrl: string, props: BaseOngoingTaskPanelProps<OngoingTaskInfo>) {
    const { onDelete, data, toggleState, onToggleDetails } = props;
    const { value: detailsVisible, toggle: toggleDetailsVisible } = useBoolean(false);

    const onEdit = useCallback(() => {
        router.navigate(editUrl);
    }, [editUrl]);

    const onDeleteHandler = useCallback(() => {
        const task = data.shared;
        const taskType = ongoingTaskModel.formatStudioTaskType(task.taskType);
        viewHelpers
            .confirmationMessage(
                "Delete Ongoing Task?",
                `You're deleting ${taskType} task: <br /><ul><li><strong>${genUtils.escapeHtml(
                    task.taskName
                )}</strong></li></ul>`,
                {
                    buttons: ["Cancel", "Delete"],
                    html: true,
                }
            )
            .done((result) => {
                if (result.can) {
                    onDelete(task);
                }
            });
    }, [onDelete, data.shared]);

    const toggleStateHandler = useCallback(
        (enable: boolean) => {
            const task = data.shared;
            const confirmationTitle = enable ? "Enable Task" : "Disable Task";
            const taskType = ongoingTaskModel.formatStudioTaskType(task.taskType);
            const confirmationMsg = enable
                ? `You&apos;re enabling ${taskType} task:<br><ul><li><strong>${task.taskName}</strong></li></ul>`
                : `You&apos;re disabling ${taskType} task:<br><ul><li><strong>${task.taskName}</strong></li></ul>`;
            const confirmButtonText = enable ? "Enable" : "Disable";

            viewHelpers
                .confirmationMessage(confirmationTitle, confirmationMsg, {
                    buttons: ["Cancel", confirmButtonText],
                    html: true,
                })
                .done((result) => {
                    if (result.can) {
                        toggleState(task, enable);
                    }
                });
        },
        [toggleState, data.shared]
    );

    const toggleDetails = useCallback(() => {
        toggleDetailsVisible();
        onToggleDetails?.();
    }, [onToggleDetails, toggleDetailsVisible]);

    return {
        detailsVisible,
        toggleDetails,
        onEdit,
        onDeleteHandler,
        toggleStateHandler,
    };
}

export function OngoingTaskResponsibleNode(props: { task: OngoingTaskInfo }) {
    const { task } = props;
    const preferredMentor = task.shared.mentorNodeTag;
    const currentNode = task.shared.responsibleNodeTag;

    const usingNotPreferredNode = preferredMentor && currentNode ? preferredMentor !== currentNode : false;

    if (currentNode) {
        if (usingNotPreferredNode) {
            return (
                <div className="node">
                    <div>
                        <i className="icon-cluster-node"></i>
                        <span className="text-danger pulse" title="User preferred node for this task">
                            {preferredMentor}
                        </span>
                        <i className="icon-arrow-right pulse text-danger"></i>
                        <span className="text-success" title="Cluster node that is temporary responsible for this task">
                            {currentNode}
                        </span>
                    </div>
                </div>
            );
        } else {
            return (
                <div className="node">
                    <div
                        title={
                            task.shared.taskType === "PullReplicationAsHub"
                                ? "Hub node that is serving this Sink task"
                                : "Cluster node that is responsible for this task"
                        }
                    >
                        <i className="icon-cluster-node"></i>
                        <span>{currentNode}</span>
                    </div>
                </div>
            );
        }
    }

    return (
        <div title="No node is currently handling this task">
            <i className="icon-cluster-node"></i> N/A
        </div>
    );
}

export function OngoingTaskName(props: { task: OngoingTaskInfo; canEdit: boolean; editUrl: string }) {
    const { task, canEdit, editUrl } = props;
    return (
        <div className="panel-name flex-grow">
            <h3 title={"Task name: " + task.shared.taskName}>
                {canEdit && (
                    <a href={editUrl}>
                        <span>{task.shared.taskName}</span>
                    </a>
                )}
                {!canEdit && <div>{task.shared.taskName}</div>}
            </h3>
        </div>
    );
}

export function OngoingTaskStatus(props: {
    task: OngoingTaskInfo;
    canEdit: boolean;
    toggleState: (enabled: boolean) => void;
}) {
    const { task, canEdit, toggleState } = props;
    return (
        <div className="btn-group">
            <button type="button" className="btn dropdown-toggle" data-toggle="dropdown" disabled={!canEdit}>
                <span>{task.shared.taskState}</span>
                <span className="caret"></span>
            </button>
            <ul className="dropdown-menu">
                <li>
                    <a href="#" onClick={withPreventDefault(() => toggleState(true))}>
                        <span>Enable</span>
                    </a>
                </li>
                <li>
                    <a href="#" onClick={withPreventDefault(() => toggleState(false))}>
                        <span>Disable</span>
                    </a>
                </li>
            </ul>
        </div>
    );
}

export function OngoingTaskActions(props: {
    canEdit: boolean;
    task: OngoingTaskInfo;
    toggleDetails: () => void;
    onEdit: () => void;
    onDelete: () => void;
}) {
    const { canEdit, task, onEdit, onDelete, toggleDetails } = props;

    return (
        <div className="actions-container">
            <div className="actions">
                <button className="btn btn-default" type="button" onClick={toggleDetails} title="Click for details">
                    <i className="icon-info"></i>
                </button>
                {!task.shared.serverWide && (
                    <button type="button" className="btn btn-default" title="Edit task" onClick={onEdit}>
                        <i className="icon-edit"></i>
                    </button>
                )}

                {!task.shared.serverWide && (
                    <button
                        className="btn btn-danger"
                        type="button"
                        disabled={!canEdit}
                        onClick={onDelete}
                        title="Delete task"
                    >
                        <i className="icon-trash"></i>
                    </button>
                )}
            </div>
        </div>
    );
}

export function ConnectionStringItem(props: {
    canEdit: boolean;
    connectionStringName: string;
    connectionStringsUrl: string;
    connectionStringDefined: boolean;
}) {
    const { canEdit, connectionStringDefined, connectionStringName, connectionStringsUrl } = props;

    if (connectionStringDefined) {
        return (
            <RichPanelDetailItem>
                Connection String:
                <div className="value">
                    {canEdit ? (
                        <a title="Connection string name" target="_blank" href={connectionStringsUrl}>
                            {connectionStringName}
                        </a>
                    ) : (
                        <div>{connectionStringName}</div>
                    )}
                </div>
            </RichPanelDetailItem>
        );
    }

    return (
        <RichPanelDetailItem>
            Connection String:
            <div className="value text-danger">
                <i className="icon-danger text-danger"></i>
                This connection string is not defined.
            </div>
        </RichPanelDetailItem>
    );
}

export function EmptyScriptsWarning(props: { task: AnyEtlOngoingTaskInfo }) {
    const emptyScripts = findScriptsWithOutMatchingDocuments(props.task);

    if (!emptyScripts.length) {
        return null;
    }

    return (
        <RichPanelDetailItem className="text-warning">
            <small>
                <i className="icon-warning" />
                Following scripts don&apos;t match any documents: {emptyScripts.join(", ")}
            </small>
        </RichPanelDetailItem>
    );
}

function findScriptsWithOutMatchingDocuments(
    data: OngoingTaskInfo<OngoingTaskSharedInfo, OngoingEtlTaskNodeInfo>
): string[] {
    const perScriptCounts = new Map<string, number>();
    data.nodesInfo.forEach((node) => {
        if (node.etlProgress) {
            node.etlProgress.forEach((progress) => {
                const transformationName = progress.transformationName;
                perScriptCounts.set(
                    transformationName,
                    (perScriptCounts.get(transformationName) ?? 0) + progress.global.total
                );
            });
        }
    });

    return Array.from(perScriptCounts.entries())
        .filter((x) => x[1] === 0)
        .map((x) => x[0]);
}

export function taskKey(task: OngoingTaskSharedInfo) {
    // we don't want to use taskId here - as it changes after edit
    return task.taskType + "-" + task.taskName;
}
