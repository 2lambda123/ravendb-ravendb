﻿import React, { useCallback } from "react";
import { Button, Input, Label } from "reactstrap";
import { UncontrolledButtonWithDropdownPanel } from "components/common/DropdownPanel";
import useId from "hooks/useId";
import useBoolean from "hooks/useBoolean";
import { useServices } from "hooks/useServices";
import database from "models/resources/database";
import { useLicenseStatus } from "hooks/useLicenseStatus";
import LicenseStatus = Raven.Server.Commercial.LicenseStatus;
import clusterTopology from "models/database/cluster/clusterTopology";
import { useChanges } from "hooks/useChanges";
import { useAccessManager } from "hooks/useAccessManager";
import { NodeGroup } from "components/pages/resources/manageDatabaseGroup/NodeGroup";
import { useDatabaseManager } from "hooks/useDatabaseManager";
import { OrchestratorsGroup } from "components/pages/resources/manageDatabaseGroup/OrchestratorsGroup";
import { ShardsGroup } from "components/pages/resources/manageDatabaseGroup/ShardsGroup";
import { FlexGrow } from "components/common/FlexGrow";
import app from "durandal/app";
import addNewShardToDatabaseGroup from "viewmodels/resources/addNewShardToDatabaseGroup";
import { StickyHeader } from "components/common/StickyHeader";
import { RichPanel, RichPanelHeader, RichPanelInfo, RichPanelName } from "components/common/RichPanel";

interface ManageDatabaseGroupPageProps {
    db: database;
}

function anyNodeHasError(topology: clusterTopology) {
    if (!topology) {
        return true;
    }
    const votingInProgress = topology.currentState() === "Candidate";

    if (votingInProgress) {
        return true;
    }

    return topology.nodes().some((x) => !x.connected());
}

function getDynamicDatabaseDistributionWarning(
    licenseStatus: LicenseStatus,
    encryptedDatabase: boolean,
    nodesCount: number
) {
    if (!licenseStatus.HasDynamicNodesDistribution) {
        return "Your current license doesn't include the dynamic nodes distribution feature.";
    }

    if (encryptedDatabase) {
        return "Dynamic database distribution is not available when database is encrypted.";
    }

    if (nodesCount === 1) {
        return "There is only one node in the group.";
    }

    return null;
}

