﻿import { IndexNodeInfo, IndexSharedInfo } from "components/models/indexes";
import React, { useState } from "react";
import classNames from "classnames";
import { IndexProgressTooltip } from "./IndexProgressTooltip";
import IndexRunningStatus = Raven.Client.Documents.Indexes.IndexRunningStatus;
import IndexUtils from "../../../../utils/IndexUtils";
import {
    DistributionItem,
    DistributionLegend,
    DistributionSummary,
    LocationDistribution,
} from "components/common/LocationDistribution";
import assertUnreachable from "../../../../utils/assertUnreachable";
import { ProgressCircle } from "components/common/ProgressCircle";
import { Button } from "reactstrap";

interface IndexDistributionProps {
    index: IndexSharedInfo;
    globalIndexingStatus: IndexRunningStatus;
    showStaleReason: (location: databaseLocationSpecifier) => void;
    openFaulty: (location: databaseLocationSpecifier) => void;
}

export function IndexDistribution(props: IndexDistributionProps) {
    const { index, globalIndexingStatus, showStaleReason, openFaulty } = props;

    const [indexId] = useState(() => _.uniqueId("index-id"));

    const totalErrors = index.nodesInfo
        .filter((x) => x.status === "loaded")
        .reduce((prev, current) => prev + current.details.errorCount, 0);
    const estimatedEntries = IndexUtils.estimateEntriesCount(index)?.toLocaleString() ?? "-";

    const sharded = IndexUtils.isSharded(index);

    const items = (
        <>
            {index.nodesInfo.map((nodeInfo) => {
                const shard = (
                    <div className="top shard">
                        {nodeInfo.location.shardNumber != null && (
                            <>
                                <i className="icon-shard" />
                                {nodeInfo.location.shardNumber}
                            </>
                        )}
                    </div>
                );

                const key = indexNodeInfoKey(nodeInfo);
                const id = indexId + key;
                const entriesCount = nodeInfo.details?.faulty ? "n/a" : nodeInfo.details?.entriesCount ?? "";

                return (
                    <DistributionItem
                        loading={nodeInfo.status === "loading" || nodeInfo.status === "notLoaded"}
                        id={id}
                        key={key}
                    >
                        {sharded && shard}
                        <div className={classNames("node", { top: !sharded })}>
                            {!sharded && <i className="icon-node"></i>}

                            {nodeInfo.location.nodeTag}
                        </div>
                        <div className="entries">{entriesCount}</div>
                        <div className="errors">{nodeInfo.details?.errorCount ?? ""}</div>

                        <IndexProgress nodeInfo={nodeInfo} />

                        {nodeInfo.details?.faulty && (
                            <div className="text-center">
                                <Button
                                    color="danger"
                                    className="px-1 py-0 my-1"
                                    size="xs"
                                    onClick={() => openFaulty(nodeInfo.location)}
                                >
                                    Open faulty index
                                </Button>
                            </div>
                        )}

                        <IndexProgressTooltip
                            target={id}
                            nodeInfo={nodeInfo}
                            index={index}
                            globalIndexingStatus={globalIndexingStatus}
                            showStaleReason={showStaleReason}
                        />
                    </DistributionItem>
                );
            })}
        </>
    );

    return (
        <LocationDistribution>
            <DistributionLegend>
                <div className="top"></div>
                {sharded && (
                    <div className="node">
                        <i className="icon-node" /> Node
                    </div>
                )}
                <div>
                    <i className="icon-list" /> Entries
                </div>
                <div>
                    <i className="icon-warning" /> Errors
                </div>
                <div>
                    <i />
                    Status
                </div>
            </DistributionLegend>
            <DistributionSummary>
                <div className="top">Total</div>
                {sharded && <div> </div>}
                <div>{estimatedEntries}</div>
                <div>{totalErrors}</div>
                <div></div>
            </DistributionSummary>
            {items}
        </LocationDistribution>
    );
}

interface IndexProgressProps {
    nodeInfo: IndexNodeInfo;
    inline?: boolean;
}

function iconForState(status: Raven.Client.Documents.Indexes.IndexRunningStatus) {
    switch (status) {
        case "Disabled":
            return "icon-stop";
        case "Paused":
            return "icon-pause";
        case "Pending":
            return "icon-waiting";
        case "Running":
            return "icon-check";
        default:
            assertUnreachable(status);
    }
}

export function IndexProgress(props: IndexProgressProps) {
    const { nodeInfo, inline } = props;

    if (nodeInfo.status === "error") {
        return (
            <ProgressCircle inline={inline} state="failed" icon="icon-cancel">
                Load error
            </ProgressCircle>
        );
    }

    if (!nodeInfo.details) {
        return null;
    }

    if (nodeInfo.details.faulty) {
        return (
            <ProgressCircle inline={inline} state="failed" icon="icon-cancel">
                Faulty
            </ProgressCircle>
        );
    }

    if (nodeInfo.details.state === "Error") {
        return (
            <ProgressCircle inline={inline} state="failed" icon="icon-cancel">
                Error
            </ProgressCircle>
        );
    }

    const icon = iconForState(nodeInfo.details.status);

    if (nodeInfo.progress) {
        const progress = nodeInfo.progress.global.total
            ? nodeInfo.progress.global.processed / nodeInfo.progress.global.total
            : 1;

        if (nodeInfo.details.stale) {
            return (
                <ProgressCircle
                    inline={inline}
                    state="running"
                    icon={nodeInfo.details.status === "Running" ? null : icon}
                    progress={progress}
                >
                    {nodeInfo.details.status}
                </ProgressCircle>
            );
        }
    }

    if (
        nodeInfo.details.status === "Paused" ||
        nodeInfo.details.status === "Disabled" ||
        nodeInfo.details.status === "Pending"
    ) {
        return (
            <ProgressCircle inline={inline} state="running" icon={icon}>
                {nodeInfo.details.status}
            </ProgressCircle>
        );
    }

    return (
        <ProgressCircle inline={inline} state="success" icon={icon}>
            up to date
        </ProgressCircle>
    );
}

const indexNodeInfoKey = (nodeInfo: IndexNodeInfo) => nodeInfo.location.shardNumber + "__" + nodeInfo.location.nodeTag;
