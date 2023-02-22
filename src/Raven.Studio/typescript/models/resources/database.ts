/// <reference path="../../../typings/tsd.d.ts"/>

import DeletionInProgressStatus = Raven.Client.ServerWide.DeletionInProgressStatus;
import accessManager from "common/shell/accessManager";
import { DatabaseSharedInfo, NodeInfo } from "components/models/databases";
import NodeId = Raven.Client.ServerWide.Operations.NodeId;
import NodesTopology = Raven.Client.ServerWide.Operations.NodesTopology;
import StudioDatabaseInfo = Raven.Server.Web.System.Processors.Studio.StudioDatabasesHandlerForGetDatabases.StudioDatabaseInfo;
import DatabaseLockMode = Raven.Client.ServerWide.DatabaseLockMode;
import type shardedDatabase from "models/resources/shardedDatabase";

abstract class database {
    static readonly type = "database";
    static readonly qualifier = "db";

    name: string;

    disabled = ko.observable<boolean>(false);
    errored = ko.observable<boolean>(false);
    relevant = ko.observable<boolean>(true);
    nodes = ko.observableArray<NodeInfo>([]);
    hasRevisionsConfiguration = ko.observable<boolean>(false);
    hasExpirationConfiguration = ko.observable<boolean>(false);
    hasRefreshConfiguration = ko.observable<boolean>(false);
    isEncrypted = ko.observable<boolean>(false);
    lockMode = ko.observable<DatabaseLockMode>();
    deletionInProgress = ko.observableArray<{ tag: string, status: DeletionInProgressStatus }>([]);
    
    environment = ko.observable<Raven.Client.Documents.Operations.Configuration.StudioConfiguration.StudioEnvironment>();
    environmentClass = database.createEnvironmentColorComputed("label", this.environment);

    databaseAccess = ko.observable<databaseAccessLevel>();
    databaseAccessText = ko.observable<string>();
    databaseAccessColor = ko.observable<string>();
    
    clusterNodeTag: KnockoutObservable<string>;
    
    abstract get root(): database;
    
    abstract isSharded(): this is shardedDatabase;

    abstract getLocations(): databaseLocationSpecifier[];

    /**
     * Gets first location - but prefers local node tag
     * @param preferredNodeTag
     */
    getFirstLocation(preferredNodeTag: string): databaseLocationSpecifier {
        const preferredMatch = this.getLocations().find(x => x.nodeTag === preferredNodeTag);
        if (preferredMatch) {
            return preferredMatch;
        }
        
        return this.getLocations()[0];
    }
    
    protected constructor(dbInfo: StudioDatabaseInfo, clusterNodeTag: KnockoutObservable<string>) {
        this.clusterNodeTag = clusterNodeTag;
    }
    
    static createEnvironmentColorComputed(prefix: string, source: KnockoutObservable<Raven.Client.Documents.Operations.Configuration.StudioConfiguration.StudioEnvironment>) {
        return ko.pureComputed(() => {
            const env = source();
            if (env) {
                switch (env) {
                    case "Production":
                        return prefix + "-danger";
                    case "Testing":
                        return prefix + "-success";
                    case "Development":
                        return prefix + "-info";
                }
            }

            return null;
        });
    }

    updateUsing(incomingCopy: StudioDatabaseInfo) {
        this.isEncrypted(incomingCopy.IsEncrypted);
        this.name = incomingCopy.Name;
        this.disabled(incomingCopy.IsDisabled);
        this.lockMode(incomingCopy.LockMode);
        
        this.deletionInProgress(Object.entries(incomingCopy.DeletionInProgress).map((kv: [string, DeletionInProgressStatus]) => {
            return {
                tag: kv[0],
                status: kv[1]
            }
        }));
        
        this.hasRevisionsConfiguration(incomingCopy.HasRevisionsConfiguration);
        this.hasExpirationConfiguration(incomingCopy.HasExpirationConfiguration);
        this.hasRefreshConfiguration(incomingCopy.HasRefreshConfiguration);
        
        /* TODO
        if (incomingCopy.LoadError) {
            this.errored(true);
        }*/

        this.environment(incomingCopy.StudioEnvironment !== "None" ? incomingCopy.StudioEnvironment : null);
        
        //TODO: delete
        const dbAccessLevel = accessManager.default.getEffectiveDatabaseAccessLevel(incomingCopy.Name);
        this.databaseAccess(dbAccessLevel);
        this.databaseAccessText(accessManager.default.getAccessLevelText(dbAccessLevel));
        this.databaseAccessColor(accessManager.default.getAccessColor(dbAccessLevel));
    }
    
    isBeingDeleted() {
        const localTag = this.clusterNodeTag();
        return this.deletionInProgress().some(x => x.tag === localTag);
    }

    static getNameFromUrl(url: string) {
        const index = url.indexOf("databases/");
        return (index > 0) ? url.substring(index + 10) : "";
    }

    toDto(): DatabaseSharedInfo {
        return {
            name: this.name,
            encrypted: this.isEncrypted(),
            sharded: this.isSharded(),
            nodes: this.nodes(),
            disabled: this.disabled(),
            currentNode: { 
                relevant: this.relevant(),
                isBeingDeleted: this.isBeingDeleted()
            },
            lockMode: this.lockMode(),
            deletionInProgress: this.deletionInProgress().map(x => x.tag)
        }
    }
    
    //TODO: remove those props?
    get fullTypeName() {
        return "Database";
    }

    get qualifier() {
        return database.qualifier;
    }


    get type() {
        return database.type;
    }

    protected mapNode(topology: NodesTopology, node: NodeId, type: databaseGroupNodeType): NodeInfo {
        return {
            tag: node.NodeTag,
            nodeUrl: node.NodeUrl,
            type,
            responsibleNode: node.ResponsibleNode,
            lastError: topology.Status?.[node.NodeTag]?.LastError,
            lastStatus: topology.Status?.[node.NodeTag]?.LastStatus,
        }
    }
}

export = database;
