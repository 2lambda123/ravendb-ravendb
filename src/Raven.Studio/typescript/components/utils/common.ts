﻿import { MouseEvent, MouseEventHandler } from "react";
import { Story, StoryFn } from "@storybook/react";
import { RestorePoint, loadableData } from "components/models/common";
import generalUtils from "common/generalUtils";
import moment from "moment";
import { yupObjectSchema } from "components/utils/yupUtils";
import * as yup from "yup";
import { SelectOption } from "components/common/select/Select";

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

export function createFailureState(error?: string): loadableData<any> {
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

export async function tryHandleSubmit<T>(promise: () => Promise<T>) {
    try {
        return await promise();
    } catch (e) {
        console.error(e);
    }
}

// source: https://stackoverflow.com/a/55266531
type AtLeastOne<T> = [T, ...T[]];

export const exhaustiveStringTuple =
    <T extends string>() =>
    <L extends AtLeastOne<T>>(
        ...x: L extends any ? (Exclude<T, L[number]> extends never ? L : Exclude<T, L[number]>[]) : never
    ) =>
        x;
// ---

export const milliSecondsInWeek = 1000 * 3600 * 24 * 7;

export function mapRestorePointFromDto(dto: Raven.Server.Documents.PeriodicBackup.Restore.RestorePoint): RestorePoint {
    let backupType = "";
    if (dto.IsSnapshotRestore) {
        if (dto.IsIncremental) {
            backupType = "Incremental ";
        }
        backupType += "Snapshot";
    } else if (dto.IsIncremental) {
        backupType = "Incremental";
    } else {
        backupType = "Full";
    }

    return {
        dateTime: moment(dto.DateTime).format(generalUtils.dateFormat),
        location: dto.Location,
        fileName: dto.FileName,
        isSnapshotRestore: dto.IsSnapshotRestore,
        isIncremental: dto.IsIncremental,
        isEncrypted: dto.IsEncrypted,
        filesToRestore: dto.FilesToRestore,
        databaseName: dto.DatabaseName,
        nodeTag: dto.NodeTag || "-",
        backupType,
    };
}

export const restorePointSchema = yupObjectSchema<RestorePoint | null>({
    dateTime: yup.string().required(),
    location: yup.string().required(),
    fileName: yup.string().required(),
    isSnapshotRestore: yup.boolean().required(),
    isIncremental: yup.boolean().required(),
    isEncrypted: yup.boolean().required(),
    filesToRestore: yup.number().required(),
    databaseName: yup.string().required(),
    nodeTag: yup.string().required(),
    backupType: yup.string().required(),
});

export const availableGlacierRegions: SelectOption<string>[] = [
    { label: "Africa (Cape Town) - af-south-1", value: "af-south-1" },
    { label: "Asia Pacific (Hong Kong) - ap-east-1", value: "ap-east-1" },
    { label: "Asia Pacific (Jakarta) - ap-southeast-3", value: "ap-southeast-3" },
    { label: "Asia Pacific (Mumbai) - ap-south-1", value: "ap-south-1" },
    { label: "Asia Pacific (Osaka) - ap-northeast-3", value: "ap-northeast-3" },
    { label: "Asia Pacific (Seoul) - ap-northeast-2", value: "ap-northeast-2" },
    { label: "Asia Pacific (Singapore) - ap-southeast-1", value: "ap-southeast-1" },
    { label: "Asia Pacific (Sydney) - ap-southeast-2", value: "ap-southeast-2" },
    { label: "Asia Pacific (Tokyo) - ap-northeast-1", value: "ap-northeast-1" },
    { label: "AWS GovCloud (US-East) - us-gov-east-1", value: "us-gov-east-1" },
    { label: "AWS GovCloud (US-West) - gov-west-1", value: "us-gov-west-1" },
    { label: "Canada (Central) - ca-central-1", value: "ca-central-1" },
    { label: "China (Beijing) - cn-north-1", value: "cn-north-1" },
    { label: "China (Ningxia) - cn-northwest-1", value: "cn-northwest-1" },
    { label: "Europe (Frankfurt) - eu-central-1", value: "eu-central-1" },
    { label: "Europe (Ireland) - eu-west-1", value: "eu-west-1" },
    { label: "Europe (London) - eu-west-2", value: "eu-west-2" },
    { label: "Europe (Milan) - eu-south-1", value: "eu-south-1" },
    { label: "Europe (Paris) - eu-west-3", value: "eu-west-3" },
    { label: "Europe (Stockholm) - eu-north-1", value: "eu-north-1" },
    { label: "Israel (Tel Aviv) - il-central-1", value: "il-central-1" },
    { label: "Middle East (Bahrain) - me-south-1", value: "me-south-1" },
    { label: "South America (São Paulo) - sa-east-1", value: "sa-east-1" },
    { label: "US East (N. Virginia) - us-east-1", value: "us-east-1" },
    { label: "US East (Ohio) - us-east-2", value: "us-east-2" },
    { label: "US West (N. California) - us-west-1", value: "us-west-1" },
    { label: "US West (Oregon) - us-west-2", value: "us-west-2" },
];

export const availableS3Regions: SelectOption<string>[] = _.sortBy(
    [
        ...availableGlacierRegions,
        { label: "Asia Pacific (Hyderabad) - ap-south-2", value: "ap-south-2" },
        { label: "Asia Pacific (Melbourne) - ap-southeast-4", value: "ap-southeast-4" },
        { label: "Europe (Spain) - eu-south-2", value: "eu-south-2" },
        { label: "Europe (Zurich) - eu-central-2", value: "eu-central-2" },
        { label: "Middle East (UAE) - me-central-1", value: "me-central-1" },
    ],
    [(region) => region.label.toLowerCase()]
);
