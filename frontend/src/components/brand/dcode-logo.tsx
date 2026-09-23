"use client";

import { useId } from "react";

export interface DCodeLogoProps {
  size?: number | string;
  className?: string;
  title?: string;
}

export function DCodeLogo({ size = 32, className, title }: DCodeLogoProps) {
  const maskId = `dcode-mark-${useId().replaceAll(":", "")}`;

  return (
    <svg
      className={className}
      width={size}
      height={size}
      viewBox="0 0 64 64"
      fill="none"
      role={title ? "img" : undefined}
      aria-hidden={title ? undefined : true}
      aria-label={title}
      xmlns="http://www.w3.org/2000/svg"
    >
      <mask id={maskId} maskUnits="userSpaceOnUse" x="4" y="4" width="56" height="58">
        <path d="M7 5H34C49.5 5 59 15.8 59 32C59 48.2 49.5 59 34 59H19L28 50H34C44.2 50 50 43.4 50 32C50 20.6 44.2 14 34 14H17V42H28L7 62V5Z" fill="white" />
        <path d="M17 14H34C44.2 14 50 20.6 50 32C50 43.4 44.2 50 34 50H28L36 42H25V22H34C39.2 22 42 25.5 42 32C42 38.5 39.2 42 34 42H17V14Z" fill="black" />
      </mask>
      <rect x="4" y="4" width="56" height="58" fill="var(--color-primary)" mask={`url(#${maskId})`} />
      <path d="M24 24L32.5 32L24 40" stroke="white" strokeWidth="5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M36 40H45" stroke="var(--color-primary)" strokeWidth="5" strokeLinecap="round" />
    </svg>
  );
}
