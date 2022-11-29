import { mockServices } from "./mocks/MockServices";
import React from "react";
import { ServiceProvider } from "../components/hooks/useServices";

export function storybookContainerPublicContainer(storyFn: any) {
    return (
        <div className="container">
            <div className="padding">{storyFn()}</div>
        </div>
    );
}

export function forceStoryRerender() {
    return {
        key: new Date().toISOString(),
    };
}

export function withStorybookContexts(storyFn: any) {
    return (
        <div style={{ margin: "50px" }}>
            <ServiceProvider services={mockServices.context}>{storyFn()}</ServiceProvider>
        </div>
    );
}

export function withBootstrap5(storyFn: any) {
    return (
        <div className="bs5">
            {storyFn()}
        </div>
    );
}
