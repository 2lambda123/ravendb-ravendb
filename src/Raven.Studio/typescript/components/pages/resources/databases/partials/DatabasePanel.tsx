﻿import React, { MouseEvent, useState } from "react";
import { DatabaseLocalInfo, DatabaseSharedInfo } from "components/models/databases";
import classNames from "classnames";
import { useAppUrls } from "hooks/useAppUrls";
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
import { useAppDispatch, useAppSelector } from "components/store";
import { useEventsCollector } from "hooks/useEventsCollector";
import useBoolean from "hooks/useBoolean";
import { DatabaseDistribution } from "components/pages/resources/databases/partials/DatabaseDistribution";
import { ValidDatabasePropertiesPanel } from "components/pages/resources/databases/partials/ValidDatabasePropertiesPanel";
import { locationAwareLoadableData } from "components/models/common";
import { useAccessManager } from "hooks/useAccessManager";
import DatabaseUtils from "components/utils/DatabaseUtils";
import { selectEffectiveDatabaseAccessLevel } from "components/common/shell/accessManagerSlice";
import genUtils from "common/generalUtils";
import databasesManager from "common/shell/databasesManager";
import { AccessIcon } from "components/pages/resources/databases/partials/AccessIcon";
import { DatabaseTopology } from "components/pages/resources/databases/partials/DatabaseTopology";
import DatabaseLockMode = Raven.Client.ServerWide.DatabaseLockMode;
import {
    selectActiveDatabase,
    selectDatabaseByName,
    selectDatabaseState,
} from "components/common/shell/databaseSliceSelectors";
import {
    changeDatabasesLockMode,
    compactDatabase,
    confirmDeleteDatabases,
    confirmSetLockMode,
    confirmToggleDatabases,
    confirmToggleIndexing,
    confirmTogglePauseIndexing,
    deleteDatabases,
    reloadDatabaseDetails,
    toggleDatabases,
    toggleIndexing,
    togglePauseIndexing,
} from "components/common/shell/databaseSliceActions";
import { Icon } from "components/common/Icon";

interface DatabasePanelProps {
    databaseName: string;
    selected: boolean;
    toggleSelection: () => void;
}

function getStatusColor(db: DatabaseSharedInfo, localInfo: locationAwareLoadableData<DatabaseLocalInfo>[]) {
    const state = DatabaseUtils.getDatabaseState(db, localInfo);
    switch (state) {
        case "Loading":
            return "secondary";
        case "Error":
            return "danger";
        case "Offline":
            return "secondary";
        case "Disabled":
            return "warning";
        default:
            return "success";
    }
}

function badgeText(db: DatabaseSharedInfo, localInfo: locationAwareLoadableData<DatabaseLocalInfo>[]) {
    const state = DatabaseUtils.getDatabaseState(db, localInfo);
    if (state === "Loading") {
        return "Loading...";
    }

    return state;
}

