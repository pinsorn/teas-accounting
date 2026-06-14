// Sprint 13g — manual capture framework.
// Sana's walkthrough .ts files call `walkthrough(meta, body)` at module load;
// importing them registers into this in-memory registry. run-capture.spec.ts
// then iterates the registry (id order), logs in per persona, runs each body
// with a recording `capture`.

import type { Page } from '@playwright/test';

export interface WalkthroughMeta {
  id: string;            // e.g. '02.05'
  title: string;
  chapter: string;       // e.g. '2. ตั้งค่าระบบ'
  intro: string;         // multi-line Thai
  prerequisites: string[];
  persona?: 'admin' | 'accountant' | 'nonvat'; // optional override; default resolved by id
}

export interface CaptureOpts {
  highlight?: string;                       // CSS / Playwright selector
  arrow?: 'up' | 'down' | 'left' | 'right';
  caption: string;                          // Thai caption
}

export type CaptureFn = (stepId: string, opts: CaptureOpts) => Promise<void>;

export interface WalkthroughCtx {
  page: Page;
  capture: CaptureFn;
}

export interface RegisteredWalkthrough {
  meta: WalkthroughMeta;
  body: (ctx: WalkthroughCtx) => Promise<void>;
}

const registry: RegisteredWalkthrough[] = [];

export function walkthrough(
  meta: WalkthroughMeta,
  body: (ctx: WalkthroughCtx) => Promise<void>,
): void {
  registry.push({ meta, body });
}

/** All registered walkthroughs, sorted by id ('01.01' < '01.04' < '02.05'). */
export function getWalkthroughs(): RegisteredWalkthrough[] {
  return [...registry].sort((a, b) => a.meta.id.localeCompare(b.meta.id));
}
