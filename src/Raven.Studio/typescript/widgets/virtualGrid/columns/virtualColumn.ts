﻿/// <reference path="../../../../typings/tsd.d.ts"/>

interface virtualColumn {

    /**
     * The width string to use for the column. Example: "20px" or "10%".
     */
    width: string; // "20px" or "10%"

    /**
     * The text or HTML to display as the column header.
     */
    header: string;

    /**
     * Renders a cell for this column. Returns a string, either text or HTML, containing the content.
     */
    renderCell(item: Object, isSelected: boolean): string;
}

export = virtualColumn;