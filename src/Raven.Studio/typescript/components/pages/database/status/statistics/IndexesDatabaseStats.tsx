﻿import React, { useMemo } from "react";
import { locationAwareLoadableData } from "components/models/common";
import database from "models/resources/database";
import IndexStats = Raven.Client.Documents.Indexes.IndexStats;
import { databaseLocationComparator, withPreventDefault } from "components/utils/common";
import IndexType = Raven.Client.Documents.Indexes.IndexType;
import genUtils from "common/generalUtils";
import appUrl from "common/appUrl";
import indexStalenessReasons from "viewmodels/database/indexes/indexStalenessReasons";
import app from "durandal/app";
import { useEventsCollector } from "hooks/useEventsCollector";
import { Card, Table } from "reactstrap";
import { LazyLoad } from "components/common/LazyLoad";

interface IndexesDatabaseStatsProps {
    database: database;
    perNodeStats: locationAwareLoadableData<IndexStats[]>[];
}

interface IndexGroupStats {
    type: IndexType;
    indexes: PerIndexStats[];
}

interface PerIndexStats {
    name: string;
    type: IndexType;
    isReduceIndex: boolean;
    details: PerLocationIndexStats[];
}

interface PerLocationIndexStats {
    entriesCount: number;
    errorsCount: number;

    isFaultyIndex: boolean;
    isStale: boolean;

    mapAttempts: number;
    mapSuccesses: number;
    mapErrors: number;

    mapReferenceSuccesses: number;
    mapReferenceErrors: number;
    mapReferenceAttempts: number;

    mappedPerSecondRate: number;
    reducedPerSecondRate: number;

    reduceAttempts: number;
    reduceSuccesses: number;
    reduceErrors: number;
}

function mapIndexStats(data: locationAwareLoadableData<IndexStats[]>[]): IndexGroupStats[] {
    if (data.length === 0) {
        return null;
    }

    if (data.every((x) => x.status !== "loaded")) {
        return null;
    }

    const indexes: PerIndexStats[] = [];

    for (let i = 0; i < data.length; i++) {
        const datum = data[i];
        if (datum.status === "loaded") {
            for (const indexStats of datum.data) {
                const existingItem = indexes.find((x) => x.name === indexStats.Name);

                let itemToUpdate: PerIndexStats;

                if (existingItem) {
                    itemToUpdate = existingItem;
                } else {
                    itemToUpdate = {
                        name: indexStats.Name,
                        type: indexStats.Type,
                        details: [...Array(data.length)],
                        isReduceIndex:
                            indexStats.Type === "AutoMapReduce" ||
                            indexStats.Type === "MapReduce" ||
                            indexStats.Type === "JavaScriptMapReduce",
                    };
                    indexes.push(itemToUpdate);
                }

                itemToUpdate.details[i] = {
                    mapAttempts: indexStats.MapAttempts,
                    mapErrors: indexStats.MapErrors,
                    mapSuccesses: indexStats.MapSuccesses,
                    isFaultyIndex: indexStats.Type === "Faulty",
                    errorsCount: indexStats.ErrorsCount,
                    entriesCount: indexStats.EntriesCount,
                    mapReferenceAttempts: indexStats.MapReferenceAttempts,
                    mapReferenceSuccesses: indexStats.MapReferenceSuccesses,
                    mapReferenceErrors: indexStats.MapReferenceErrors,
                    reduceAttempts: indexStats.ReduceAttempts,
                    reduceSuccesses: indexStats.ReduceSuccesses,
                    reduceErrors: indexStats.ReduceErrors,
                    mappedPerSecondRate: indexStats.MappedPerSecondRate,
                    reducedPerSecondRate: indexStats.ReducedPerSecondRate,
                    isStale: indexStats.IsStale,
                };
            }
        }
    }

    indexes.sort((a, b) => (a.name > b.name ? 1 : -1));

    const types = Array.from(new Set(indexes.map((x) => x.type)));
    types.sort();

    return types.map((type) => {
        return {
            type,
            indexes: indexes.filter((i) => i.type === type),
        };
    });
}

interface IndexBlockProps {
    children: (locationData: PerLocationIndexStats, location: databaseLocationSpecifier) => JSX.Element;
    index: PerIndexStats;
    alwaysRenderValue?: boolean;
}

