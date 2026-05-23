// Sprint 13g — capture(): spotlight a target, screenshot, record.
//
// Captures are written under docs/manual/captures/ (NOT frontend/manual/
// captures as the spec drafted) because MkDocs only serves files under its
// docs_dir (docs/manual). Flagged in Report-Backend24. Layout:
//   docs/manual/captures/<chap>/<walkthroughId>/<stepId>.png
// where <chap> = first 2 chars of the walkthrough id ('02' for '02.05').

import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import type { Page } from '@playwright/test';
import type { CaptureFn, CaptureOpts } from './walkthrough';

export interface StepRecord {
  walkthroughId: string;
  stepId: string;
  caption: string;
  imageRel: string; // path relative to docs/manual (for generated markdown)
}

// frontend/manual/lib → ../../../ → code/ → code/docs/manual/captures
const CAPTURES_ROOT = resolve(__dirname, '../../../docs/manual/captures');

type Rect = { x: number; y: number; w: number; h: number };

function applySpotlight(
  args: [Rect | null, 'up' | 'down' | 'left' | 'right' | null],
) {
  const [rect, arrow] = args;
  for (const id of ['__m_ring', '__m_arrow']) {
    document.getElementById(id)?.remove();
  }
  if (!rect) return;
  const ring = document.createElement('div');
  ring.id = '__m_ring';
  Object.assign(ring.style, {
    position: 'fixed',
    left: `${rect.x}px`,
    top: `${rect.y}px`,
    width: `${rect.w}px`,
    height: `${rect.h}px`,
    outline: '3px solid #f59e0b',
    borderRadius: '4px',
    boxShadow: '0 0 0 9999px rgba(0,0,0,0.5)',
    zIndex: '2147483646',
    pointerEvents: 'none',
  } as CSSStyleDeclaration);
  document.body.appendChild(ring);
  if (!arrow) return;
  const a = document.createElement('div');
  a.id = '__m_arrow';
  const S = 22;
  const C = '#f59e0b';
  const cx = rect.x + rect.w / 2;
  const cy = rect.y + rect.h / 2;
  const st: Partial<CSSStyleDeclaration> = {
    position: 'fixed',
    zIndex: '2147483647',
    width: '0',
    height: '0',
    pointerEvents: 'none',
  };
  if (arrow === 'down') {
    Object.assign(st, {
      left: `${cx - S}px`, top: `${rect.y - S - 6}px`,
      borderLeft: `${S}px solid transparent`,
      borderRight: `${S}px solid transparent`,
      borderTop: `${S}px solid ${C}`,
    });
  } else if (arrow === 'up') {
    Object.assign(st, {
      left: `${cx - S}px`, top: `${rect.y + rect.h + 6}px`,
      borderLeft: `${S}px solid transparent`,
      borderRight: `${S}px solid transparent`,
      borderBottom: `${S}px solid ${C}`,
    });
  } else if (arrow === 'right') {
    Object.assign(st, {
      left: `${rect.x - S - 6}px`, top: `${cy - S}px`,
      borderTop: `${S}px solid transparent`,
      borderBottom: `${S}px solid transparent`,
      borderLeft: `${S}px solid ${C}`,
    });
  } else {
    Object.assign(st, {
      left: `${rect.x + rect.w + 6}px`, top: `${cy - S}px`,
      borderTop: `${S}px solid transparent`,
      borderBottom: `${S}px solid transparent`,
      borderRight: `${S}px solid ${C}`,
    });
  }
  Object.assign(a.style, st);
  document.body.appendChild(a);
}

function clearSpotlight() {
  for (const id of ['__m_ring', '__m_arrow']) {
    document.getElementById(id)?.remove();
  }
}

export function makeCapture(
  page: Page,
  walkthroughId: string,
  records: StepRecord[],
): CaptureFn {
  const chap = walkthroughId.slice(0, 2);

  return async function capture(stepId: string, opts: CaptureOpts) {
    let rect: Rect | null = null;

    if (opts.highlight) {
      const loc = page.locator(opts.highlight).first();
      try {
        await loc.waitFor({ state: 'visible', timeout: 8000 });
        await loc.scrollIntoViewIfNeeded({ timeout: 4000 }).catch(() => {});
        const box = await loc.boundingBox();
        if (box) rect = { x: box.x, y: box.y, w: box.width, h: box.height };
      } catch {
        // Selector didn't resolve headless — screenshot without spotlight
        // instead of failing the whole walkthrough (anti-flake; reported).
        rect = null;
      }
    }

    await page.waitForTimeout(1000); // settle (animations/toasts)

    const arg: [Rect | null, 'up' | 'down' | 'left' | 'right' | null] =
      [rect, opts.arrow ?? null];
    await page.evaluate(applySpotlight, arg).catch(() => {});

    const abs = resolve(CAPTURES_ROOT, chap, walkthroughId, `${stepId}.png`);
    mkdirSync(dirname(abs), { recursive: true });
    await page.screenshot({ path: abs }); // viewport (config-sized)

    await page.evaluate(clearSpotlight).catch(() => {});

    records.push({
      walkthroughId,
      stepId,
      caption: opts.caption,
      imageRel: `captures/${chap}/${walkthroughId}/${stepId}.png`,
    });
  };
}
