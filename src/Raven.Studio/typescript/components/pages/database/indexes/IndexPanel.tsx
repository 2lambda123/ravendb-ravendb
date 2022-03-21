﻿import React, { MouseEvent, useState } from "react";
import classNames from "classnames";
import IndexPriority = Raven.Client.Documents.Indexes.IndexPriority;
import { IndexNodeInfo, IndexNodeInfoDetails, IndexSharedInfo } from "../../../models/indexes";
import IndexLockMode = Raven.Client.Documents.Indexes.IndexLockMode;
import { useAppUrls } from "../../../hooks/useAppUrls";
import IndexUtils from "../../../utils/IndexUtils";
import { useEventsCollector } from "../../../hooks/useEventsCollector";

interface IndexPanelProps {
    index: IndexSharedInfo;
    setPriority: (priority: IndexPriority) => Promise<void>;
    setLockMode: (lockMode: IndexLockMode) => Promise<void>;
    enableIndexing: () => Promise<void>;
    disableIndexing: () => Promise<void>;
    pauseIndexing: () => Promise<void>;
    resumeIndexing: () => Promise<void>;
    deleteIndex: () => Promise<void>;
    resetIndex: () => Promise<void>;
    selected: boolean;
    toggleSelection: () => void;
}

export function IndexPanel(props: IndexPanelProps) {
    const { index, selected, toggleSelection } = props;
    
    const eventsCollector = useEventsCollector();

    const [updatingLocalPriority, setUpdatingLocalPriority] = useState(false);
    const [updatingLockMode, setUpdatingLockMode] = useState(false);
    const [updatingState, setUpdatingState] = useState(false); //TODO: bind me!
    
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
    }
    
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
    }
    
    const enableIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "set-state", "enabled");
        setUpdatingState(true);
        try {
            await props.enableIndexing();
        } finally {
            setUpdatingState(false);
        }
    }
    
    const disableIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "set-state", "disabled");
        setUpdatingState(true);
        try {
            await props.disableIndexing();
        } finally {
            setUpdatingState(false);
        }
    }

    const pauseIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "pause");
        setUpdatingState(true);
        try {
            await props.pauseIndexing();
        } finally {
            setUpdatingState(false);
        }
    }

    const resumeIndexing = async (e: MouseEvent) => {
        e.preventDefault();
        eventsCollector.reportEvent("indexes", "pause");
        setUpdatingState(true);
        try {
            await props.resumeIndexing();
        } finally {
            setUpdatingState(false);
        }
    }
    
    const deleteIndex = async (e: MouseEvent) => {
        e.preventDefault();
        return props.deleteIndex();
    }
    
    const resetIndex = () => props.resetIndex();
    
    const urls = useAppUrls();
    const queryUrl = urls.query(index.name)();
    const termsUrl = urls.terms(index.name)();
    const editUrl = urls.editIndex(index.name)();
    
    return (
        <div className="sidebyside-indexes">
            <div className="panel panel-state panel-hover index" data-bind="css: { 'has-replacement': replacement }">
                <div className="padding padding-sm js-index-template" id={indexUniqueId(index)}>
                    <div className="row">
                        <div className="col-xs-12 col-sm-6 col-xl-4 info-container">
                            <div className="flex-horizontal">
                                <div className="checkbox" data-bind="requiredAccess: 'DatabaseReadWrite'">
                                    <input type="checkbox" className="styled" checked={selected} onChange={toggleSelection} />
                                    <label/>
                                </div>
                                <h3 className="index-name flex-grow">
                                    <a href={editUrl} title={index.name}>{index.name}</a>
                                </h3>
                                { index.sourceType === "Counters" && (
                                    <i className="icon-new-counter" title="Index source: Counters" />
                                )}
                                { index.sourceType === "TimeSeries" && (
                                    <i className="icon-timeseries" title="Index source: Time Series" />
                                )}
                                { index.sourceType === "Documents" && (
                                    <i className="icon-documents" title="Index source: Documents" />
                                )}
                            </div>
                            <div className="flex-horizontal clear-left index-info nospacing">
                                <div className="index-type-icon" data-placement="right" data-toggle="tooltip"
                                     data-animation="true" data-html="true"
                                     data-bind="tooltipText: mapReduceIndexInfoTooltip">
                                    { index.reduceOutputCollectionName && !index.patternForReferencesToReduceOutputCollection && (
                                        <span>
                                            <i className="icon-output-collection" />
                                        </span>
                                    )}
                                    { index.patternForReferencesToReduceOutputCollection && (
                                        <span><i className="icon-reference-pattern"/></span>
                                    )}
                                </div>
                                <div className="index-type">
                                    <span>{IndexUtils.formatType(index.type)}</span>
                                    { /* TODO
                                    
                                    <span data-bind="visible: replacement" className="margin-left margin-left-sm"><span
                                        className="label label-warning">OLD</span></span>
                                    <span data-bind="visible: parent" className="margin-left margin-left-sm"><span
                                        className="label label-warning">NEW</span></span> */ }
                                </div>
                            </div>
                        </div>
                        { !IndexUtils.isFaulty(index) && (
                            <div className="col-xs-12 col-sm-12 col-xl-5 vertical-divider properties-container">
                                <div className="properties-item priority" data-bind="if: !isSideBySide()">
                                    <span className="properties-label">Priority:</span>
                                    <div className="btn-group properties-value">
                                        <button type="button" className={classNames("btn set-size dropdown-toggle", { "btn-spinner": updatingLocalPriority, "enable": !updatingLocalPriority })}
                                                data-toggle="dropdown"
                                                data-bind="requiredAccess: 'DatabaseReadWrite', requiredAccessOptions: { strategy: 'disable' }">
                                            { index.priority === "Normal" && (
                                                <span>
                                                <i className="icon-check"/>
                                                <span>Normal</span>
                                            </span>
                                            )}
                                            { index.priority === "Low" && (
                                                <span>
                                                <i className="icon-coffee"/>
                                                <span>Low</span>
                                            </span>
                                            )}
                                            { index.priority === "High" && (
                                                <span>
                                                <i className="icon-force"/>
                                                <span>High</span>
                                            </span>
                                            )}
                                            <span className="caret"/>
                                        </button>
                                        <ul className="dropdown-menu">
                                            <li>
                                                <a href="#" onClick={e => setPriority(e, "Low")} title="Low">
                                                    <i className="icon-coffee"/><span>Low</span>
                                                </a>
                                            </li>
                                            <li>
                                                <a href="#" onClick={e => setPriority(e, "Normal")} title="Normal">
                                                    <i className="icon-check"/><span>Normal</span>
                                                </a>
                                            </li>
                                            <li>
                                                <a href="#" onClick={e => setPriority(e, "High")} title="High">
                                                    <i className="icon-force"/><span>High</span>
                                                </a>
                                            </li>
                                        </ul>
                                    </div>
                                </div>
                                <div className="properties-item mode"
                                     data-bind="css: { 'hidden': type() === 'AutoMap' || type() === 'AutoMapReduce' || isSideBySide() }">
                                    <span className="properties-label">Mode:</span>
                                    <div className="btn-group properties-value">
                                        <button type="button" className={classNames("btn set-size dropdown-toggle", { "btn-spinner": updatingLockMode, enable: !updatingLockMode })} data-toggle="dropdown"
                                                data-bind="requiredAccess: 'DatabaseReadWrite', requiredAccessOptions: { strategy: 'disable' }">
                                            {index.lockMode === "Unlock" && (
                                                <span>
                                                <i className="icon-unlock"/><span>Unlocked</span>
                                            </span>
                                            )}
                                            {index.lockMode === "LockedIgnore" && (
                                                <span>
                                                <i className="icon-lock"/><span>Locked</span>
                                            </span>
                                            )}
                                            {index.lockMode === "LockedError" && (
                                                <span>
                                                <i className="icon-lock-error"/><span>Locked (Error)</span>
                                            </span>
                                            )}
                                            <span className="caret"/>
                                        </button>
                                        <ul className="dropdown-menu">
                                            <li>
                                                <a href="#" onClick={e => setLockMode(e, "Unlock")} title="Unlocked: The index is unlocked for changes; apps can modify it, e.g. via IndexCreation.CreateIndexes().">
                                                    <i className="icon-unlock"/>
                                                    <span>Unlock</span>
                                                </a>
                                            </li>
                                            <li className="divider"/>
                                            <li>
                                                <a href="#" onClick={e => setLockMode(e, "LockedIgnore")}
                                                   title="Locked: The index is locked for changes; apps cannot modify it. Programmatic attempts to modify the index will be ignored.">
                                                    <i className="icon-lock"/>
                                                    <span>Lock</span>
                                                </a>
                                            </li>
                                            <li>
                                                <a href="#" onClick={e => setLockMode(e, "LockedError")}
                                                   title="Locked + Error: The index is locked for changes; apps cannot modify it. An error will be thrown if an app attempts to modify it.">
                                                    <i className="icon-lock-error"/>
                                                    <span>Lock (Error)</span>
                                                </a>
                                            </li>
                                        </ul>
                                    </div>
                                </div>
                            </div>
                        )}
                        
                        <div className="col-xs-12 col-sm-6 col-xl-3 actions-container">
                            <div className="actions">
                                <div className="btn-toolbar pull-right-sm" role="toolbar">
                                    <div className="btn-group properties-value">
                                        <button type="button" className="btn btn-default" data-toggle="dropdown"
                                                data-bind="css: { 'btn-spinner': _.includes($root.spinners.localState(), name) },
                                           enable: $root.globalIndexingStatus() === 'Running'  && !_.includes($root.spinners.localState(), name),
                                           requiredAccess: 'DatabaseReadWrite', requiredAccessOptions: { strategy: 'disable' }">
                                            Set State
                                            <span className="caret"/>
                                        </button>
                                        <ul className="dropdown-menu">
                                            <li data-bind="visible: canBeEnabled()">
                                                <a href="#" onClick={enableIndexing} title="Enable indexing on ALL cluster nodes">
                                                    <i className="icon-play" />
                                                    <span>Enable indexing</span>
                                                </a>
                                            </li>
                                            <li data-bind="visible: canBeDisabled()">
                                                <a href="#" onClick={disableIndexing} title="Disable indexing on ALL cluster nodes">
                                                    <i className="icon-cancel"/>
                                                    <span>Disable indexing</span>
                                                </a>
                                            </li>
                                            <li data-bind="visible: canBePaused()">
                                                <a href="#" onClick={pauseIndexing} className="text-warning" title="Pause until restart">
                                                    <i className="icon-pause"/>
                                                    <span>Pause indexing until restart</span>
                                                </a>
                                            </li>
                                            <li data-bind="visible: canBeResumed()">
                                                <a href="#" onClick={resumeIndexing} className="text-success" title="Resume indexing">
                                                    <i className="icon-play"/>
                                                    <span>Resume indexing</span>
                                                </a>
                                            </li>
                                        </ul>
                                    </div>
                                    
                                    
                                    { !IndexUtils.isFaulty(index) && (
                                        <div className="btn-group" role="group">
                                            <a className="btn btn-default" href={queryUrl}>
                                                <i className="icon-search"/><span>Query</span></a>
                                            <button type="button" className="btn btn-default dropdown-toggle"
                                                    data-toggle="dropdown" aria-haspopup="true" aria-expanded="false">
                                                <span className="caret"/>
                                                <span className="sr-only">Toggle Dropdown</span>
                                            </button>
                                            <ul className="dropdown-menu">
                                                <li>
                                                    <a href={termsUrl}>
                                                        <i className="icon-terms"/> Terms
                                                    </a>
                                                </li>
                                            </ul>
                                        </div>
                                    )}
                                    
                                    <div className="btn-group" role="group">
                                        { !IndexUtils.isAutoIndex(index) && (
                                            <a className="btn btn-default" href={editUrl}
                                               data-bind=" visible: !isAutoIndex() && !$root.isReadOnlyAccess()"
                                               title="Edit index"><i className="icon-edit"/></a>
                                        )}
                                        { IndexUtils.isAutoIndex(index) && (
                                            <a className="btn btn-default" href={editUrl}
                                               data-bind=", visible: isAutoIndex() || $root.isReadOnlyAccess()"
                                               title="View index"><i className="icon-preview"/></a>
                                        )}
                                    </div>
                                    { IndexUtils.isFaulty(index) && (
                                        <div className="btn-group" role="group">
                                            <button className="btn btn-default" data-bind="click: $root.openFaultyIndex"
                                                    title="Open index"><i className="icon-arrow-filled-up"/></button>
                                        </div>    
                                    )}
                                    
                                    <div className="btn-group" role="group">
                                        <button className="btn btn-warning" type="button" onClick={resetIndex}
                                                data-bind="requiredAccess: 'DatabaseReadWrite'"
                                                title="Reset index (rebuild)"><i className="icon-reset-index"/></button>
                                        <button className="btn btn-danger" onClick={deleteIndex}
                                                data-bind="requiredAccess: 'DatabaseReadWrite'"
                                                title="Delete the index"><i className="icon-trash"/></button>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            <div className="sidebyside-actions" data-bind="with: replacement, visible: replacement">
                <div className="panel panel-state panel-warning">
                    {index.nodesInfo.map(nodeInfo => (
                        <div key={indexNodeInfoKey(nodeInfo)}>
                            <span className="margin-right">Shard #{nodeInfo.location.shardNumber}</span>
                            <span className="margin-right">Node Tag: {nodeInfo.location.nodeTag}</span>
                            { nodeInfo.status === "loaded" && (
                                <>
                                    <span className="margin-right">Errors: {nodeInfo.details.errorCount}</span>
                                    <span className="margin-right">Entries: {nodeInfo.details.entriesCount}</span>
                                    <span className={classNames("badge", badgeClass(index, nodeInfo.details))}>
                                        {badgeText(index, nodeInfo.details)}
                                    </span>
                                </>
                            )}
                        </div>
                    ))}
                </div>
            </div>
        </div>
    )
}

