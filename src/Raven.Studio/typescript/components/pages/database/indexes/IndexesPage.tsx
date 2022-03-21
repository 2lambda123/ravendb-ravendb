﻿import React, { useCallback, useEffect, useMemo, useReducer, useState } from "react"
import database from "models/resources/database";
import collectionsTracker from "common/helpers/database/collectionsTracker";
import {
    IndexFilterCriteria, IndexGroup, IndexNodeInfo, IndexNodeInfoDetails,
    IndexSharedInfo, IndexStatus,
} from "../../../models/indexes";
import IndexPriority = Raven.Client.Documents.Indexes.IndexPriority;
import { IndexPanel } from "./IndexPanel";
import appUrl from "common/appUrl";
import deleteIndexesConfirm from "viewmodels/database/indexes/deleteIndexesConfirm";
import app from "durandal/app";
import IndexFilter, { IndexFilterDescription } from "./IndexFilter";
import IndexGlobalIndexing from "./IndexGlobalIndexing";
import IndexLockMode = Raven.Client.Documents.Indexes.IndexLockMode;
import IndexToolbarActions from "./IndexToolbarActions";
import { useServices } from "../../../hooks/useServices";
import { indexesStatsReducer, indexesStatsReducerInitializer } from "./IndexesStatsReducer";
import collection from "models/database/documents/collection";
import IndexUtils from "../../../utils/IndexUtils";
import genUtils from "common/generalUtils";
import { shardingTodo } from "common/developmentHelper";
import viewHelpers from "common/helpers/view/viewHelpers";
import { CheckboxTriple } from "../../../common/CheckboxTriple";
import { useEventsCollector } from "../../../hooks/useEventsCollector";

interface IndexesPageProps {
    database: database;
    reload: () => Promise<void>;
}

async function confirmResetIndex(db: database, index: IndexSharedInfo): Promise<boolean> {
    return new Promise(done => {
        viewHelpers.confirmationMessage("Reset index?",
            `You're resetting index: <br><ul><li><strong>${genUtils.escapeHtml(index.name)}</strong></li></ul>
             <div class="margin-top margin-top-lg text-warning bg-warning padding padding-xs flex-horizontal">
                <div class="flex-start">
                    <small><i class="icon-warning"></i></small>
                </div>
                <div>
                    <small>Clicking <strong>Reset</strong> will remove all existing indexed data.</small><br>
                    <small>All items matched by the index definition will be re-indexed.</small>
                </div>
             </div>`, {
                buttons: ["Cancel", "Reset"],
                html: true
            })
            .done(result => {
                done(result.can);
            });
    });
}

function NoIndexes() {
    const newIndexUrl = appUrl.forCurrentDatabase().newIndex();
    
    return (
        <div className="row">
            <div className="col-sm-8 col-sm-offset-2 col-lg-6 col-lg-offset-3">
                <i className="icon-xl icon-empty-set text-muted"/>
                <h2 className="text-center text-muted">No indexes have been created for this database.</h2>
                <p data-bind="requiredAccess: 'DatabaseReadWrite'" className="lead text-center text-muted">
                    Go ahead and <a href={newIndexUrl}>create one now</a>.</p>
            </div>
        </div>
    )
}

export const defaultFilterCriteria: IndexFilterCriteria = {
    status: ["Normal", "ErrorOrFaulty", "Stale", "Paused", "Disabled", "Idle", "RollingDeployment"],
    autoRefresh: true,
    showOnlyIndexesWithIndexingErrors: false,
    searchText: ""
};

