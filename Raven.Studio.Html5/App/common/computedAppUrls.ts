// Interface
interface computedAppUrls {
    databases: KnockoutComputed<string>;
    documents: KnockoutComputed<string>;
    conflicts: KnockoutComputed<string>;
    patch: KnockoutComputed<string>;
    indexes: KnockoutComputed<string>;
    newIndex: KnockoutComputed<string>;
    editIndex: (indexName?: string) => KnockoutComputed<string>;
    newTransformer: KnockoutComputed<string>;
    editTransformer: (transformerName?: string) => KnockoutComputed<string>;
    transformers: KnockoutComputed<string>;
    query: (indexName?: string) => KnockoutComputed<string>;
    reporting: KnockoutComputed<string>;
    tasks: KnockoutComputed<string>;
    status: KnockoutComputed<string>;
    metrics: KnockoutComputed<string>;
    metricsRequests: KnockoutComputed<string>;
    metricsIndexBatchSize: KnockoutComputed<string>;
    metricsPrefetches: KnockoutComputed<string>;
    settings: KnockoutComputed<string>;
    logs: KnockoutComputed<string>;
    alerts: KnockoutComputed<string>;
    indexErrors: KnockoutComputed<string>;
    replicationStats: KnockoutComputed<string>;
    userInfo: KnockoutComputed<string>;
    databaseSettings: KnockoutComputed<string>;
    quotas: KnockoutComputed<string>;
    periodicExport: KnockoutComputed<string>;
    replications: KnockoutComputed<string>;
    versioning: KnockoutComputed<string>;
    sqlReplications: KnockoutComputed<string>;
    scriptedIndexes: KnockoutComputed<string>;
    customFunctionsEditor: KnockoutComputed<string>;
    statusDebug: KnockoutComputed<string>;
    statusDebugChanges: KnockoutComputed<string>;
    statusDebugMetrics: KnockoutComputed<string>;
    statusDebugConfig: KnockoutComputed<string>;
    statusDebugDocrefs: KnockoutComputed<string>;
    statusDebugCurrentlyIndexing: KnockoutComputed<string>;
    statusDebugQueries: KnockoutComputed<string>;
    statusDebugTasks: KnockoutComputed<string>;
    statusDebugRoutes: KnockoutComputed<string>;
    statusDebugRequestTracing: KnockoutComputed<string>;
    statusDebugSqlReplication: KnockoutComputed<string>;
    statusDebugIndexFields: KnockoutComputed<string>;
    statusDebugSlowDocCounts: KnockoutComputed<string>;
    statusDebugIdentities: KnockoutComputed<string>;


    isAreaActive: (routeRoot: string) => KnockoutComputed<boolean>;
    isActive: (routeTitle: string) => KnockoutComputed<boolean>;
    databasesManagement: KnockoutComputed<string>;
    filesystemsManagement: KnockoutComputed<string>;

    filesystems: KnockoutComputed<string>;
    filesystemFiles: KnockoutComputed<string>;
    filesystemSearch: KnockoutComputed<string>;
    filesystemSynchronization: KnockoutComputed<string>;
    filesystemStatus: KnockoutComputed<string>;
    filesystemSynchronizationDestinations: KnockoutComputed<string>;
    filesystemConfiguration: KnockoutComputed<string>;
    couterStorages:KnockoutComputed<string>;
    counterStorageManagement: KnockoutComputed<string>;
    counterStorageCounters: KnockoutComputed<string>;
    counterStorageReplication: KnockoutComputed<string>;
    counterStorageStats: KnockoutComputed<string>;
    counterStorageConfiguration: KnockoutComputed<string>;
}
