﻿import IndexSourceType = Raven.Client.Documents.Indexes.IndexSourceType;

export type IndexStatus = "Normal" | "ErrorOrFaulty" | "Stale" | "Paused" | "Disabled" | "Idle" | "RollingDeployment";

export interface IndexGroup {
    name: string;
    indexes: IndexSharedInfo[];
}

export interface IndexSharedInfo {
    name: string;
    sourceType: IndexSourceType;
    collections: string[];
    priority: Raven.Client.Documents.Indexes.IndexPriority;
    type: Raven.Client.Documents.Indexes.IndexType;
    lockMode: Raven.Client.Documents.Indexes.IndexLockMode;

    reduceOutputCollectionName: string;
    patternForReferencesToReduceOutputCollection: string;
    collectionNameForReferenceDocuments: string;

    nodesInfo: IndexNodeInfo[];
}

export interface IndexNodeInfo {
    location: databaseLocationSpecifier;
    status: "notLoaded" | "loading" | "loaded" | "error";
    details: IndexNodeInfoDetails;
}

export interface IndexNodeInfoDetails {
    errorCount: number;
    entriesCount: number;
    status: Raven.Client.Documents.Indexes.IndexRunningStatus;
    state: Raven.Client.Documents.Indexes.IndexState;
    stale: boolean;
}

export interface IndexFilterCriteria {
    searchText: string;
    status: IndexStatus[];
    showOnlyIndexesWithIndexingErrors: boolean;
    autoRefresh: boolean; //TODO: 
}
