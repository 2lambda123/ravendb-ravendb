﻿import React, { useState } from "react";
import { DatabaseSharedInfo, ShardedDatabaseSharedInfo } from "components/models/databases";
import classNames from "classnames";
import { useAppUrls } from "hooks/useAppUrls";
import DatabaseLockMode = Raven.Client.ServerWide.DatabaseLockMode;
import {
    Button,
    ButtonGroup,
    Collapse,
    DropdownItem,
    DropdownMenu,
    DropdownToggle,
    Input,
    Spinner,
    UncontrolledDropdown,
} from "reactstrap";
import {
    RichPanel,
    RichPanelActions,
    RichPanelHeader,
    RichPanelInfo,
    RichPanelName,
    RichPanelSelect,
    RichPanelStatus,
} from "components/common/RichPanel";
import appUrl from "common/appUrl";
import { NodeSet, NodeSetItem, NodeSetLabel } from "components/common/NodeSet";
import assertUnreachable from "components/utils/assertUnreachable";
import { useAppDispatch, useAppSelector } from "components/store";
import {
    changeDatabasesLockMode,
    compactDatabase,
    confirmDeleteDatabases,
    confirmSetLockMode,
    confirmToggleDatabases,
    deleteDatabases,
    selectActiveDatabase,
    toggleDatabases,
} from "components/common/shell/databasesSlice";
import { useEventsCollector } from "hooks/useEventsCollector";
import useBoolean from "hooks/useBoolean";
import { DatabaseDistribution } from "components/pages/resources/databases/partials/DatabaseDistribution";
import { ValidDatabasePropertiesPanel } from "components/pages/resources/databases/partials/ValidDatabasePropertiesPanel";

interface DatabasePanelProps {
    db: DatabaseSharedInfo;
    selected: boolean;
    toggleSelection: () => void;
}

function getStatusColor(db: DatabaseSharedInfo) {
    if (db.disabled) {
        return "warning";
    }
    return "success";
}

//TODO:
// eslint-disable-next-line @typescript-eslint/no-unused-vars
function badgeClass(db: DatabaseSharedInfo) {
    /* TODO Created getStatusColor() function this one might be deprecated
     if (this.hasLoadError()) {
                return "state-danger";
            }

            if (this.disabled()) {
                return "state-warning";
            }

            if (this.online()) {
                return "state-success";
            }

            return "state-offline"; // offline
     */
    return "state-success";
}

// eslint-disable-next-line @typescript-eslint/no-unused-vars
function badgeText(db: DatabaseSharedInfo) {
    if (db.disabled) {
        return "Disabled";
    }
    /* TODO
        if (this.hasLoadError()) {
                return "Error";
            }

            if (this.online()) {
                return "Online";
            }
            return "Offline";
     */

    return "Online";
}

function toExternalUrl(db: DatabaseSharedInfo, url: string) {
    // we have to redirect to different node, let's find first member where selected database exists
    const firstNode = db.nodes[0];
    if (!firstNode) {
        return "";
    }
    return appUrl.toExternalUrl(firstNode.nodeUrl, url);
}
interface DatabaseTopologyProps {
    db: DatabaseSharedInfo;
}

function extractShardNumber(dbName: string) {
    const [, shard] = dbName.split("$", 2);
    return shard;
}

function DatabaseTopology(props: DatabaseTopologyProps) {
    const { db } = props;

    if (db.sharded) {
        const shardedDb = db as ShardedDatabaseSharedInfo;
        return (
            <div className="px-3 py-2">
                <NodeSet color="orchestrator" className="m-1">
                    <NodeSetLabel color="orchestrator" icon="orchestrator">
                        Orchestrators
                    </NodeSetLabel>
                    {db.nodes.map((node) => (
                        <NodeSetItem key={node.tag} icon={iconForNodeType(node.type)} color="node" title={node.type}>
                            {node.tag}
                        </NodeSetItem>
                    ))}
                </NodeSet>

                {shardedDb.shards.map((shard) => {
                    return (
                        <React.Fragment key={shard.name}>
                            <NodeSet color="shard" className="m-1">
                                <NodeSetLabel color="shard" icon="shard">
                                    #{extractShardNumber(shard.name)}
                                </NodeSetLabel>
                                {shard.nodes.map((node) => {
                                    return (
                                        <NodeSetItem
                                            key={node.tag}
                                            icon={iconForNodeType(node.type)}
                                            color="node"
                                            title={node.type}
                                        >
                                            {node.tag}
                                        </NodeSetItem>
                                    );
                                })}
                            </NodeSet>
                        </React.Fragment>
                    );
                })}
            </div>
        );
    } else {
        return (
            <div className="px-3 py-2">
                <NodeSet className="m-1">
                    <NodeSetLabel icon="database">Nodes</NodeSetLabel>
                    {db.nodes.map((node) => {
                        return (
                            <NodeSetItem
                                key={node.tag}
                                icon={iconForNodeType(node.type)}
                                color="node"
                                title={node.type}
                            >
                                {node.tag}
                            </NodeSetItem>
                        );
                    })}
                    {db.deletionInProgress.map((node) => {
                        return (
                            <NodeSetItem
                                key={"deletion-" + node}
                                icon="trash"
                                color="warning"
                                title="Deletion in progress"
                                extraIconClassName="pulse"
                            >
                                {node}
                            </NodeSetItem>
                        );
                    })}
                </NodeSet>
            </div>
        );
    }
}

