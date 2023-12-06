﻿import React, { useState } from "react";
import { shardingTodo } from "common/developmentHelper";
import { IndexStatus, IndexFilterCriteria, IndexType } from "components/models/indexes";
import { Button, Input, PopoverBody, UncontrolledPopover } from "reactstrap";
import produce from "immer";
import { Icon } from "components/common/Icon";
import { MultiCheckboxToggle } from "components/common/MultiCheckboxToggle";
import { InputItem } from "components/models/common";
import { Switch } from "components/common/Checkbox";
import { SortDropdown, SortDropdownRadioList, sortItem } from "components/common/SortDropdown";

interface IndexFilterProps {
    filter: IndexFilterCriteria;
    setFilter: (x: IndexFilterCriteria) => void;
    filterByStatusOptions: InputItem<IndexStatus>[];
    filterByTypeOptions: InputItem<IndexType>[];
    indexesCount: number;
}

export default function IndexFilter(props: IndexFilterProps) {
    const { filter, setFilter, filterByStatusOptions, filterByTypeOptions, indexesCount } = props;

    shardingTodo();

    /* TODO
    let totalProcessedPerSecond = 0;

    this.indexGroups().forEach(indexGroup => {
        const indexesInGroup = indexGroup.indexes().filter(i => !i.filteredOut());
        indexesCount += indexesInGroup.length;

        totalProcessedPerSecond += _.sum(indexesInGroup
            .filter(i => i.progress() || (i.replacement() && i.replacement().progress()))
            .map(i => {
                let sum = 0;

                const progress = i.progress();
                if (progress) {
                    sum += progress.globalProgress().processedPerSecond();
                }

                const replacement = i.replacement();
                if (replacement) {
                    const replacementProgress = replacement.progress();
                    if (replacementProgress) {
                        sum += replacementProgress.globalProgress().processedPerSecond();
                    }
                }

                return sum;
            }));
    });
    */

    /* TODO
    const indexingErrorsOnlyPart = filter.showOnlyIndexesWithIndexingErrors ? (
        <>
            <Badge pill color="warning" className="mx-1">
                indexing errors only
            </Badge>{" "}
        </>
    ) : (
        ""
    );*/

    const onSearchTextChange = (searchText: string) => {
        setFilter(
            produce(filter, (draft) => {
                draft.searchText = searchText;
            })
        );
    };

    const onSearchStatusesChange = (statuses: IndexStatus[]) => {
        setFilter(
            produce(filter, (draft) => {
                draft.statuses = statuses;
            })
        );
    };

    const onSearchTypesChange = (types: IndexType[]) => {
        setFilter(
            produce(filter, (draft) => {
                draft.types = types;
            })
        );
    };

    const toggleAutoRefreshSelection = () => {
        setFilter(
            produce(filter, (draft) => {
                draft.autoRefresh = !draft.autoRefresh;
            })
        );
    };

    const sortBy: sortItem[] = [
        { value: "alphabetically", label: "Alphabetically" },
        { value: "creationDate", label: "Creation date" },
        { value: "lastIndexedDate", label: "Last indexed date" },
        { value: "lastQueryDate", label: "Last query date" },
    ];

    const sortDirection: sortItem[] = [
        { value: "asc", label: "Ascending", icon: "arrow-thin-top" },
        { value: "desc", label: "Descending", icon: "arrow-thin-bottom" },
    ];

    const groupBy: sortItem[] = [
        { value: "byCollection", label: "By collection" },
        { value: "none", label: "none" },
    ];

    const [selectedSortBy, setSelectedSortBy] = useState<string>(sortBy[0].value);
    const [selectedSortDirection, setSelectedSortDirection] = useState<string>(sortDirection[0].value);
    const [selectedGroupBy, setSelectedGroupBy] = useState<string>(groupBy[0].value);

    return (
        <div className="hstack flex-wrap align-items-end gap-3 my-3 justify-content-end">
            <div className="flex-grow">
                <div className="small-label ms-1 mb-1">Filter by name</div>
                <div className="clearable-input">
                    <Input
                        type="text"
                        accessKey="/"
                        placeholder="e.g. Orders/ByCompany/*"
                        title="Filter indexes"
                        className="filtering-input"
                        value={filter.searchText}
                        onChange={(e) => onSearchTextChange(e.target.value)}
                    />
                    {filter.searchText && (
                        <div className="clear-button">
                            <Button color="secondary" size="sm" onClick={() => onSearchTextChange("")}>
                                <Icon icon="clear" margin="m-0" />
                            </Button>
                        </div>
                    )}
                </div>
            </div>

            <MultiCheckboxToggle
                inputItems={filterByStatusOptions}
                label="Filter by state"
                selectedItems={filter.statuses}
                setSelectedItems={onSearchStatusesChange}
                selectAll
                selectAllLabel="All"
                selectAllCount={indexesCount}
            />
            <MultiCheckboxToggle
                inputItems={filterByTypeOptions}
                label="Filter by type"
                selectedItems={filter.types}
                setSelectedItems={onSearchTypesChange}
                selectAllCount={indexesCount}
            />
            <div>
                <div className="small-label ms-1 mb-1">Sort & Group</div>

                <SortDropdown
                    label={
                        <>
                            {sortBy.find((item) => item.value === selectedSortBy).label}{" "}
                            {selectedSortDirection === "asc" ? (
                                <Icon icon="arrow-thin-top" margin="ms-1" />
                            ) : (
                                <Icon icon="arrow-thin-bottom" margin="ms-1" />
                            )}{" "}
                            {selectedGroupBy !== "none" && (
                                <span className="ms-2">
                                    {groupBy.find((item) => item.value === selectedGroupBy).label}
                                </span>
                            )}
                        </>
                    }
                >
                    <SortDropdownRadioList
                        radioOptions={sortBy}
                        label="Sort by"
                        selected={selectedSortBy}
                        setSelected={setSelectedSortBy}
                    />
                    <SortDropdownRadioList
                        radioOptions={sortDirection}
                        label="Sort direction"
                        selected={selectedSortDirection}
                        setSelected={setSelectedSortDirection}
                    />
                    <SortDropdownRadioList
                        radioOptions={groupBy}
                        label="Group by"
                        selected={selectedGroupBy}
                        setSelected={setSelectedGroupBy}
                    />
                </SortDropdown>
            </div>
            {/* TODO: `Processing Speed: <strong>${Math.floor(totalProcessedPerSecond).toLocaleString()}</strong> docs / sec`;*/}
            <Switch
                id="autoRefresh"
                toggleSelection={toggleAutoRefreshSelection}
                selected={filter.autoRefresh}
                color="info"
                className="mt-1"
            >
                <span>Auto refresh is {filter.autoRefresh ? "on" : "off"}</span>
            </Switch>
            <UncontrolledPopover target="autoRefresh" trigger="hover" placement="bottom">
                <PopoverBody>
                    Automatically refreshes the list of indexes.
                    <br />
                    Might result in list flickering.
                </PopoverBody>
            </UncontrolledPopover>
        </div>
    );
}
