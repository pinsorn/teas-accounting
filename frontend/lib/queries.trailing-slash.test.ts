import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// WP2.3 (F21) — root cause (verified live via curl against `next dev`): a trailing-slash proxy
// path (e.g. POST /api/proxy/vendor-invoices/) 308-redirects (next.config.ts has no
// trailingSlash override, default false) to the no-slash path, producing the double-POST /
// hanging-form symptom. This is a source-string regression guard (a path-string unit test, as
// the design explicitly allows over a full Playwright request-count assertion) over every file
// that had an offending `apiPost`/`apiPut` create call — asserts NONE remain.
const FILES = ['queries.ts', '../app/(dashboard)/payment-vouchers/new/page.tsx'];

describe('WP2.3 — no trailing-slash create POST/PUT paths', () => {
  for (const rel of FILES) {
    it(`${rel} has no apiPost/apiPut call with a trailing-slash static path`, () => {
      const abs = path.resolve(path.dirname(fileURLToPath(import.meta.url)), rel);
      const src = readFileSync(abs, 'utf8');
      const offenders = src.match(/api(Post|Put)[<(][^)]*?'[a-z][a-z-]*\/'/g) ?? [];
      expect(offenders).toEqual([]);
    });
  }
});
