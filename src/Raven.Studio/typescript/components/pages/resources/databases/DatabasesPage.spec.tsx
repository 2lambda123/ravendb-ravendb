import { rtlRender } from "test/rtlTestUtils";
import React from "react";
import { composeStories } from "@storybook/testing-react";

import * as stories from "./DatabasesPage.stories";

const { Single, Cluster, Sharded, DifferentNodeStates, WithLoadError } = composeStories(stories);

describe("DatabasesPage", function () {
    it("can render single view", async () => {
        const { screen } = rtlRender(<Single />);

        await screen.findByText(/Manage group/i);
        await screen.findByText("3 Indexing errors");
    });

    it("can render cluster view", async () => {
        const { screen } = rtlRender(<Cluster />);

        await screen.findByText(/Manage group/i);

        await screen.findByText("9 Indexing errors");
    });

    it("can render load error", async () => {
        const { screen, fireClick } = rtlRender(<WithLoadError />);

        await screen.findByText(/Manage group/i);

        await fireClick(await screen.findByTitle(/Toggle distribution/i));
        await screen.findByText(/Database has load errors/i);
    });

    it("can render sharded view", async () => {
        const { screen } = rtlRender(<Sharded />);

        await screen.findByText(/Manage group/i);

        await screen.findByText("18 Indexing errors");
    });

    it("can render node statuses", async () => {
        const { screen } = rtlRender(<DifferentNodeStates />);

        expect(await screen.findAllByText(/Manage group/i)).toHaveLength(2);

        await screen.findByText("9 Indexing errors");
        await screen.findByText("18 Indexing errors");
    });
});
