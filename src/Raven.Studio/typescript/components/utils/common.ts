﻿import { MouseEvent, MouseEventHandler } from "react";
import { Story, StoryFn } from "@storybook/react";
import { loadableData } from "components/models/common";

export function withPreventDefault(action: (...args: any[]) => void): MouseEventHandler<HTMLElement> {
    return (e: MouseEvent<HTMLElement>) => {
        e.preventDefault();
        action();
    };
}

export function createIdleState(): loadableData<any> {
    return {
        data: null,
        status: "idle",
        error: null,
    };
}

export function createSuccessState<T>(data: T): loadableData<T> {
    return {
        data,
        error: null,
        status: "success",
    };
}

export function createLoadingState<T>(previousState?: loadableData<T>): loadableData<T> {
    return {
        error: null,
        data: null,
        ...previousState,
        status: "loading",
    };
}

export function createFailureState(error: string): loadableData<any> {
    return {
        error,
        data: null,
        status: "failure",
    };
}

export async function delay(ms: number) {
    return new Promise((resolve) => setTimeout(resolve, ms));
}

export function databaseLocationComparator(lhs: databaseLocationSpecifier, rhs: databaseLocationSpecifier) {
    return lhs.nodeTag === rhs.nodeTag && lhs.shardNumber === rhs.shardNumber;
}

export function boundCopy<TArgs>(story: StoryFn<TArgs>, args?: TArgs): Story<TArgs> {
    const copy = story.bind({});
    copy.args = args;
    return copy;
}
