// Sprint 13g — embed a pre-rendered "filled RD form" PDF page (PNG) into the
// page as a centred image on a viewer-grey backdrop, so the normal capture()
// screenshots it like any other step. The PNGs are produced out-of-band by
// `manual/render-pdf-samples.py` (it calls the backend PDF endpoints + renders
// page 1 with PyMuPDF) — a re-capture run does NOT regenerate them, so each
// caller must guard: showPdfSample throws a clear "run the renderer first"
// error when the file is absent (mirrors the 06.01 dev-data guard).
import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Page } from '@playwright/test';

// frontend/manual/lib → ../pdf-samples → frontend/manual/pdf-samples
const SAMPLES_DIR = resolve(__dirname, '../pdf-samples');

export async function showPdfSample(page: Page, file: string): Promise<void> {
  const abs = resolve(SAMPLES_DIR, file);
  if (!existsSync(abs)) {
    throw new Error(
      `PDF sample "${file}" missing. Run \`python manual/render-pdf-samples.py\` ` +
      `(backend :5080 + co2 demo seed up) before capturing chapter 7.`,
    );
  }
  const b64 = readFileSync(abs).toString('base64');
  await page.setContent(
    '<!doctype html><html><body style="margin:0;height:100vh;display:flex;' +
    'align-items:center;justify-content:center;background:#525659">' +
    `<img alt="${file}" src="data:image/png;base64,${b64}" ` +
    'style="max-height:96vh;max-width:96vw;box-shadow:0 0 24px rgba(0,0,0,.55)"></body></html>',
    { waitUntil: 'load' },
  );
}
