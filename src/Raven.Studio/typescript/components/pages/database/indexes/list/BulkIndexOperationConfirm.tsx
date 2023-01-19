﻿import React, { useState } from "react";
import { Button, Modal, ModalBody, ModalFooter, ModalHeader } from "reactstrap";
import pluralizeHelpers from "common/helpers/text/pluralizeHelpers";
import { IndexSharedInfo } from "components/models/indexes";
import { MultipleDatabaseLocationSelector } from "components/common/MultipleDatabaseLocationSelector";
import { capitalize } from "lodash";
import assertUnreachable from "components/utils/assertUnreachable";

type operationType = "pause" | "resume" | "enable" | "disable";

interface BulkIndexOperationConfirmProps {
    type: operationType;
    indexes: IndexSharedInfo[];
    toggle: () => void;
    locations: databaseLocationSpecifier[];
    onConfirm: (locations: databaseLocationSpecifier[]) => void;
}

export function BulkIndexOperationConfirm(props: BulkIndexOperationConfirmProps) {
    const { type, indexes, toggle, locations, onConfirm } = props;

    const infinitive = getInfinitiveForType(type);
    const gerund = getGerund(type);

    const [selectedLocations, setSelectedLocations] = useState<databaseLocationSpecifier[]>(() => locations);

    const title = infinitive + " " + pluralizeHelpers.pluralize(indexes.length, "index", "indexes", true) + "?";
    const subtitle =
        indexes.length === 1 ? (
            <>You&apos;re {gerund} index:</>
        ) : (
            <>
                You&apos;re {gerund} <strong>{indexes.length}</strong> indexes:
            </>
        );

    const showContextSelector = locations.length > 1;

    const onSubmit = () => {
        onConfirm(selectedLocations);
        toggle();
    };

    return (
        <Modal isOpen toggle={toggle} wrapClassName="bs5">
            <ModalHeader toggle={toggle}>{title}</ModalHeader>
            <ModalBody>
                {subtitle}
                <ul style={{ maxHeight: "200px", overflowY: "auto" }}>
                    {indexes.map((index) => (
                        <li key={index.name} className="padding-xs">
                            {index.name}
                        </li>
                    ))}
                </ul>

                {showContextSelector && (
                    <div>
                        <p>Select context:</p>
                        <MultipleDatabaseLocationSelector
                            locations={locations}
                            selectedLocations={selectedLocations}
                            setSelectedLocations={setSelectedLocations}
                        />
                    </div>
                )}
            </ModalBody>
            <ModalFooter>
                <Button color="secondary" onClick={toggle}>
                    Cancel
                </Button>
                <Button color="danger" onClick={onSubmit}>
                    {infinitive}
                </Button>
            </ModalFooter>
        </Modal>
    );
}

function getInfinitiveForType(type: operationType) {
    return capitalize(type);
}

function getGerund(type: operationType) {
    switch (type) {
        case "disable":
            return "disabling";
        case "enable":
            return "enabling";
        case "pause":
            return "pausing";
        case "resume":
            return "resuming";
        default:
            assertUnreachable(type);
    }
}
