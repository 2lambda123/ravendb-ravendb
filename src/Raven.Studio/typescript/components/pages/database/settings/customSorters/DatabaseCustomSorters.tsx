﻿import React, { useEffect, useState } from "react";
import { Col, Row, UncontrolledPopover, UncontrolledTooltip } from "reactstrap";
import { AboutViewAnchored, AboutViewHeading, AccordionItemWrapper } from "components/common/AboutView";
import { Icon } from "components/common/Icon";
import { HrHeader } from "components/common/HrHeader";
import { EmptySet } from "components/common/EmptySet";
import {
    RichPanel,
    RichPanelActions,
    RichPanelHeader,
    RichPanelInfo,
    RichPanelName,
} from "components/common/RichPanel";
import { todo } from "common/developmentHelper";
import { licenseSelectors } from "components/common/shell/licenseSlice";
import { useAppUrls } from "components/hooks/useAppUrls";
import { useServices } from "components/hooks/useServices";
import { useAppSelector } from "components/store";
import { AsyncStateStatus, useAsync, useAsyncCallback } from "react-async-hook";
import classNames from "classnames";
import ButtonWithSpinner from "components/common/ButtonWithSpinner";
import { CounterBadge } from "components/common/CounterBadge";
import { LoadError } from "components/common/LoadError";
import { LoadingView } from "components/common/LoadingView";
import ServerWideCustomSortersList from "components/pages/resources/manageServer/serverWideSorters/ServerWideCustomSortersList";
import { NonShardedViewProps } from "components/models/common";
import FeatureNotAvailable from "components/common/FeatureNotAvailable";
import DeleteCustomSorterConfirm from "components/common/customSorters/DeleteCustomSorterConfirm";
import { accessManagerSelectors } from "components/common/shell/accessManagerSlice";
import { getLicenseLimitReachStatus, useLimitedFeatureAvailability } from "components/utils/licenseLimitsUtils";
import { useRavenLink } from "components/hooks/useRavenLink";
import FeatureAvailabilitySummaryWrapper, {
    FeatureAvailabilityData,
} from "components/common/FeatureAvailabilitySummary";
import { throttledUpdateLicenseLimitsUsage } from "components/common/shell/setup";

todo("Feature", "Damian", "Add 'Test custom sorter' button");

