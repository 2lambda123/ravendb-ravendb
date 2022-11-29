﻿import database from "models/resources/database";
import React, { useCallback, useEffect, useState } from "react";
import { useServices } from "../../../../hooks/useServices";
import EssentialDatabaseStatistics = Raven.Client.Documents.Operations.EssentialDatabaseStatistics;
import { EssentialDatabaseStatsComponent } from "./EssentialDatabaseStatsComponent";
import { useAppUrls } from "../../../../hooks/useAppUrls";
import { DetailedDatabaseStats } from "./DetailedDatabaseStats";
import { IndexesDatabaseStats } from "./IndexesDatabaseStats";

interface StatisticsPageProps {
    database: database;
}

export function StatisticsPage(props: StatisticsPageProps): JSX.Element {
    const { database } = props;
    const { databasesService } = useServices();

    const [essentialStats, setEssentialStats] = useState<EssentialDatabaseStatistics>();
    const [dbDetailsVisible, setDbDetailsVisible] = useState(false);

    const fetchEssentialStats = useCallback(async () => {
        const stats = await databasesService.getEssentialStats(database);
        setEssentialStats(stats);
    }, [databasesService, database]);

    const rawJsonUrl = useAppUrls().appUrl.forEssentialStatsRawData(database);

    useEffect(() => {
        // noinspection JSIgnoredPromiseFromCall
        fetchEssentialStats();
    }, [fetchEssentialStats]);

    if (!essentialStats) {
        return (
            <div>
                <i className="btn-spinner margin-right" />
                <span>Loading...</span>
            </div>
        );
    }

    const refreshStats = () => {
        // noinspection JSIgnoredPromiseFromCall
        fetchEssentialStats();
    };

    return (
        <div className="stats">
            <div className="row">
                <div className="col">
                    <h2 className="on-base-background">
                        General Database Stats
                        <a target="_blank" href={rawJsonUrl} title="Show raw output">
                            <i className="icon-link"></i>
                        </a>
                    </h2>
                </div>
                <div className="col-auto">
                    <button
                        onClick={() => setDbDetailsVisible((x) => !x)}
                        type="button"
                        className="btn btn-primary pull-right margin-left-xs"
                        title="Click to load detailed statistics"
                    >
                        <span>{dbDetailsVisible ? "Hide" : "Show"} details</span>
                    </button>
                    <button
                        onClick={refreshStats}
                        type="button"
                        className="btn btn-primary pull-right margin-left-xs"
                        title="Click to refresh stats"
                    >
                        <i className="icon-refresh"></i>
                        <span>Refresh</span>
                    </button>
                </div>
            </div>

            <EssentialDatabaseStatsComponent stats={essentialStats} />

            {dbDetailsVisible && <DetailedDatabaseStats key="db-stats" database={database} />}
            {dbDetailsVisible && <IndexesDatabaseStats key="index-stats" database={database} />}
        </div>
    );
}