function matchesAnyIndexStatus(index: IndexSharedInfo, status: IndexStatus[]): boolean {
    if (status.length === 0) {
        return false;
    }

    /* TODO
    ADD : _.includes(status, "Stale") && this.isStale()
        || _.includes(status, "RollingDeployment") && this.rollingDeploymentInProgress()
     */
    
    const anyMatch = (index: IndexSharedInfo, predicate: (index: IndexNodeInfoDetails) => boolean) => index.nodesInfo.some(x => x.status === "loaded" && predicate(x.details));
    
    return status.includes("Normal") && anyMatch(index, IndexUtils.isNormalState)
        || status.includes("ErrorOrFaulty") && (anyMatch(index, IndexUtils.isErrorState) || IndexUtils.isFaulty(index))
        || status.includes("Paused") && anyMatch(index, IndexUtils.isPausedState)
        || status.includes("Disabled") && anyMatch(index, IndexUtils.isDisabledState)
        || status.includes("Idle") && anyMatch(index, IndexUtils.isIdleState);
}

function indexMatchesFilter(index: IndexSharedInfo, filter: IndexFilterCriteria): boolean {
    const nameMatch = !filter.searchText || index.name.toLowerCase().includes(filter.searchText.toLowerCase());
    const statusMatch = matchesAnyIndexStatus(index, filter.status);
    const indexingErrorsMatch = true; //TODO:  !withIndexingErrorsOnly || (withIndexingErrorsOnly && !!this.errorsCount());

    return nameMatch && statusMatch && indexingErrorsMatch;
}

function groupAndFilterIndexStats(indexes: IndexSharedInfo[], collections: collection[], filter: IndexFilterCriteria) {
    const result = new Map<string, IndexGroup>();

    indexes.forEach(index => {
        if (!indexMatchesFilter(index, filter)) {
            return ;
        }

        const groupName = IndexUtils.getIndexGroupName(index, collections);
        if (!result.has(groupName)) {
            const group: IndexGroup = {
                name: groupName,
                indexes: []
            }
            result.set(groupName, group);
        }

        const group = result.get(groupName);
        group.indexes.push(index);
    });

    // sort groups
    const groups = Array.from(result.values());
    groups.sort((l, r) => genUtils.sortAlphaNumeric(l.name, r.name));

    groups.forEach(group => {
        group.indexes.sort((a, b) => genUtils.sortAlphaNumeric(a.name, b.name));
    });
    
    return groups;
}