function iconForNodeType(type: databaseGroupNodeType) {
    switch (type) {
        case "Member":
            return "dbgroup-member";
        case "Rehab":
            return "dbgroup-rehab";
        case "Promotable":
            return "dbgroup-promotable";
        default:
            assertUnreachable(type);
    }
}

export function DatabasePanel(props: DatabasePanelProps) {
    const { db, selected, toggleSelection } = props;
    const activeDatabase = useAppSelector(selectActiveDatabase);
    const { appUrl } = useAppUrls();
    const dispatch = useAppDispatch();

    const { reportEvent } = useEventsCollector();

    const { value: panelCollapsed, toggle: togglePanelCollapsed } = useBoolean(true);

    const [lockChanges, setLockChanges] = useState(false);

    const localDocumentsUrl = appUrl.forDocuments(null, db.name);
    const documentsUrl = db.currentNode.relevant ? localDocumentsUrl : toExternalUrl(db, localDocumentsUrl);

    const localManageGroupUrl = appUrl.forManageDatabaseGroup(db.name);
    const manageGroupUrl = db.currentNode.relevant ? localManageGroupUrl : toExternalUrl(db, localManageGroupUrl);

    const canNavigateToDatabase = !db.disabled;

    const onChangeLockMode = async (lockMode: DatabaseLockMode) => {
        if (db.lockMode === lockMode) {
            return;
        }

        const dbs = [db];

        reportEvent("databases", "set-lock-mode", lockMode);

        const can = await dispatch(confirmSetLockMode());

        if (can) {
            setLockChanges(true);
            try {
                await dispatch(changeDatabasesLockMode(dbs, lockMode));
            } finally {
                setLockChanges(false);
            }
        }
    };

    //TODO: enable / disable

    const onDelete = async () => {
        const confirmation = await dispatch(confirmDeleteDatabases([db]));

        if (confirmation.can) {
            await dispatch(deleteDatabases(confirmation.databases, confirmation.keepFiles));
        }
    };

    const onCompactDatabase = async () => {
        reportEvent("databases", "compact");
        dispatch(compactDatabase(db));
    };

    const onToggleDatabase = async () => {
        const enable = db.disabled;

        const confirmation = await dispatch(confirmToggleDatabases([db], enable));
        if (confirmation) {
            await dispatch(toggleDatabases([db], enable));
        }
    };

    return (
        <RichPanel
            className={classNames("flex-row", badgeClass(db), {
                active: activeDatabase === db.name,
                relevant: true,
            })}
        >
            <RichPanelStatus color={getStatusColor(db)}>{badgeText(db)}</RichPanelStatus>
            <div className="flex-grow-1">
                <div className="flex-grow-1">
                    <RichPanelHeader>
                        <RichPanelInfo>
                            <RichPanelSelect>
                                <Input type="checkbox" checked={selected} onChange={toggleSelection} />
                            </RichPanelSelect>

                            <RichPanelName>
                                {canNavigateToDatabase ? (
                                    <a
                                        href={documentsUrl}
                                        className={classNames(
                                            { "link-disabled": db.currentNode.isBeingDeleted },
                                            { "link-shard": db.sharded }
                                        )}
                                        target={db.currentNode.relevant ? undefined : "_blank"}
                                        title={db.name}
                                    >
                                        <i
                                            className={classNames(
                                                { "icon-database": !db.sharded },
                                                { "icon-sharding": db.sharded },
                                                { "icon-addon-home": db.currentNode.relevant }
                                            )}
                                        ></i>
                                        <span>{db.name}</span>
                                    </a>
                                ) : (
                                    <div className="name">
                                        <span title="Database is disabled">
                                            <small>
                                                <i
                                                    className={
                                                        db.currentNode.relevant
                                                            ? "icon-database icon-addon-home"
                                                            : "icon-database"
                                                    }
                                                ></i>
                                            </small>
                                            <span>{db.name}</span>
                                        </span>
                                    </div>
                                )}
                            </RichPanelName>

                            <div className="member">
                                {/* TODO: <!-- ko foreach: deletionInProgress -->
                            <div>
                                <div title="Deletion in progress" className="text-warning pulse">
                                    <small><i className="icon-trash" /><span data-bind="text: 'Node ' + $data" /></small>
                                </div>
                            </div>
                            <!-- /ko -->*/}
                            </div>
                        </RichPanelInfo>

                        <RichPanelActions>
                            <Button
                                href={manageGroupUrl}
                                title="Manage the Database Group"
                                target={db.currentNode.relevant ? undefined : "_blank"}
                                className="me-1"
                                disabled={!canNavigateToDatabase || db.currentNode.isBeingDeleted}
                            >
                                <i className="icon-dbgroup icon-addon-settings me-2" />
                                Manage group
                            </Button>

                            <UncontrolledDropdown className="me-1">
                                <ButtonGroup>
                                    <Button onClick={onToggleDatabase}>
                                        {db.disabled ? (
                                            <span>
                                                <i className="icon-database-cutout icon-addon-play2 me-1" /> Enable
                                            </span>
                                        ) : (
                                            <span>
                                                <i className="icon-database-cutout icon-addon-cancel me-1" /> Disable
                                            </span>
                                        )}
                                    </Button>
                                    <DropdownToggle caret></DropdownToggle>
                                </ButtonGroup>
                                <DropdownMenu end>
                                    {/* TODO details */}
                                    <DropdownItem style={{ display: "none" }}>
                                        <i className="icon-pause me-1" /> Pause indexing
                                    </DropdownItem>
                                    {/* TODO details */}
                                    <DropdownItem style={{ display: "none" }}>
                                        <i className="icon-stop me-1" /> Disable indexing
                                    </DropdownItem>
                                    <DropdownItem divider />
                                    <DropdownItem onClick={onCompactDatabase}>
                                        <i className="icon-compact me-1" /> Compact database
                                    </DropdownItem>
                                </DropdownMenu>
                            </UncontrolledDropdown>

                            {/* TODO
                            <Button className="me-1">
                                <i className="icon-refresh-stats" />
                            </Button> */}

                            {/* TODO <div className="btn-group">
                                <button className="btn btn-default" data-bind="click: $root.toggleDatabase, visible: $root.accessManager.canDisableEnableDatabase,
                                                                    css: { 'btn-spinner': inProgressAction },
                                                                    disable: isBeingDeleted() || inProgressAction()">
                                    <span data-bind="visible: inProgressAction(), text: inProgressAction()"/>
                                    <i className="icon-database-cutout icon-addon-play2"
    data-bind="visible: !inProgressAction() && disabled()"/>
                                    <span data-bind="visible: !inProgressAction() && disabled()">Enable</span>
                                    <i className="icon-database-cutout icon-addon-cancel"
    data-bind="visible: !inProgressAction() && !disabled()"/>
                                    <span data-bind="visible: !inProgressAction() && !disabled()">Disable</span>
                                </button>
                                <button type="button" className="btn btn-default dropdown-toggle" data-toggle="dropdown"
                                        aria-haspopup="true" aria-expanded="false"
                                        data-bind="disable: isBeingDeleted() || inProgressAction(), 
                                                       visible: online() && $root.isAdminAccessByDbName($data.name)">
                                    <span className="caret"/>
                                    <span className="sr-only">Toggle Dropdown</span>
                                </button>
                                <ul className="dropdown-menu dropdown-menu-right">
                                    <li data-bind="visible: online() && !indexingPaused() && !indexingDisabled()">
                                        <a href="#" data-bind="click: $root.togglePauseDatabaseIndexing">
                                            <i className="icon-pause"/> Pause indexing
                                        </a>
                                    </li>
                                    <li data-bind="visible: indexingPaused()">
                                        <a href="#" data-bind="click: $root.togglePauseDatabaseIndexing">
                                            <i className="icon-play"/> Resume indexing
                                        </a>
                                    </li>
                                    <li data-bind="visible: !indexingDisabled() && $root.accessManager.canDisableIndexing()">
                                        <a href="#" data-bind="click: $root.toggleDisableDatabaseIndexing">
                                            <i className="icon-cancel"/> Disable indexing
                                        </a>
                                    </li>
                                    <li data-bind="visible: indexingDisabled() && $root.accessManager.canDisableIndexing()">
                                        <a href="#" data-bind="click: $root.toggleDisableDatabaseIndexing">
                                            <i className="icon-play"/> Enable indexing
                                        </a>
                                    </li>
                                    <li className="divider"
    data-bind="visible: $root.createIsLocalDatabaseObservable(name) &&  $root.accessManager.canCompactDatabase()"/>
                                    <li data-bind="visible: $root.createIsLocalDatabaseObservable(name)() && $root.accessManager.canCompactDatabase()">
                                        <a data-bind="visible: disabled" title="The database is disabled"
                                           className="has-disable-reason disabled" data-placement="top">
                                            <i className="icon-compact"/> Compact database
                                        </a>
                                        <a href="#" data-bind="click: $root.compactDatabase, visible: !disabled()">
                                            <i className="icon-compact"/> Compact database
                                        </a>
                                    </li>
                                </ul>
                            </div>*/}
                            {/* TODO <button className="btn btn-success"
                                    data-bind="click: _.partial($root.updateDatabaseInfo, name), enable: canNavigateToDatabase(), disable: isBeingDeleted"
                                    title="Refresh database statistics">
                                <i className="icon-refresh-stats"/>
                            </button>*/}

                            <UncontrolledDropdown>
                                <ButtonGroup data-bind="visible: $root.accessManager.canDelete">
                                    <Button
                                        onClick={() => onDelete()}
                                        title={
                                            db.lockMode === "Unlock"
                                                ? "Remove database"
                                                : "Database cannot be deleted because of the set lock mode"
                                        }
                                        color={db.lockMode === "Unlock" && "danger"}
                                        disabled={db.lockMode !== "Unlock"}
                                        data-bind=" disable: isBeingDeleted() || lockMode() !== 'Unlock', 
                                        css: { 'btn-spinner': isBeingDeleted() || _.includes($root.spinners.localLockChanges(), name) }"
                                    >
                                        {lockChanges && <Spinner size="sm" />}
                                        {!lockChanges && db.lockMode === "Unlock" && <i className="icon-trash" />}
                                        {!lockChanges && db.lockMode === "PreventDeletesIgnore" && (
                                            <i className="icon-trash-cutout icon-addon-cancel" />
                                        )}
                                        {!lockChanges && db.lockMode === "PreventDeletesError" && (
                                            <i className="icon-trash-cutout icon-addon-exclamation" />
                                        )}
                                    </Button>
                                    <DropdownToggle caret color={db.lockMode === "Unlock" && "danger"}></DropdownToggle>
                                </ButtonGroup>
                                <DropdownMenu>
                                    <DropdownItem
                                        onClick={() => onChangeLockMode("Unlock")}
                                        title="Allow to delete database"
                                    >
                                        <i className="icon-trash-cutout icon-addon-check" /> Allow database delete
                                    </DropdownItem>
                                    <DropdownItem
                                        onClick={() => onChangeLockMode("PreventDeletesIgnore")}
                                        title="Prevent deletion of database. An error will not be thrown if an app attempts to delete the database."
                                    >
                                        <i className="icon-trash-cutout icon-addon-cancel" /> Prevent database delete
                                    </DropdownItem>
                                    <DropdownItem
                                        onClick={() => onChangeLockMode("PreventDeletesError")}
                                        title="Prevent deletion of database. An error will be thrown if an app attempts to delete the database."
                                    >
                                        <i className="icon-trash-cutout icon-addon-exclamation" /> Prevent database
                                        delete (Error)
                                    </DropdownItem>
                                </DropdownMenu>
                            </UncontrolledDropdown>
                            <Button
                                color="secondary"
                                onClick={togglePanelCollapsed}
                                title="Toggle distribution details"
                            >
                                <i className={panelCollapsed ? "icon-expand-vertical" : "icon-collapse-vertical"} />
                            </Button>
                        </RichPanelActions>
                    </RichPanelHeader>

                    <ValidDatabasePropertiesPanel db={db} />

                    <Collapse isOpen={!panelCollapsed}>
                        <DatabaseDistribution db={db} />
                    </Collapse>
                    <Collapse isOpen={panelCollapsed}>
                        <DatabaseTopology db={db} />
                    </Collapse>
                </div>
            </div>
        </RichPanel>
    );
}

/* TODO

<script type="text/html" id="invalid-database-properties-template">
    <div class="padding">
        <div class="addons-container flex-wrap">
            <div class="text-danger flex-grow">
                <small>
                    <i class="icon-exclamation"></i>
                    <span data-bind="text: loadError"></span>
                </small>
            </div>
        </div>
    </div>
</script>
 */