export default function DatabaseCustomSorters({ db }: NonShardedViewProps) {
    const { databasesService, manageServerService } = useServices();

    const asyncGetServerWideSorters = useAsync(manageServerService.getServerWideCustomSorters, []);
    const asyncGetDatabaseSorters = useAsync(() => databasesService.getCustomSorters(db), [db]);

    const { appUrl } = useAppUrls();
    const upgradeLicenseLink = useRavenLink({ hash: "FLDLO4", isDocs: false });
    const customSortersDocsLink = useRavenLink({ hash: "LGUJH8" });

    const isDatabaseAdmin =
        useAppSelector(accessManagerSelectors.effectiveDatabaseAccessLevel(db.name)) === "DatabaseAdmin";

    const licenseClusterLimit = useAppSelector(licenseSelectors.statusValue("MaxNumberOfCustomSortersPerCluster"));
    const licenseDatabaseLimit = useAppSelector(licenseSelectors.statusValue("MaxNumberOfCustomSortersPerDatabase"));
    const numberOfCustomSortersInCluster = useAppSelector(licenseSelectors.limitsUsage).NumberOfCustomSortersInCluster;

    const featureAvailability = useLimitedFeatureAvailability({
        defaultFeatureAvailability,
        overwrites: [
            {
                featureName: defaultFeatureAvailability[0].featureName,
                value: licenseDatabaseLimit,
            },
            {
                featureName: defaultFeatureAvailability[1].featureName,
                value: licenseClusterLimit,
            },
        ],
    });

    const databaseResultsCount = asyncGetDatabaseSorters.result?.length ?? null;
    const serverWideResultsCount = asyncGetServerWideSorters.result?.length ?? null;

    useEffect(() => {
        throttledUpdateLicenseLimitsUsage();
    }, [databaseResultsCount]);

    const databaseLimitReachStatus = getLicenseLimitReachStatus(databaseResultsCount, licenseDatabaseLimit);
    const clusterLimitReachStatus = getLicenseLimitReachStatus(numberOfCustomSortersInCluster, licenseClusterLimit);

    const isLimitReached = databaseLimitReachStatus === "limitReached" || clusterLimitReachStatus === "limitReached";

    if (db.isSharded()) {
        return (
            <FeatureNotAvailable>
                <span>
                    Custom sorters are not available for <Icon icon="sharding" color="shard" margin="m-0" /> sharded
                    databases
                </span>
            </FeatureNotAvailable>
        );
    }

    return (
        <div className="content-margin">
            <Col xxl={12}>
                <Row className="gy-sm">
                    <Col>
                        <AboutViewHeading title="Custom sorters" icon="custom-sorters" />
                        {isDatabaseAdmin && (
                            <>
                                <div id="newCustomSorter" className="w-fit-content">
                                    <a
                                        href={appUrl.forEditCustomSorter(db)}
                                        className={classNames("btn btn-primary mb-3", {
                                            disabled: isLimitReached,
                                        })}
                                    >
                                        <Icon icon="plus" />
                                        Add a custom sorter
                                    </a>
                                </div>
                                {isLimitReached && (
                                    <UncontrolledPopover
                                        trigger="hover"
                                        target="newCustomSorter"
                                        placement="top"
                                        className="bs5"
                                    >
                                        <div className="p-3 text-center">
                                            <Icon
                                                icon={
                                                    databaseLimitReachStatus === "limitReached" ? "database" : "cluster"
                                                }
                                            />
                                            {databaseLimitReachStatus === "limitReached" ? "Database" : "Cluster"} has
                                            reached the maximum number of Custom Sorters allowed per{" "}
                                            {databaseLimitReachStatus === "limitReached" ? "database" : "cluster"}.
                                            <br /> Delete unused sorters or{" "}
                                            <a href={upgradeLicenseLink} target="_blank">
                                                upgrade your license
                                            </a>
                                        </div>
                                    </UncontrolledPopover>
                                )}
                            </>
                        )}

                        <HrHeader count={databaseLimitReachStatus === "notReached" ? databaseResultsCount : null}>
                            Database custom sorters
                            {databaseLimitReachStatus !== "notReached" && (
                                <CounterBadge
                                    className="ms-2"
                                    count={databaseResultsCount}
                                    limit={licenseDatabaseLimit}
                                />
                            )}
                        </HrHeader>
                        <DatabaseSortersList
                            fetchStatus={asyncGetDatabaseSorters.status}
                            sorters={asyncGetDatabaseSorters.result}
                            reload={asyncGetDatabaseSorters.execute}
                            forEditLink={(name) => appUrl.forEditCustomSorter(db, name)}
                            deleteCustomSorter={(name) => databasesService.deleteCustomSorter(db, name)}
                            serverWideSorterNames={asyncGetServerWideSorters.result?.map((x) => x.Name) ?? []}
                            isDatabaseAdmin={isDatabaseAdmin}
                        />

                        <HrHeader
                            right={
                                <a href={appUrl.forServerWideCustomSorters()} target="_blank">
                                    <Icon icon="link" />
                                    Server-wide custom sorters
                                </a>
                            }
                            count={serverWideResultsCount}
                        >
                            Server-wide custom sorters
                        </HrHeader>
                        <ServerWideCustomSortersList
                            fetchStatus={asyncGetServerWideSorters.status}
                            sorters={asyncGetServerWideSorters.result}
                            reload={asyncGetServerWideSorters.execute}
                            isReadOnly
                        />
                    </Col>
                    <Col sm={12} lg={4}>
                        <AboutViewAnchored>
                            <AccordionItemWrapper
                                targetId="1"
                                icon="about"
                                color="info"
                                description="Get additional info on this feature"
                                heading="About this view"
                            >
                                <p>
                                    A <strong>Custom Sorter</strong> allows you to define how documents will be ordered
                                    in the query results
                                    <br /> according to your specific requirements.
                                </p>
                                <div>
                                    <strong>In this view</strong>, you can add your own sorters:
                                    <ul className="margin-top-xxs">
                                        <li>
                                            The custom sorters added here can be used only with queries in this
                                            database.
                                        </li>
                                        <li>
                                            The server-wide custom sorters listed can also be applied within this
                                            database.
                                        </li>
                                        <li>The custom sorters can be tested in this view with a sample query.</li>
                                        <li>Note: custom sorters are not supported when querying Corax indexes.</li>
                                    </ul>
                                </div>
                                <div>
                                    Provide <code>C#</code> code in the editor view, or upload from file:
                                    <ul className="margin-top-xxs">
                                        <li>
                                            The sorter name must be the same as the sorter&apos;s class name in your
                                            code.
                                        </li>
                                        <li>
                                            Inherit from <code>Lucene.Net.Search.FieldComparator</code>
                                        </li>
                                        <li>
                                            Code must be compilable and include all necessary <code>using</code>{" "}
                                            statements.
                                        </li>
                                    </ul>
                                </div>
                                <hr />
                                <div className="small-label mb-2">useful links</div>
                                <a href={customSortersDocsLink} target="_blank">
                                    <Icon icon="newtab" /> Docs - Custom Sorters
                                </a>
                            </AccordionItemWrapper>
                            <FeatureAvailabilitySummaryWrapper
                                isUnlimited={
                                    databaseLimitReachStatus === "notReached" &&
                                    clusterLimitReachStatus === "notReached"
                                }
                                data={featureAvailability}
                            />
                        </AboutViewAnchored>
                    </Col>
                </Row>
            </Col>
        </div>
    );
}

