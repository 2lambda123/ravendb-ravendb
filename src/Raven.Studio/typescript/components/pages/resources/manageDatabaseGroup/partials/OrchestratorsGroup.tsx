﻿import React, { useCallback } from "react";
import { Button } from "reactstrap";
import { DndProvider } from "react-dnd";
import { HTML5Backend } from "react-dnd-html5-backend";
import {
    ReorderNodes,
    ReorderNodesControls,
} from "components/pages/resources/manageDatabaseGroup/partials/ReorderNodes";
import { OrchestratorInfoComponent } from "components/pages/resources/manageDatabaseGroup/partials/NodeInfoComponent";
import { DeletionInProgress } from "components/pages/resources/manageDatabaseGroup/partials/DeletionInProgress";
import { useEventsCollector } from "hooks/useEventsCollector";
import { useServices } from "hooks/useServices";
import app from "durandal/app";
import addNewOrchestratorToDatabase from "viewmodels/resources/addNewOrchestatorToDatabaseGroup";
import classNames from "classnames";
import {
    RichPanel,
    RichPanelActions,
    RichPanelHeader,
    RichPanelInfo,
    RichPanelName,
} from "components/common/RichPanel";
import {
    DatabaseGroup,
    DatabaseGroupActions,
    DatabaseGroupItem,
    DatabaseGroupList,
    DatabaseGroupNode,
} from "components/common/DatabaseGroup";
import { useGroup } from "components/pages/resources/manageDatabaseGroup/partials/useGroup";
import { Icon } from "components/common/Icon";
import useConfirm from "components/common/ConfirmDialog";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import { useAppSelector } from "components/store";

export function OrchestratorsGroup() {
    const db = useAppSelector(databaseSelectors.activeDatabase);

    const {
        fixOrder,
        setNewOrder,
        newOrder,
        setFixOrder,
        addNodeEnabled,
        canSort,
        sortableMode,
        enableReorder,
        exitReorder,
    } = useGroup(db.nodes, db.fixOrder);

    const { databasesService } = useServices();
    const { reportEvent } = useEventsCollector();
    const confirm = useConfirm();

    const addNode = useCallback(() => {
        const addKeyView = new addNewOrchestratorToDatabase(db.name, db.nodes);
        app.showBootstrapDialog(addKeyView);
    }, [db]);

    const saveNewOrder = useCallback(
        async (tagsOrder: string[], fixOrder: boolean) => {
            reportEvent("db-group", "save-order");
            await databasesService.reorderNodesInGroup(db.name, tagsOrder, fixOrder);
            exitReorder();
        },
        [databasesService, db.name, reportEvent, exitReorder]
    );

    const deleteOrchestratorFromGroup = useCallback(
        async (nodeTag: string) => {
            const isConfirmed = await confirm({
                icon: "trash",
                title: (
                    <span>
                        Do you want to delete orchestrator from node <strong>{nodeTag}</strong>?
                    </span>
                ),
                confirmText: "Delete",
                actionColor: "danger",
            });

            if (isConfirmed) {
                await databasesService.deleteOrchestratorFromNode(db.name, nodeTag);
            }
        },
        [confirm, databasesService, db.name]
    );

    const onSave = async () => {
        await saveNewOrder(
            newOrder.map((x) => x.tag),
            fixOrder
        );
    };

    return (
        <RichPanel className="mt-3">
            <RichPanelHeader className="bg-faded-orchestrator">
                <RichPanelInfo>
                    <RichPanelName className="text-orchestrator">
                        <Icon icon="orchestrator" /> Orchestrators
                    </RichPanelName>
                </RichPanelInfo>
                <RichPanelActions>
                    <ReorderNodesControls
                        enableReorder={enableReorder}
                        canSort={canSort}
                        sortableMode={sortableMode}
                        cancelReorder={exitReorder}
                        onSave={onSave}
                    />
                </RichPanelActions>
            </RichPanelHeader>

            {sortableMode ? (
                <DndProvider backend={HTML5Backend}>
                    <ReorderNodes
                        fixOrder={fixOrder}
                        setFixOrder={setFixOrder}
                        newOrder={newOrder}
                        setNewOrder={setNewOrder}
                    />
                </DndProvider>
            ) : (
                <React.Fragment>
                    <DatabaseGroup>
                        <DatabaseGroupList>
                            <DatabaseGroupItem
                                className={classNames("item-new", "position-relative", {
                                    "item-disabled": !addNodeEnabled,
                                })}
                            >
                                <DatabaseGroupNode icon="node-add" color="success" />
                                <DatabaseGroupActions>
                                    <Button
                                        size="xs"
                                        color="success"
                                        outline
                                        className="rounded-pill stretched-link"
                                        disabled={!addNodeEnabled}
                                        onClick={addNode}
                                    >
                                        <Icon icon="plus" />
                                        Add node
                                    </Button>
                                </DatabaseGroupActions>
                            </DatabaseGroupItem>
                            {db.nodes.map((node) => (
                                <OrchestratorInfoComponent
                                    key={node.tag}
                                    node={node}
                                    canDelete={db.nodes.length > 1}
                                    deleteFromGroup={deleteOrchestratorFromGroup}
                                />
                            ))}

                            {db.deletionInProgress.map((deleting) => (
                                <DeletionInProgress key={deleting} nodeTag={deleting} />
                            ))}
                        </DatabaseGroupList>
                    </DatabaseGroup>
                </React.Fragment>
            )}
        </RichPanel>
    );
}
