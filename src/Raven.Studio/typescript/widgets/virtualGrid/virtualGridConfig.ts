﻿import virtualColumn = require("widgets/virtualGrid/virtualColumn");
import pagedResult = require("widgets/virtualGrid/pagedResult");

interface virtualGridConfig<T> {

    /**
     * The function that fetches a chunk of items. This function will be invoked by the grid as the user scrolls and loads more items.
     */
    fetcher: (skip: number, take: number) => JQueryPromise<pagedResult<T>>;

    /**
     * Optional. A list of columns to use. If not specified, the columns will be pulled from the first set of loaded items, with priority set on .Id and .Name columns.
     */
    columns?: virtualColumn[];

    /**
     * Whether to show the header containing the column names.
     */
    showColumns?: boolean;

    /**
     * Whether to show the row selection checkbox column. Defaults to true. If so, this will be the first column in the grid.
     */
    showRowSelectionCheckbox?: boolean;

    /**
     * An observable that, when changed, tells the grid to clear the items and refetch.
     */
    resetItems?: KnockoutObservable<any>; 
}

export = virtualGridConfig;