function badgeClass(index: IndexSharedInfo, details: IndexNodeInfoDetails) {
    if (IndexUtils.isFaulty(index)) {
        return "badge-danger";
    }

    if (IndexUtils.isErrorState(details)) {
        return "badge-danger";
    }

    if (IndexUtils.isPausedState(details)) {
        return "badge-warnwing";
    }

    if (IndexUtils.isDisabledState(details)) {
        return "badge-warning";
    }

    if (IndexUtils.isIdleState(details)) {
        return "badge-warning";
    }

    if (IndexUtils.isErrorState(details)) {
        return "badge-danger";
    }

    return "badge-success";
}

function badgeText(index: IndexSharedInfo, details: IndexNodeInfoDetails) {
    if (IndexUtils.isFaulty(index)) {
        return "Faulty";
    }

    if (IndexUtils.isErrorState(details)) {
        return "Error";
    }

    if (IndexUtils.isPausedState(details)) {
        return "Paused";
    }

    if (IndexUtils.isDisabledState(details)) {
        return "Disabled";
    }

    if (IndexUtils.isIdleState(details)) {
        return "Idle";
    }

    return "Normal";
}

const indexUniqueId = (index: IndexSharedInfo) => "index_" + index.name;

const indexNodeInfoKey = (nodeInfo: IndexNodeInfo) => "$" + nodeInfo.location.shardNumber + "@" + nodeInfo.location.nodeTag;

