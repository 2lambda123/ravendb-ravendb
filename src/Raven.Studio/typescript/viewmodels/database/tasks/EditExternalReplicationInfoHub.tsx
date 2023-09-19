﻿import AboutViewFloating, { AccordionItemWrapper } from "components/common/AboutView";
import { licenseSelectors } from "components/common/shell/licenseSlice";
import { useAppSelector } from "components/store";
import React from "react";
import { Icon } from "components/common/Icon";
import FeatureAvailabilitySummaryWrapper from "components/common/FeatureAvailabilitySummary";
import { featureAvailabilityProfessionalOrAbove } from "components/utils/licenseLimitsUtils";
import {useRavenLink} from "hooks/useRavenLink";

export function EditExternalReplicationInfoHub() {
    const isProfessionalOrAbove = useAppSelector(licenseSelectors.isProfessionalOrAbove);
    const externalReplicationDocsLink = useRavenLink({ hash: "MZOBO3" });

    return (
        <AboutViewFloating defaultOpen={isProfessionalOrAbove ? null : "licensing"}>
            <AccordionItemWrapper
                targetId="about"
                icon="about"
                color="info"
                heading="About this view"
                description="Get additional info on this feature"
            >
                <div>
                    Schedule an <strong>External Replication ongoing-task</strong>{" "}
                    in order to have a live replica of your data in another RavenDB database in another cluster.
                    <ul className="margin-top-xxs">
                        <li>The replica can serve as a failover solution in case the source cluster is down.</li>
                    </ul>
                </div>
                <div>
                    What is replicated:
                    <ul className="margin-top-xxs">
                        <li>Documents and their related data (attachments, revisions,
                            counters,
                            time series) will be replicated.
                        </li>
                        <li>Server and cluster-level items (e.g. indexes, identities,
                            compare-exchange items, ongoing tasks definitions, etc.) are Not replicated.
                        </li>
                    </ul>
                </div>
                <div>
                    Task definition includes:
                    <ul  className="margin-top-xxs">
                        <li>A connection string to the destination database containing the URLs of
                            the target cluster&apos;s servers.
                        </li>
                        <li>An optional delay time for data replication.</li>
                        <li>A responsible node to handle this task can be set.</li>
                    </ul>
                </div>
                <hr />
                <div className="small-label mb-2">useful links</div>
                <a href={externalReplicationDocsLink} target="_blank">
                    <Icon icon="newtab" /> Docs - External Replication
                </a>
            </AccordionItemWrapper>
            <FeatureAvailabilitySummaryWrapper
                isUnlimited={isProfessionalOrAbove}
                data={featureAvailabilityProfessionalOrAbove}
            />
        </AboutViewFloating>
    );
}