export function IndexesDatabaseStats(props: IndexesDatabaseStatsProps) {
    const { database, perNodeStats } = props;

    const indexStats = useMemo(() => mapIndexStats(perNodeStats), [perNodeStats]);

    const noData = perNodeStats.some((x) => x.status === "loaded" && x.data.length === 0);
    const sharded = database.isSharded();
    const locations = database.getLocations();

    function DetailsBlock(props: IndexBlockProps): JSX.Element {
        const { children, index, alwaysRenderValue } = props;

        return (
            <>
                {locations.map((location, locationIndex) => {
                    const stat = perNodeStats.find((x) => databaseLocationComparator(x.location, location));

                    const locationDetails = index.details[locationIndex];

                    const key = genUtils.formatLocation(location);
                    if (alwaysRenderValue) {
                        return <td key={key}>{children(locationDetails, location)}</td>;
                    }

                    if (stat.status === "loaded" && !locationDetails) {
                        return (
                            <td key={key} className="text-danger">
                                (index wasn&apos;t found on: {genUtils.formatLocation(location)})
                            </td>
                        );
                    }

                    const faulty = stat.status === "loaded" && locationDetails?.isFaultyIndex;

                    if (faulty) {
                        return (
                            <td key={key} className="text-danger">
                                (faulty index)
                            </td>
                        );
                    }

                    if (stat.status === "error") {
                        return (
                            <td key={key} className="text-danger">
                                <i className="icon-cancel" title={"Load error: " + stat.error.responseJSON.Message} />
                            </td>
                        );
                    }

                    if (stat.status === "loading" || stat.status === "notLoaded") {
                        return (
                            <td key={key}>
                                <LazyLoad active>
                                    <div>Loading...</div>
                                </LazyLoad>
                            </td>
                        );
                    }

                    return <td key={key}>{children(locationDetails, location)}</td>;
                })}
            </>
        );
    }

    const eventsCollector = useEventsCollector();

    const showStaleReasons = (index: PerIndexStats, location: databaseLocationSpecifier) => {
        const view = new indexStalenessReasons(database, index.name, location);
        eventsCollector.reportEvent("indexes", "show-stale-reasons");
        app.showBootstrapDialog(view);
    };

    return (
        <section className="mt-6">
            <h2 className="on-base-background">Indexes Stats</h2>

            {noData && (
                <div className="text-center">
                    <i className="icon-xl icon-empty-set text-muted"></i>
                    <h2 className="text-muted">No indexes have been created for this database.</h2>
                </div>
            )}

            {indexStats &&
                indexStats.map((group) => (
                    <Card className="p-4" key={group.type}>
                        <h3>{group.type} Indexes</h3>
                        <div>
                            {group.indexes.map((index) => {
                                const showErrorCounts = index.details.some((x) => x && x.errorsCount > 0);
                                const showMapErrors = index.details.some((x) => x && x.mapErrors > 0);
                                const performanceUrl = appUrl.forIndexPerformance(database, index.name);

                                const showMapReferenceSection = index.details.some(
                                    (x) =>
                                        x &&
                                        (x.mapReferenceSuccesses > 0 ||
                                            x.mapReferenceErrors > 0 ||
                                            x.mapReferenceAttempts > 0)
                                );
                                const showMapReferenceErrors = index.details.some((x) => x && x.mapReferenceErrors > 0);
                                const showMappedPerSecondRate = index.details.some(
                                    (x) => x && x.mappedPerSecondRate > 1
                                );
                                const showReducedPerSecondRate = index.details.some(
                                    (x) => x && x.reducedPerSecondRate > 1
                                );
                                const showReduceErrors = index.details.some((x) => x && x.reduceErrors > 0);

                                return (
                                    <React.Fragment key={index.name}>
                                        <h4 className="text-elipsis mt-4">
                                            <a href={performanceUrl} title={index.name}>
                                                {index.name}
                                            </a>
                                        </h4>
                                        <Table responsive striped key={index.name}>
                                            <tbody>
                                                <tr>
                                                    <td style={{ width: "200px" }}>Staleness</td>
                                                    <DetailsBlock index={index}>
                                                        {(data, location) =>
                                                            data.isStale ? (
                                                                <a
                                                                    href="#"
                                                                    title="Show stale reason"
                                                                    className="flex-shrink-0 badge badge-warning"
                                                                    onClick={withPreventDefault(() =>
                                                                        showStaleReasons(index, location)
                                                                    )}
                                                                >
                                                                    Stale
                                                                </a>
                                                            ) : (
                                                                <div title="Index up to date">
                                                                    <i className="icon-check text-success" />
                                                                </div>
                                                            )
                                                        }
                                                    </DetailsBlock>
                                                </tr>
                                                <tr>
                                                    <td style={{ width: "200px" }}>Node Tag</td>
                                                    <DetailsBlock index={index} alwaysRenderValue>
                                                        {(data, location) => <>{location.nodeTag}</>}
                                                    </DetailsBlock>
                                                </tr>
                                                {sharded && (
                                                    <tr>
                                                        <td>Shard #</td>
                                                        <DetailsBlock index={index} alwaysRenderValue>
                                                            {(data, location) => <>{location.shardNumber}</>}
                                                        </DetailsBlock>
                                                    </tr>
                                                )}
                                                <tr>
                                                    <td>Entries Count</td>
                                                    <DetailsBlock index={index}>
                                                        {(data) => <>{data.entriesCount.toLocaleString()}</>}
                                                    </DetailsBlock>
                                                </tr>
                                                {showErrorCounts && (
                                                    <tr>
                                                        <td>Errors Count</td>
                                                        <DetailsBlock index={index}>
                                                            {(data) => <>{data.errorsCount.toLocaleString()}</>}
                                                        </DetailsBlock>
                                                    </tr>
                                                )}
                                                <tr>
                                                    <td>Map Attempts</td>
                                                    <DetailsBlock index={index}>
                                                        {(data) => <>{data.mapAttempts.toLocaleString()}</>}
                                                    </DetailsBlock>
                                                </tr>
                                                <tr>
                                                    <td>Map Successes</td>
                                                    <DetailsBlock index={index}>
                                                        {(data) => <>{data.mapSuccesses.toLocaleString()}</>}
                                                    </DetailsBlock>
                                                </tr>
                                                {showMapErrors && (
                                                    <tr>
                                                        <td>Map Errors</td>
                                                        <DetailsBlock index={index}>
                                                            {(data) => <>{data.mapErrors.toLocaleString()}</>}
                                                        </DetailsBlock>
                                                    </tr>
                                                )}
                                                {showMapReferenceSection && (
                                                    <>
                                                        <tr>
                                                            <td>Map Reference Attempts</td>
                                                            <DetailsBlock index={index}>
                                                                {(data) => (
                                                                    <>{data.mapReferenceAttempts.toLocaleString()}</>
                                                                )}
                                                            </DetailsBlock>
                                                        </tr>
                                                        <tr>
                                                            <td>Map Reference Successes</td>
                                                            <DetailsBlock index={index}>
                                                                {(data) => (
                                                                    <>{data.mapReferenceSuccesses.toLocaleString()}</>
                                                                )}
                                                            </DetailsBlock>
                                                        </tr>
                                                        {showMapReferenceErrors && (
                                                            <tr>
                                                                <td>Map Reference Errors</td>
                                                                <DetailsBlock index={index}>
                                                                    {(data) => (
                                                                        <>{data.mapReferenceErrors.toLocaleString()}</>
                                                                    )}
                                                                </DetailsBlock>
                                                            </tr>
                                                        )}
                                                    </>
                                                )}
                                                {showMappedPerSecondRate && (
                                                    <tr>
                                                        <td>Mapped Per Second Rate</td>
                                                        <DetailsBlock index={index}>
                                                            {(data) => (
                                                                <>
                                                                    {data.mappedPerSecondRate > 1
                                                                        ? genUtils.formatNumberToStringFixed(
                                                                              data.mappedPerSecondRate,
                                                                              2
                                                                          )
                                                                        : ""}
                                                                </>
                                                            )}
                                                        </DetailsBlock>
                                                    </tr>
                                                )}

                                                {index.isReduceIndex && (
                                                    <>
                                                        <tr>
                                                            <td>Reduce Attempts</td>
                                                            <DetailsBlock index={index}>
                                                                {(data) => <>{data.reduceAttempts.toLocaleString()}</>}
                                                            </DetailsBlock>
                                                        </tr>
                                                        <tr>
                                                            <td>Reduce Successes</td>
                                                            <DetailsBlock index={index}>
                                                                {(data) => <>{data.reduceSuccesses.toLocaleString()}</>}
                                                            </DetailsBlock>
                                                        </tr>
                                                        {showReduceErrors && (
                                                            <tr>
                                                                <td>Reduce Errors</td>
                                                                <DetailsBlock index={index}>
                                                                    {(data) => (
                                                                        <>{data.reduceErrors.toLocaleString()}</>
                                                                    )}
                                                                </DetailsBlock>
                                                            </tr>
                                                        )}
                                                        {showReducedPerSecondRate && (
                                                            <tr>
                                                                <td>Reduced Per Second Rate</td>
                                                                <DetailsBlock index={index}>
                                                                    {(data) => (
                                                                        <>
                                                                            {data.reducedPerSecondRate > 1
                                                                                ? genUtils.formatNumberToStringFixed(
                                                                                      data.reducedPerSecondRate,
                                                                                      2
                                                                                  )
                                                                                : ""}
                                                                        </>
                                                                    )}
                                                                </DetailsBlock>
                                                            </tr>
                                                        )}
                                                    </>
                                                )}
                                            </tbody>
                                        </Table>
                                    </React.Fragment>
                                );
                            })}
                        </div>
                    </Card>
                ))}
        </section>
    );
}
