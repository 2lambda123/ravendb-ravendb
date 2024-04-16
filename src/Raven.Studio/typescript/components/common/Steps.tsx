﻿import * as React from "react";
import classNames from "classnames";
import "./Steps.scss";
import { Icon } from "./Icon";

interface StepsProps {
    current: number;
    steps: string[];
    onClick: (stepNum: number) => void;
    className?: string;
}

// TODO show invalid state
export default function Steps(props: StepsProps) {
    const { current, steps, onClick, className } = props;

    const stepNodes = steps.map((stepName, i) => {
        const classes = classNames({
            "steps-item": true,
            done: i < current,
            active: i === current,
        });

        const stepItem = (
            <div key={"step-" + i} className={classes} onClick={() => onClick(i)}>
                <div className="step-bullet">
                    <Icon icon="arrow-thin-bottom" margin="m-0" className="bullet-icon-active" />
                    <Icon icon="check" margin="m-0" className="bullet-icon-done" />
                </div>
                <span className="steps-label small-label">{stepName}</span>
            </div>
        );

        const spacer = <div className="steps-spacer" />;

        return (
            <React.Fragment key={"step-" + i}>
                {stepItem}
                {i !== steps.length - 1 && spacer}
            </React.Fragment>
        );
    });

    return <div className={classNames("steps", className)}>{stepNodes}</div>;
}