interface DatabaseSortersListProps {
    fetchStatus: AsyncStateStatus;
    sorters: Raven.Client.Documents.Queries.Sorting.SorterDefinition[];
    reload: () => void;
    forEditLink: (name: string) => string;
    deleteCustomSorter: (name: string) => Promise<void>;
    serverWideSorterNames: string[];
    isDatabaseAdmin: boolean;
}

function DatabaseSortersList({
    forEditLink,
    fetchStatus,
    sorters,
    reload,
    deleteCustomSorter,
    serverWideSorterNames,
    isDatabaseAdmin,
}: DatabaseSortersListProps) {
    const asyncDeleteSorter = useAsyncCallback(deleteCustomSorter, {
        onSuccess: reload,
    });

    const [nameToConfirmDelete, setNameToConfirmDelete] = useState<string>(null);

    if (fetchStatus === "loading") {
        return <LoadingView />;
    }

    if (fetchStatus === "error") {
        return <LoadError error="Unable to load custom sorters" refresh={reload} />;
    }

    if (sorters.length === 0) {
        return <EmptySet>No custom sorters have been defined</EmptySet>;
    }

    todo("Feature", "Damian", "Render react edit sorter");

    return (
        <div>
            {sorters.map((sorter) => {
                const tooltipId = "override-info" + sorter.Name.replace(/\s/g, "-");

                return (
                    <RichPanel key={sorter.Name} className="mt-3">
                        <RichPanelHeader>
                            <RichPanelInfo>
                                <RichPanelName>{sorter.Name}</RichPanelName>
                            </RichPanelInfo>
                            {serverWideSorterNames.includes(sorter.Name) && (
                                <>
                                    <UncontrolledTooltip target={tooltipId} placement="left">
                                        Overrides server-wide sorter
                                    </UncontrolledTooltip>
                                    <Icon id={tooltipId} icon="info" color="info" />
                                </>
                            )}
                            <RichPanelActions>
                                <a href={forEditLink(sorter.Name)} className="btn btn-secondary">
                                    <Icon icon={isDatabaseAdmin ? "edit" : "preview"} margin="m-0" />
                                </a>

                                {isDatabaseAdmin && (
                                    <>
                                        {nameToConfirmDelete != null && (
                                            <DeleteCustomSorterConfirm
                                                name={nameToConfirmDelete}
                                                onConfirm={(name) => asyncDeleteSorter.execute(name)}
                                                toggle={() => setNameToConfirmDelete(null)}
                                            />
                                        )}
                                        <ButtonWithSpinner
                                            color="danger"
                                            onClick={() => setNameToConfirmDelete(sorter.Name)}
                                            icon="trash"
                                            isSpinning={asyncDeleteSorter.status === "loading"}
                                        />
                                    </>
                                )}
                            </RichPanelActions>
                        </RichPanelHeader>
                    </RichPanel>
                );
            })}
        </div>
    );
}

const defaultFeatureAvailability: FeatureAvailabilityData[] = [
    {
        featureName: "Limit per database",
        community: { value: 1 },
        professional: { value: Infinity },
        enterprise: { value: Infinity },
    },
    {
        featureName: "Limit per cluster",
        community: { value: 5 },
        professional: { value: Infinity },
        enterprise: { value: Infinity },
    },
];
