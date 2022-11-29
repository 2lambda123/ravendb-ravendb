﻿import React, { ForwardedRef, forwardRef, MouseEvent, useState } from "react";
import classNames from "classnames";
import IndexPriority = Raven.Client.Documents.Indexes.IndexPriority;
import { IndexSharedInfo } from "../../../../models/indexes";
import IndexLockMode = Raven.Client.Documents.Indexes.IndexLockMode;
import { useAppUrls } from "hooks/useAppUrls";
import IndexUtils from "../../../../utils/IndexUtils";
import { useEventsCollector } from "hooks/useEventsCollector";
import indexStalenessReasons from "viewmodels/database/indexes/indexStalenessReasons";
import database = require("models/resources/database");
import app from "durandal/app";
import { useAccessManager } from "hooks/useAccessManager";
import IndexRunningStatus = Raven.Client.Documents.Indexes.IndexRunningStatus;
import { UncontrolledTooltip } from "../../../../common/UncontrolledTooltip";
import { IndexDistribution, IndexProgress } from "./IndexDistribution";
import { IndexProgressTooltip } from "./IndexProgressTooltip";
import IndexSourceType = Raven.Client.Documents.Indexes.IndexSourceType;
import {
    RichPanel,
    RichPanelDetailItem,
    RichPanelDetails,
    RichPanelHeader,
    RichPanelSelect,
} from "../../../../common/RichPanel";
import { Checkbox } from "../../../../common/Checkbox";
import {
    Button,
    ButtonGroup,
    Dropdown,
    DropdownItem,
    DropdownMenu,
    DropdownToggle,
    FormGroup,
    Input,
    Spinner,
    UncontrolledDropdown,
} from "reactstrap";

interface IndexPanelProps {
    database: database;
    index: IndexSharedInfo;
    globalIndexingStatus: IndexRunningStatus;
    setPriority: (priority: IndexPriority) => Promise<void>;
    setLockMode: (lockMode: IndexLockMode) => Promise<void>;
    enableIndexing: () => Promise<void>;
    disableIndexing: () => Promise<void>;
    pauseIndexing: () => Promise<void>;
    resumeIndexing: () => Promise<void>;
    deleteIndex: () => Promise<void>;
    resetIndex: () => Promise<void>;
    openFaulty: (location: databaseLocationSpecifier) => Promise<void>;
    selected: boolean;
    hasReplacement?: boolean;
    toggleSelection: () => void;
    ref?: any;
}

export const IndexPanel = forwardRef(IndexPanelInternal);