export function IndexesPage(props: IndexesPageProps) {
    const { database } = props;
    const locations = database.getLocations();
    
    const { indexesService } = useServices();
    
    const eventsCollector = useEventsCollector();
    
    const [stats, dispatch] = useReducer(indexesStatsReducer, locations, indexesStatsReducerInitializer);
    
    const initialLocation = locations[0]; //TODO:
    
    const [filter, setFilter] = useState<IndexFilterCriteria>(defaultFilterCriteria);
    
    const groups = useMemo(() => {
        const collections = collectionsTracker.default.collections();
        return groupAndFilterIndexStats(stats.indexes, collections, filter);
    }, [stats, filter]);
    
    const [selectedIndexes, setSelectedIndexes] = useState<string[]>([]);
    
    const fetchStats = async (location: databaseLocationSpecifier) => {
        const stats = await indexesService.getStats(database, location);
        dispatch({
            type: "StatsLoaded",
            location,
            stats
        });
    };
    
    useEffect(() => {
        fetchStats(initialLocation);
    }, []);

    const getSelectedIndexes = (): IndexSharedInfo[] => stats.indexes.filter(x => selectedIndexes.includes(x.name));
    
    const deleteSelectedIndexes = () => confirmDeleteIndexes(database, getSelectedIndexes())

    const enableSelectedIndexes = async () => {
        //TODO: add confirmation dialog!
        const indexes = getSelectedIndexes();
        while (indexes.length) {
            await enableIndexing(indexes.pop());
        }
    }
    
    const disableSelectedIndexes = async () => {
        //TODO: add confirmation dialog!
        const indexes = getSelectedIndexes();
        while (indexes.length) {
            await disableIndexing(indexes.pop());
        }
    }

    const resumeSelectedIndexes = async () => {
        //TODO: add confirmation dialog!
        //TODO: use list + single call per location
        const indexes = getSelectedIndexes();
        while (indexes.length) {
            await resumeIndexing(indexes.pop());
        }
    }

    const pauseSelectedIndexes = async () => {
        //TODO: add confirmation dialog!
        const indexes = getSelectedIndexes();
        //TODO: send list of indexes - since call per location
        while (indexes.length) {
            await pauseIndexing(indexes.pop());
        }
    }
    
    const confirmDeleteIndexes = async (db: database, indexes: IndexSharedInfo[]): Promise<void> => {
        if (indexes.length > 0) {
            const deleteIndexesVm = new deleteIndexesConfirm(indexes, db);
            app.showBootstrapDialog(deleteIndexesVm);
            deleteIndexesVm.deleteTask
                .done((deleted: boolean) => {
                    if (deleted) {
                        dispatch({
                            type: "DeleteIndexes",
                            indexNames: indexes.map(x => x.name)
                        });
                    }
                });
            await deleteIndexesVm.deleteTask;
        }
    }
    
    const setIndexPriority = async (index: IndexSharedInfo, priority: IndexPriority) => {
        await indexesService.setPriority(index, priority, database);

        dispatch({
            type: "SetPriority",
            priority,
            indexName: index.name
        });
    }
    
    const setIndexLockMode = async (index: IndexSharedInfo, lockMode: IndexLockMode) => {
        await indexesService.setLockMode([index], lockMode, database);
        
        dispatch({
            type: "SetLockMode",
            lockMode,
            indexName: index.name
        });
    }

    if (stats.indexes.length === 0) {
        return <NoIndexes />
    }

    const toggleSelection = (index: IndexSharedInfo) => {
        if (selectedIndexes.includes(index.name)) {
            setSelectedIndexes(s => s.filter(x => x !== index.name));
        } else {
            setSelectedIndexes(s => s.concat(index.name));
        }
    }
    
    const loadMissing = () => { //TODO: temp
        stats.indexes[0].nodesInfo.forEach(nodeInfo => {
            if (nodeInfo.status === "notLoaded") {
                fetchStats(nodeInfo.location);
            }
        })
    }
    
    const enableIndexing = async (index: IndexSharedInfo) => {
        //TODO: dialog with confirmation which locations should be modified
        const locations = database.getLocations();
        while (locations.length > 0) {
            const location = locations.pop();
            await indexesService.enable(index, database, location);
            dispatch({
                type: "EnableIndexing",
                indexName: index.name,
                location
            });
        }
    }
    
    const disableIndexing = async (index: IndexSharedInfo) => {
        //TODO: dialog with confirmation which locations should be modified
        const locations = database.getLocations();
        while (locations.length > 0) {
            const location = locations.pop();
            await indexesService.disable(index, database, location);
            dispatch({
                type: "DisableIndexing",
                indexName: index.name,
                location
            });
        }
    }
    
    const pauseIndexing = async (index: IndexSharedInfo) => {
        const locations = database.getLocations();
        while (locations.length > 0) {
            const location = locations.pop();
            await indexesService.pause([index], database, location); 
            dispatch({
                type: "PauseIndexing",
                indexName: index.name,
                location
            });
        }
    }

    const resumeIndexing = async (index: IndexSharedInfo) => {
        const locations = database.getLocations();
        while (locations.length > 0) {
            const location = locations.pop();
            await indexesService.resume([index], database, location);
            dispatch({
                type: "ResumeIndexing",
                indexName: index.name,
                location
            });
        }
    }
    
    const resetIndex = async (index: IndexSharedInfo) => {
        const canReset = await confirmResetIndex(database, index);
        if (canReset) {
            eventsCollector.reportEvent("indexes", "reset");
            
            const locations = database.getLocations();
            while (locations.length) {
                await indexesService.resetIndex(index, database, locations.pop());
            }
            
            /* TODO
             // reset index is implemented as delete and insert, so we receive notification about deleted index via changes API
                    // let's issue marker to ignore index delete information for next few seconds because it might be caused by reset.
                    // Unfortunately we can't use resetIndexVm.resetTask.done, because we receive event via changes api before resetTask promise 
                    // is resolved. 
                    this.resetsInProgress.add(i.name);

                    new resetIndexCommand(i.name, this.activeDatabase())
                        .execute();

                    setTimeout(() => {
                        this.resetsInProgress.delete(i.name);
                    }, 30000);
             */
        }
    }
    
    const getAllIndexes = () => {
        const allIndexes: IndexSharedInfo[] = [];
        groups.forEach(group => allIndexes.push(...group.indexes));
        return allIndexes;
    }
    
    const toggleSelectAll = () => {
        eventsCollector.reportEvent("indexes", "toggle-select-all");
        
        const selectedIndexesCount = selectedIndexes.length;

        if (selectedIndexesCount > 0) {
            setSelectedIndexes([]);
        } else {
            const toSelect: string[] = [];
            groups.forEach(group => {
                toSelect.push(...group.indexes.map(x => x.name));
            });
            setSelectedIndexes(toSelect); 
            /* TODO handle replacements
             this.indexGroups().forEach(indexGroup => {
                if (!indexGroup.groupHidden()) {
                    indexGroup.indexes().forEach(index => {
                        if (!index.filteredOut() && !_.includes(namesToSelect, index.name)) {
                            namesToSelect.push(index.name);

                            if (index.replacement()) {
                                namesToSelect.push(index.replacement().name);
                            }
                        }
                    });
                }
            });
             */
        }
    }
    
    const indexesSelectionState = (): checkbox => {
        const selectedCount = selectedIndexes.length;
        const indexesCount = getAllIndexes().length;
        if (indexesCount && selectedCount === indexesCount) {
            return "checked";
        }
        if (selectedCount > 0) {
            return "some_checked";
        }
        return "unchecked";
    };
    
    return (
        <div className="flex-vertical absolute-fill">
            <div className="flex-header">
                {stats.indexes.length > 0 && (
                    <div className="clearfix toolbar">
                        <div className="pull-left">
                            <div className="form-inline">
                                <div className="checkbox checkbox-primary checkbox-inline align-checkboxes"
                                     title="Select all or none" data-bind="requiredAccess: 'DatabaseReadWrite'">
                                    <CheckboxTriple onChanged={toggleSelectAll} state={indexesSelectionState()} />
                                    <label/>
                                </div>

                                <IndexFilter filter={filter} setFilter={setFilter} />
                                <IndexToolbarActions
                                    selectedIndexes={selectedIndexes}
                                    deleteSelectedIndexes={deleteSelectedIndexes}
                                    enableSelectedIndexes={enableSelectedIndexes}
                                    disableSelectedIndexes={disableSelectedIndexes}
                                    pauseSelectedIndexes={pauseSelectedIndexes}
                                    resumeSelectedIndexes={resumeSelectedIndexes}
                                />
                            </div>
                        </div>
                        { /*  TODO  <IndexGlobalIndexing /> */ }
                    </div>
                )}
                <IndexFilterDescription filter={filter} groups={groups} />
                <button type="button" onClick={loadMissing}>Load Missing</button>
            </div>
            <div className="flex-grow scroll js-scroll-container">
                {groups.map(group => {
                    return (
                        <div key={group.name}>
                            <h2 className="on-base-background" title={"Collection: " + group.name}>
                                {group.name}
                            </h2>
                            {group.indexes.map(index =>
                                (
                                    <IndexPanel setPriority={p => setIndexPriority(index, p)}
                                                setLockMode={l => setIndexLockMode(index, l)}
                                                resetIndex={() => resetIndex(index)}
                                                enableIndexing={() => enableIndexing(index)}
                                                disableIndexing={() => disableIndexing(index)}
                                                pauseIndexing={() => pauseIndexing(index)}
                                                resumeIndexing={() => resumeIndexing(index)}
                                                index={index}
                                                deleteIndex={() => confirmDeleteIndexes(database, [index])}
                                                selected={selectedIndexes.includes(index.name)}
                                                toggleSelection={() => toggleSelection(index)}
                                                key={index.name}
                                    />
                                ))}
                        </div>
                    )
                })}
            </div>
        </div>
    );
}
