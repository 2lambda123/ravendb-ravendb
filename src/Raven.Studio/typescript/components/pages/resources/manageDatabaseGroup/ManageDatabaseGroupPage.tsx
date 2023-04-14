﻿import React, { useCallback } from "react";
import { Button, Input, Label } from "reactstrap";
import { UncontrolledButtonWithDropdownPanel } from "components/common/DropdownPanel";
import useId from "hooks/useId";
import useBoolean from "hooks/useBoolean";
import { useServices } from "hooks/useServices";
import database from "models/resources/database";
import { useLicenseStatus } from "hooks/useLicenseStatus";
import LicenseStatus = Raven.Server.Commercial.LicenseStatus;
import { useAccessManager } from "hooks/useAccessManager";
import { NodeGroup } from "components/pages/resources/manageDatabaseGroup/partials/NodeGroup";
import { OrchestratorsGroup } from "components/pages/resources/manageDatabaseGroup/partials/OrchestratorsGroup";
import { ShardsGroup } from "components/pages/resources/manageDatabaseGroup/partials/ShardsGroup";
import { FlexGrow } from "components/common/FlexGrow";
import app from "durandal/app";
import addNewShardToDatabaseGroup from "viewmodels/resources/addNewShardToDatabaseGroup";
import { StickyHeader } from "components/common/StickyHeader";
import { useAppSelector } from "components/store";
import { ShardedDatabaseSharedInfo } from "components/models/databases";
import { selectDatabaseByName } from "components/common/shell/databaseSliceSelectors";
import { Icon } from "components/common/Icon";

interface ManageDatabaseGroupPageProps {
    db: database;
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

    const dbSharedInfo = useAppSelector(selectDatabaseByName(db.name));

    const { value: dynamicDatabaseDistribution, toggle: toggleDynamicDatabaseDistribution } = useBoolean(
        dbSharedInfo.dynamicNodesDistribution
    );

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
        dbSharedInfo.encrypted,
        dbSharedInfo.nodes.length
    );
    const enableDynamicDatabaseDistribution = isOperatorOrAbove() && !dynamicDatabaseDistributionWarning;

    return (
        <>
            <StickyHeader>
                <div className="flex-horizontal">
                    {!db.isSharded() && (
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
                                    <div className="bg-faded-warning px-4 py-2">
                                        {dynamicDatabaseDistributionWarning}
                                    </div>
                                )}
                            </>
                        </UncontrolledButtonWithDropdownPanel>
                    )}

                    <FlexGrow />
                    {db.isSharded() && (
                        <Button color="shard" onClick={addNewShard}>
                            <Icon icon="shard" addon="plus" className="me-1" />
                            Add Shard
                        </Button>
                    )}
                </div>
            </StickyHeader>
            <div className="content-margin">
                {db.isSharded() ? (
                    <React.Fragment key="sharded-db">
                        <OrchestratorsGroup db={dbSharedInfo} />
                        {(dbSharedInfo as ShardedDatabaseSharedInfo).shards.map((shard) => {
                            return <ShardsGroup key={shard.name} db={shard} />;
                        })}
                    </React.Fragment>
                ) : (
                    <NodeGroup key="non-sharded-db" db={dbSharedInfo} />
                )}
            </div>
        </>
    );
}
