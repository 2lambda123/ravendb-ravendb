﻿import React, { ReactNode } from "react";
import classNames from "classnames";
import { Badge } from "reactstrap";

interface HrHeaderProps {
    children?: ReactNode | ReactNode[];
    right?: ReactNode | ReactNode[];
    className?: string;
    count?: number;
}

export function HrHeader(props: HrHeaderProps) {
    const { right, children, className, count, ...rest } = props;

    return (
        <div className="flex-horizontal align-items-center my-3">
            {children && (
                <h5 className={classNames("m-0 me-3 text-uppercase", { className })} {...rest}>
                    {children}
                </h5>
            )}
            {count && (
                <Badge pill className="me-3">
                    {count}
                </Badge>
            )}
            <hr className="flex-grow-1 m-0" />
            {right && <div className="ms-3">{right}</div>}
        </div>
    );
}
