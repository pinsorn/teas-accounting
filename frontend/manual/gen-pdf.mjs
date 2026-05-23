// Sprint 13g P5 — PDF export (Option A: Playwright headless print).
// Renders the self-contained docs/manual/generated/print.html (built by
// gen-markdown.mjs) and prints to A4 PDF with Thai font.

import { resolve, join } from 'node:path';
import { existsSync, statSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import pw from '@playwright/test';

const MANUAL = resolve(import.meta.dirname, '../../docs/manual');
const PRINT_HTML = join(MANUAL, 'generated', 'print.html');
const OUT_PDF = join(MANUAL, 'AccountProject-User-Manual-TH-v0.5.pdf');

if (!existsSync(PRINT_HTML)) {
  console.error('gen-pdf: missing', PRINT_HTML, '— run manual:md first');
  process.exit(1);
}

const t0 = Date.now();
const browser = await pw.chromium.launch();
const page = await browser.newPage();
await page.goto(pathToFileURL(PRINT_HTML).href, { waitUntil: 'networkidle' });
// Give web fonts (Sarabun) + images a moment to settle.
await page.waitForTimeout(2500);
await page.pdf({
  path: OUT_PDF,
  format: 'A4',
  printBackground: true,
  margin: { top: '14mm', bottom: '14mm', left: '12mm', right: '12mm' },
});
await browser.close();

const mb = (statSync(OUT_PDF).size / 1024 / 1024).toFixed(2);
console.log(`gen-pdf: ${OUT_PDF} (${mb} MB) in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