export function ManageDatabaseGroupPage(props: ManageDatabaseGroupPageProps) {
    const { db } = props;

    const { databasesService } = useServices();
    const { status: licenseStatus } = useLicenseStatus();

    const { isOperatorOrAbove } = useAccessManager();

    const { databases, findByName } = useDatabaseManager();

    const {
        value: dynamicDatabaseDistribution,
        toggle: toggleDynamicDatabaseDistribution,
        setValue: setDynamicDatabaseDistribution,
    } = useBoolean(false); //tODO: assign default value

    const { serverNotifications } = useChanges();

    const dbShardedInfo = findByName(db.name);

    /*
    useEffect(() => {
        const anyError = anyNodeHasError(clusterTopology);
        //TODO: test this part!
        if (sortableMode && anyError) {
            messagePublisher.reportWarning(
                "Can't reorder nodes, when at least one node is down or voting is in progress."
            );
            setSortableMode(false);
        }
    }, [clusterTopology, sortableMode]);
    
     */

    const settingsUniqueId = useId("settings");

    const addNewShard = useCallback(() => {
        const addShardView = new addNewShardToDatabaseGroup(db.name);
        app.showBootstrapDialog(addShardView);
    }, [db]);

    const changeDynamicDatabaseDistribution = useCallback(async () => {
        toggleDynamicDatabaseDistribution();

        await databasesService.toggleDynamicNodeAssignment(db, !dynamicDatabaseDistribution);
    }, [dynamicDatabaseDistribution, toggleDynamicDatabaseDistribution, databasesService, db]);

    const dynamicDatabaseDistributionWarning = getDynamicDatabaseDistributionWarning(
        licenseStatus,
        dbShardedInfo.encrypted,
        dbShardedInfo.nodes.length
    );
    const enableDynamicDatabaseDistribution = isOperatorOrAbove() && !dynamicDatabaseDistributionWarning;

    return (
        <>
            <StickyHeader>
                <div className="flex-horizontal">
                    <UncontrolledButtonWithDropdownPanel buttonText="Settings">
                        <>
                            <Label className="dropdown-item-text m-0" htmlFor={settingsUniqueId}>
                                <div className="form-switch form-check-reverse">
                                    <Input
                                        id={settingsUniqueId}
                                        type="switch"
                                        role="switch"
                                        disabled={!enableDynamicDatabaseDistribution}
                                        checked={dynamicDatabaseDistribution}
                                        onChange={changeDynamicDatabaseDistribution}
                                    />
                                    Allow dynamic database distribution
                                </div>
                            </Label>
                            {dynamicDatabaseDistributionWarning && (
                                <div className="bg-faded-warning px-4 py-2">{dynamicDatabaseDistributionWarning}</div>
                            )}
                        </>
                    </UncontrolledButtonWithDropdownPanel>
                    <FlexGrow />
                    {db.isSharded() && (
                        <Button color="shard" onClick={addNewShard}>
                            <i className="icon-shard icon-addon-plus me-1" />
                            Add Shard
                        </Button>
                    )}
                </div>
            </StickyHeader>
            <div className="content-margin">
                {db.isSharded() ? (
                    <React.Fragment key="sharded-db">
                        <OrchestratorsGroup
                            orchestrators={dbShardedInfo.nodes}
                            db={db}
                            deletionInProgress={dbShardedInfo.deletionInProgress}
                        />
                        {db.shards().map((shard) => (
                            <ShardsGroup
                                key={shard.name}
                                nodes={shard.nodes()}
                                shard={shard}
                                lockMode={dbShardedInfo.lockMode}
                            />
                        ))}
                    </React.Fragment>
                ) : (
                    <NodeGroup
                        key="non-sharded-db"
                        nodes={dbShardedInfo.nodes}
                        db={db}
                        deletionInProgress={dbShardedInfo.deletionInProgress}
                        lockMode={dbShardedInfo.lockMode}
                    />
                )}
            </div>
            {/* <RichPanel className="mt-3">
                <RichPanelHeader className="bg-faded-orchestrator">
                    <RichPanelInfo>
                        <RichPanelName className="text-orchestrator">
                            <i className="icon-orchestrator me-2" /> Orchestrators
                        </RichPanelName>
                    </RichPanelInfo>
                </RichPanelHeader>
                <div className="dbgroup">
                    <div className="dbgroup-image"></div>
                    <div className="dbgroup-list">
                        <div className="dbgroup-item item-new">
                            <div className="dbgroup-node">
                                <i className="icon-node-add" />
                            </div>
                            <div className="dbgroup-actions">
                                <Button size="xs" color="success" outline className="rounded-pill">
                                    <i className="icon-plus me-1" />
                                    Add node
                                </Button>
                            </div>
                        </div>
                        <div className="dbgroup-item">
                            <div className="dbgroup-node">
                                <i className="icon-node text-node" /> <strong>A</strong> Catching Up
                            </div>
                            <div className="dbgroup-type">
                                <i className="icon-dbgroup-member me-1" /> Member
                            </div>
                            <div className="dbgroup-actions">
                                <Button size="xs" color="danger" outline className="rounded-pill">
                                    <i className="icon-cancel" /> Remove
                                </Button>
                            </div>
                        </div>
                        <div className="dbgroup-item">
                            <div className="dbgroup-node">
                                <i className="icon-node text-node" /> <strong>B</strong> Active
                            </div>
                            <div className="dbgroup-type">
                                <i className="icon-dbgroup-promotable me-1" /> Promotable
                            </div>
                            <div className="dbgroup-actions">
                                <Button size="xs" color="danger" outline className="rounded-pill">
                                    <i className="icon-cancel" /> Remove
                                </Button>
                            </div>
                        </div>
                    </div>
                </div>
            </RichPanel> */}
        </>
    );
}