export function IndexPanelInternal(props: IndexPanelProps, ref: ForwardedRef<HTMLDivElement>) {
    const { index, selected, toggleSelection, database, hasReplacement, globalIndexingStatus } = props;

    const { canReadWriteDatabase, canReadOnlyDatabase } = useAccessManager();

    const isReplacement = IndexUtils.isSideBySide(index);
    const isFaulty = IndexUtils.hasAnyFaultyNode(index);
    const inlineDetails = index.nodesInfo.length === 1;

    const eventsCollector = useEventsCollector();

    const [updatingLocalPriority, setUpdatingLocalPriority] = useState(false);
    const [updatingLockMode, setUpdatingLockMode] = useState(false);
    const [updatingState, setUpdatingState] = useState(false);

    const setPriority = async (e: MouseEvent, priority: IndexPriority) => {
        e.preventDefault();
        if (priority !== index.priority) {
            setUpdatingLocalPriority(true);
            try {
                await props.setPriority(priority);
            } finally {
                setUpdatingLocalPriority(false);
            }
        }
    };

    const setLockMode = async (e: MouseEvent, lockMode: IndexLockMode) => {
        e.preventDefault();
        if (lockMode !== index.lockMode) {
            setUpdatingLockMode(true);
            try {
                await props.setLockMode(lockMode);
            } finally {
                setUpdatingLockMode(false);
            }
        }
    };

    const enableIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "set-state", "enabled");
        setUpdatingState(true);
        try {
            await props.enableIndexing();
        } finally {
            setUpdatingState(false);
        }
    };

    const disableIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "set-state", "disabled");
        setUpdatingState(true);
        try {
            await props.disableIndexing();
        } finally {
            setUpdatingState(false);
        }
    };

    const pauseIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "pause");
        setUpdatingState(true);
        try {
            await props.pauseIndexing();
        } finally {
            setUpdatingState(false);
        }
    };

    const resumeIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "pause");
        setUpdatingState(true);
        try {
            await props.resumeIndexing();
        } finally {
            setUpdatingState(false);
        }
    };

    const deleteIndex = async (e: MouseEvent) => {
        e.preventDefault();
        return props.deleteIndex();
    };

    const showStaleReasons = (index: IndexSharedInfo, location: databaseLocationSpecifier) => {
        const view = new indexStalenessReasons(database, index.name, location);
        eventsCollector.reportEvent("indexes", "show-stale-reasons");
        app.showBootstrapDialog(view);
    };

    const openFaulty = async (location: databaseLocationSpecifier) => {
        await props.openFaulty(location);
    };

    const resetIndex = () => props.resetIndex();

    const { forCurrentDatabase: urls } = useAppUrls();
    const queryUrl = urls.query(index.name)();
    const termsUrl = urls.terms(index.name)();
    const editUrl = urls.editIndex(index.name)();

    const [reduceOutputId] = useState(() => _.uniqueId("reduce-output-id"));

    return (
        <>
            <RichPanel className={classNames({ "index-sidebyside": hasReplacement || isReplacement })} innerRef={ref}>
                <RichPanelHeader id={indexUniqueId(index)}>
                    <RichPanelSelect>
                        {canReadWriteDatabase(database) && (
                            <FormGroup check className="form-check-secondary">
                                <Input type="checkbox" bsSize="lg" onClick={toggleSelection} checked={selected} />
                            </FormGroup>
                        )}
                    </RichPanelSelect>

                    <h3 className="index-name flex-grow">
                        <a href={editUrl} title={index.name}>
                            {index.name}
                        </a>
                    </h3>

                    {!IndexUtils.hasAnyFaultyNode(index) && (
                        <div className="flex-horizontal">
                            {!IndexUtils.isSideBySide(index) && (
                                <UncontrolledDropdown className="margin-right">
                                    <DropdownToggle outline color="info" disabled={!canReadWriteDatabase(database)}>
                                        {updatingLocalPriority && <Spinner size="sm" className="margin-right-xs" />}
                                        {index.priority === "Normal" && (
                                            <span>
                                                <i className="icon-check" />
                                                <span>Normal Priority</span>
                                            </span>
                                        )}
                                        {index.priority === "Low" && (
                                            <span>
                                                <i className="icon-coffee" />
                                                <span>Low Priority</span>
                                            </span>
                                        )}
                                        {index.priority === "High" && (
                                            <span>
                                                <i className="icon-force" />
                                                <span>High Priority</span>
                                            </span>
                                        )}
                                    </DropdownToggle>

                                    <DropdownMenu>
                                        <DropdownItem onClick={(e) => setPriority(e, "Low")} title="Low">
                                            <i className="icon-coffee" /> <span>Low Priority</span>
                                        </DropdownItem>
                                        <DropdownItem onClick={(e) => setPriority(e, "Normal")} title="Normal">
                                            <i className="icon-check" /> <span>Normal Priority</span>
                                        </DropdownItem>
                                        <DropdownItem onClick={(e) => setPriority(e, "High")} title="High">
                                            <i className="icon-force" /> <span>High Priority</span>
                                        </DropdownItem>
                                    </DropdownMenu>
                                </UncontrolledDropdown>
                            )}

                            {index.type !== "AutoMap" &&
                                index.type !== "AutoMapReduce" &&
                                !IndexUtils.isSideBySide(index) && (
                                    <UncontrolledDropdown className="margin-right">
                                        <DropdownToggle outline color="info" disabled={!canReadWriteDatabase(database)}>
                                            {updatingLockMode && <Spinner size="sm" className="margin-right-xs" />}
                                            {index.lockMode === "Unlock" && (
                                                <span>
                                                    <i className="icon-unlock" />
                                                    <span>Unlocked</span>
                                                </span>
                                            )}
                                            {index.lockMode === "LockedIgnore" && (
                                                <span>
                                                    <i className="icon-lock" />
                                                    <span>Locked</span>
                                                </span>
                                            )}
                                            {index.lockMode === "LockedError" && (
                                                <span>
                                                    <i className="icon-lock-error" />
                                                    <span>Locked (Error)</span>
                                                </span>
                                            )}
                                        </DropdownToggle>

                                        <DropdownMenu>
                                            <DropdownItem
                                                onClick={(e) => setLockMode(e, "Unlock")}
                                                title="Unlocked: The index is unlocked for changes; apps can modify it, e.g. via IndexCreation.CreateIndexes()."
                                            >
                                                <i className="icon-unlock" /> <span>Unlock</span>
                                            </DropdownItem>
                                            <DropdownItem divider />
                                            <DropdownItem
                                                onClick={(e) => setLockMode(e, "LockedIgnore")}
                                                title="Locked: The index is locked for changes; apps cannot modify it. Programmatic attempts to modify the index will be ignored."
                                            >
                                                <i className="icon-lock" /> <span>Lock</span>
                                            </DropdownItem>
                                            <DropdownItem
                                                onClick={(e) => setLockMode(e, "LockedError")}
                                                title="Locked + Error: The index is locked for changes; apps cannot modify it. An error will be thrown if an app attempts to modify it."
                                            >
                                                <i className="icon-lock-error" /> <span>Lock (Error)</span>
                                            </DropdownItem>
                                        </DropdownMenu>
                                    </UncontrolledDropdown>
                                )}
                        </div>
                    )}

                    <div className="actions">
                        <div className="btn-toolbar pull-right-sm" role="toolbar">
                            {!IndexUtils.hasAnyFaultyNode(index) && (
                                <UncontrolledDropdown>
                                    <DropdownToggle
                                        data-bind="css: { 'btn-spinner': _.includes($root.spinners.localState(), name) },
                                           enable: $root.globalIndexingStatus() === 'Running'  && !_.includes($root.spinners.localState(), name),
                                           requiredAccess: 'DatabaseReadWrite', requiredAccessOptions: { strategy: 'disable' }"
                                    >
                                        {updatingState && <Spinner size="sm" className="margin-right-xs" />}
                                        <span>Set State</span>
                                    </DropdownToggle>

                                    <DropdownMenu>
                                        <DropdownItem onClick={enableIndexing} title="Enable indexing">
                                            <i className="icon-play" /> <span>Enable indexing</span>
                                        </DropdownItem>
                                        <DropdownItem onClick={disableIndexing} title="Disable indexing">
                                            <i className="icon-cancel text-danger" /> <span>Disable indexing</span>
                                        </DropdownItem>
                                        <DropdownItem divider />
                                        <DropdownItem onClick={resumeIndexing} title="Resume indexing">
                                            <i className="icon-play" /> <span>Resume indexing</span>
                                        </DropdownItem>
                                        <DropdownItem onClick={pauseIndexing} title="Pause until restart">
                                            <i className="icon-pause text-warning" />{" "}
                                            <span>Pause indexing until restart</span>
                                        </DropdownItem>
                                    </DropdownMenu>
                                </UncontrolledDropdown>
                            )}

                            {!IndexUtils.hasAnyFaultyNode(index) && (
                                <ButtonGroup className="margin-left-xxs">
                                    <Button variant="secondary" href={queryUrl}>
                                        <i className="icon-search" />
                                        <span>Query</span>
                                    </Button>

                                    <UncontrolledDropdown>
                                        <DropdownToggle className="dropdown-toggle" />

                                        <DropdownMenu end>
                                            <DropdownItem href={termsUrl}>
                                                {" "}
                                                <i className="icon-terms" /> Terms{" "}
                                            </DropdownItem>
                                        </DropdownMenu>
                                    </UncontrolledDropdown>
                                </ButtonGroup>
                            )}

                            <ButtonGroup className="margin-left-xxs">
                                {!IndexUtils.isAutoIndex(index) && !canReadOnlyDatabase(database) && (
                                    <Button href={editUrl} title="Edit index">
                                        <i className="icon-edit" />
                                    </Button>
                                )}
                                {(IndexUtils.isAutoIndex(index) || canReadOnlyDatabase(database)) && (
                                    <Button href={editUrl} title="View index">
                                        <i className="icon-preview" />
                                    </Button>
                                )}
                            </ButtonGroup>

                            {inlineDetails && isFaulty && (
                                <Button
                                    onClick={() => openFaulty(index.nodesInfo[0].location)}
                                    className="margin-left-xxs"
                                >
                                    Open faulty index
                                </Button>
                            )}

                            {canReadWriteDatabase(database) && (
                                <ButtonGroup className="margin-left-xxs">
                                    <Button color="warning" onClick={resetIndex} title="Reset index (rebuild)">
                                        <i className="icon-reset-index" />
                                    </Button>
                                    <Button color="danger" onClick={deleteIndex} title="Delete the index">
                                        <i className="icon-trash" />
                                    </Button>
                                </ButtonGroup>
                            )}
                        </div>
                    </div>
                </RichPanelHeader>
                <RichPanelDetails>
                    {(index.reduceOutputCollectionName || index.patternForReferencesToReduceOutputCollection) && (
                        <RichPanelDetailItem>
                            <div className="index-type-icon" id={reduceOutputId}>
                                {index.reduceOutputCollectionName &&
                                    !index.patternForReferencesToReduceOutputCollection && (
                                        <span>
                                            <i className="icon-output-collection" />
                                        </span>
                                    )}
                                {index.patternForReferencesToReduceOutputCollection && (
                                    <span>
                                        <i className="icon-reference-pattern" />
                                    </span>
                                )}
                                <UncontrolledTooltip target={reduceOutputId} animation placement="right">
                                    <>
                                        {index.reduceOutputCollectionName && (
                                            <span>
                                                Reduce Results are saved in Collection:
                                                <br />
                                                <strong>{index.reduceOutputCollectionName}</strong>
                                            </span>
                                        )}
                                        {index.collectionNameForReferenceDocuments && (
                                            <span>
                                                <br />
                                                Referencing Documents are saved in Collection:
                                                <br />
                                                <strong>{index.collectionNameForReferenceDocuments}</strong>
                                            </span>
                                        )}
                                        {!index.collectionNameForReferenceDocuments &&
                                            index.patternForReferencesToReduceOutputCollection && (
                                                <span>
                                                    <br />
                                                    Referencing Documents are saved in Collection:
                                                    <br />
                                                    <strong>{index.reduceOutputCollectionName}/References</strong>
                                                </span>
                                            )}
                                    </>
                                </UncontrolledTooltip>
                            </div>
                        </RichPanelDetailItem>
                    )}

                    {(hasReplacement || isReplacement) && (
                        <RichPanelDetailItem>
                            {hasReplacement && (
                                <span className="margin-left margin-left-sm">
                                    <span className="label label-warning">OLD</span>
                                </span>
                            )}
                            {isReplacement && (
                                <span className="margin-left margin-left-sm">
                                    <span className="label label-warning">NEW</span>
                                </span>
                            )}
                        </RichPanelDetailItem>
                    )}
                    <RichPanelDetailItem className={isFaulty ? "text-danger" : ""}>
                        <i className={IndexUtils.indexTypeIcon(index.type)} />
                        {IndexUtils.formatType(index.type)}
                    </RichPanelDetailItem>
                    <IndexSourceTypeComponent sourceType={index.sourceType} />
                    <RichPanelDetailItem>
                        <i className="icon-search" />
                        {index.searchEngine}
                    </RichPanelDetailItem>
                    {inlineDetails && !isFaulty && (
                        <InlineDetails
                            index={index}
                            globalIndexingStatus={globalIndexingStatus}
                            showStaleReason={(location) => showStaleReasons(index, location)}
                        />
                    )}
                </RichPanelDetails>
                {index.nodesInfo.length > 1 && (
                    <IndexDistribution
                        index={index}
                        globalIndexingStatus={globalIndexingStatus}
                        showStaleReason={(location) => showStaleReasons(index, location)}
                        openFaulty={openFaulty}
                    />
                )}
            </RichPanel>
        </>
    );
}