export function DatabasePanel(props: DatabasePanelProps) {
    const { databaseName, selected, toggleSelection } = props;
    const db = useAppSelector(selectDatabaseByName(databaseName));
    const activeDatabase = useAppSelector(selectActiveDatabase);
    const dbState = useAppSelector(selectDatabaseState(db.name));
    const { appUrl } = useAppUrls();
    const dispatch = useAppDispatch();

    //TODO: show commands errors!

    const dbAccess: databaseAccessLevel = useAppSelector(selectEffectiveDatabaseAccessLevel(db.name));

    const { reportEvent } = useEventsCollector();

    const { value: panelCollapsed, toggle: togglePanelCollapsed } = useBoolean(true);

    const [lockChanges, setLockChanges] = useState(false);

    const [inProgressAction, setInProgressAction] = useState<string>(null);

    const localDocumentsUrl = appUrl.forDocuments(null, db.name);
    const documentsUrl = db.currentNode.relevant
        ? localDocumentsUrl
        : appUrl.toExternalDatabaseUrl(db, localDocumentsUrl);

    const localManageGroupUrl = appUrl.forManageDatabaseGroup(db.name);
    const manageGroupUrl = db.currentNode.relevant
        ? localManageGroupUrl
        : appUrl.toExternalDatabaseUrl(db, localManageGroupUrl);

    const { isOperatorOrAbove, isSecuredServer, isAdminAccessOrAbove } = useAccessManager();

    const canNavigateToDatabase = !db.disabled;

    const indexingDisabled = dbState.some((x) => x.status === "success" && x.data.indexingStatus === "Disabled");
    const canPauseAnyIndexing = dbState.some((x) => x.status === "success" && x.data.indexingStatus === "Running");
    const canResumeAnyPausedIndexing = dbState.some(
        (x) => x.status === "success" && x.data?.indexingStatus === "Paused"
    );

    const canDisableIndexing = isOperatorOrAbove() && !indexingDisabled;
    const canEnableIndexing = isOperatorOrAbove() && indexingDisabled;

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

    const onTogglePauseIndexing = async (pause: boolean) => {
        reportEvent("databases", "pause-indexing");

        const confirmation = await dispatch(confirmTogglePauseIndexing(db, pause));

        if (confirmation.can) {
            try {
                setInProgressAction(pause ? "Pausing indexing" : "Resume indexing");
                await dispatch(togglePauseIndexing(db, pause, confirmation.locations));
            } finally {
                setInProgressAction(null);
            }
        }
    };

    const onToggleDisableIndexing = async (disable: boolean) => {
        reportEvent("databases", "toggle-indexing");

        const confirmation = await dispatch(confirmToggleIndexing(db, disable));

        if (confirmation.can) {
            try {
                setInProgressAction(disable ? "Disabling indexing" : "Enabling indexing");
                await dispatch(toggleIndexing(db, disable));
            } finally {
                setInProgressAction(null);
            }
        }
    };

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

    const onHeaderClicked = async (db: DatabaseSharedInfo, e: MouseEvent<HTMLElement>) => {
        if (genUtils.canConsumeDelegatedEvent(e)) {
            if (!db || db.disabled || !db.currentNode.relevant) {
                return true;
            }

            const manager = databasesManager.default;

            const databaseToActivate = manager.getDatabaseByName(db.name);

            if (databaseToActivate) {
                try {
                    await manager.activate(databaseToActivate);
                    await manager.updateDatabaseInfo(databaseToActivate, db.name);
                } finally {
                    await dispatch(reloadDatabaseDetails(db.name));
                }
            }
        }
    };

    return (
        <RichPanel
            hover={db.currentNode.relevant}
            className={classNames("flex-row", "with-status", {
                active: activeDatabase === db.name,
                relevant: true,
            })}
        >
            <RichPanelStatus color={getStatusColor(db, dbState)}>{badgeText(db, dbState)}</RichPanelStatus>
            <div className="flex-grow-1">
                <div className="flex-grow-1">
                    <RichPanelHeader onClick={(e) => onHeaderClicked(db, e)}>
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
                                        <Icon
                                            icon={db.sharded ? "sharding" : "database"}
                                            addon={db.currentNode.relevant ? "home" : null}
                                            className="me-2"
                                        />
                                        {db.name}
                                    </a>
                                ) : (
                                    <span title="Database is disabled">
                                        <Icon icon="database" addon={db.currentNode.relevant ? "home" : null} />
                                        {db.name}
                                    </span>
                                )}
                            </RichPanelName>
                            <div className="text-muted">
                                {dbAccess && isSecuredServer() && <AccessIcon dbAccess={dbAccess} />}
                            </div>
                        </RichPanelInfo>

                        <RichPanelActions>
                            <Button
                                href={manageGroupUrl}
                                title="Manage the Database Group"
                                target={db.currentNode.relevant ? undefined : "_blank"}
                                className="ms-1"
                                disabled={!canNavigateToDatabase || db.currentNode.isBeingDeleted}
                            >
                                <Icon icon="dbgroup" addon="settings" className="me-2" />
                                Manage group
                            </Button>

                            {isAdminAccessOrAbove(db) && (
                                <UncontrolledDropdown className="ms-1">
                                    <ButtonGroup>
                                        {isOperatorOrAbove() && (
                                            <Button onClick={onToggleDatabase}>
                                                {db.disabled ? (
                                                    <span>
                                                        <Icon icon="database" addon="play2" className="me-1" /> Enable
                                                    </span>
                                                ) : (
                                                    <span>
                                                        <Icon icon="database" addon="cancel" className="me-1" /> Disable
                                                    </span>
                                                )}
                                            </Button>
                                        )}
                                        <DropdownToggle caret></DropdownToggle>
                                    </ButtonGroup>

                                    <DropdownMenu end container="dropdownContainer">
                                        {canPauseAnyIndexing && (
                                            <DropdownItem onClick={() => onTogglePauseIndexing(true)}>
                                                <Icon icon="pause" className="me-1" /> Pause indexing
                                            </DropdownItem>
                                        )}
                                        {canResumeAnyPausedIndexing && (
                                            <DropdownItem onClick={() => onTogglePauseIndexing(false)}>
                                                <Icon icon="play" className="me-1" /> Resume indexing
                                            </DropdownItem>
                                        )}
                                        {canDisableIndexing && (
                                            <DropdownItem onClick={() => onToggleDisableIndexing(true)}>
                                                <Icon icon="stop" className="me-1" /> Disable indexing
                                            </DropdownItem>
                                        )}
                                        {canEnableIndexing && (
                                            <DropdownItem onClick={() => onToggleDisableIndexing(false)}>
                                                <Icon icon="play" className="me-1" /> Enable indexing
                                            </DropdownItem>
                                        )}
                                        {isOperatorOrAbove() && (
                                            <>
                                                <DropdownItem divider />
                                                <DropdownItem onClick={onCompactDatabase}>
                                                    <Icon icon="compact" className="me-1" /> Compact database
                                                </DropdownItem>
                                            </>
                                        )}
                                    </DropdownMenu>
                                </UncontrolledDropdown>
                            )}

                            {isOperatorOrAbove() && (
                                <UncontrolledDropdown className="ms-1">
                                    <ButtonGroup>
                                        <Button
                                            onClick={() => onDelete()}
                                            title={
                                                db.lockMode === "Unlock"
                                                    ? "Remove database"
                                                    : "Database cannot be deleted because of the set lock mode"
                                            }
                                            color={db.lockMode === "Unlock" && "danger"}
                                            disabled={db.lockMode !== "Unlock"}
                                        >
                                            {lockChanges && <Spinner size="sm" />}
                                            {!lockChanges && db.lockMode === "Unlock" && <Icon icon="trash" />}
                                            {!lockChanges && db.lockMode === "PreventDeletesIgnore" && (
                                                <Icon icon="trash" addon="cancel" />
                                            )}
                                            {!lockChanges && db.lockMode === "PreventDeletesError" && (
                                                <Icon icon="trash" addon="exclamation" />
                                            )}
                                        </Button>
                                        <DropdownToggle
                                            caret
                                            color={db.lockMode === "Unlock" && "danger"}
                                        ></DropdownToggle>
                                    </ButtonGroup>

                                    <DropdownMenu container="dropdownContainer">
                                        <DropdownItem
                                            onClick={() => onChangeLockMode("Unlock")}
                                            title="Allow to delete database"
                                        >
                                            <Icon icon="trash" addon="check" className="me-1" /> Allow database delete
                                        </DropdownItem>
                                        <DropdownItem
                                            onClick={() => onChangeLockMode("PreventDeletesIgnore")}
                                            title="Prevent deletion of database. An error will not be thrown if an app attempts to delete the database."
                                        >
                                            <Icon icon="trash" addon="cancel" className="me-1" /> Prevent database
                                            delete
                                        </DropdownItem>
                                        <DropdownItem
                                            onClick={() => onChangeLockMode("PreventDeletesError")}
                                            title="Prevent deletion of database. An error will be thrown if an app attempts to delete the database."
                                        >
                                            <Icon icon="trash" addon="exclamation" className="me-1" /> Prevent database
                                            delete (Error)
                                        </DropdownItem>
                                    </DropdownMenu>
                                </UncontrolledDropdown>
                            )}

                            <Button
                                color="secondary"
                                onClick={togglePanelCollapsed}
                                title="Toggle distribution details"
                                className="ms-1"
                            >
                                <Icon icon={panelCollapsed ? "arrow-down" : "arrow-up"} />
                            </Button>
                        </RichPanelActions>
                    </RichPanelHeader>

                    <ValidDatabasePropertiesPanel db={db} />
                    <div className="px-3 pb-2">
                        <Collapse isOpen={!panelCollapsed}>
                            <DatabaseDistribution db={db} />
                        </Collapse>
                        <Collapse isOpen={panelCollapsed}>
                            <DatabaseTopology db={db} />
                        </Collapse>
                    </div>
                </div>
            </div>
        </RichPanel>
    );
}