function IndexSourceTypeComponent(props: { sourceType: IndexSourceType }) {
    const { sourceType } = props;

    return (
        <RichPanelDetailItem>
            {sourceType === "Counters" && (
                <>
                    <i className="icon-new-counter" title="Index source: Counters" />
                    Counters
                </>
            )}
            {sourceType === "TimeSeries" && (
                <>
                    <i className="icon-timeseries" title="Index source: Time Series" />
                    Time Series
                </>
            )}
            {sourceType === "Documents" && (
                <>
                    <i className="icon-documents" title="Index source: Documents" />
                    Documents
                </>
            )}
        </RichPanelDetailItem>
    );
}

interface InlineDetailsProps {
    index: IndexSharedInfo;
    globalIndexingStatus: IndexRunningStatus;
    showStaleReason: (location: databaseLocationSpecifier) => void;
}

function InlineDetails(props: InlineDetailsProps) {
    const { index, globalIndexingStatus, showStaleReason } = props;
    const nodeInfo = index.nodesInfo[0];

    const [indexId] = useState(() => _.uniqueId("index-inline-details-id"));

    return (
        <>
            <RichPanelDetailItem>
                <i className="icon-list" />
                Entries
                <div className="value">{nodeInfo.details.entriesCount.toLocaleString()}</div>
            </RichPanelDetailItem>
            <RichPanelDetailItem
                className={classNames("index-detail-item", {
                    "text-danger": nodeInfo.details.errorCount > 0,
                })}
            >
                <i className="icon-warning" />
                Errors
                <div className="value">{nodeInfo.details.errorCount.toLocaleString()}</div>
            </RichPanelDetailItem>
            <RichPanelDetailItem id={indexId}>
                <IndexProgress inline nodeInfo={nodeInfo} />
            </RichPanelDetailItem>
            <IndexProgressTooltip
                target={indexId}
                nodeInfo={index.nodesInfo[0]}
                index={index}
                globalIndexingStatus={globalIndexingStatus}
                showStaleReason={showStaleReason}
            />
        </>
    );
}

const indexUniqueId = (index: IndexSharedInfo) => "index_" + index.name;
