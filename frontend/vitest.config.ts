import path from 'node:path';
import { configDefaults, defineConfig } from 'vitest/config';

// WP2.2/2.3/2.4 (2026-07-14) — the FIRST vitest run to import lib/api.ts's dependency chain
// (lib/api/errors.ts already used '@/lib/api' pre-existing this branch) surfaced that plain
// `vitest` never resolved the '@/*' alias tsconfig.json declares (Next's own bundler resolves
// it at build time; vitest is a separate Vite instance with no config at all until now). Mirror
// tsconfig.json's paths exactly — no new dependency (vite is already a vitest transitive dep).
export default defineConfig({
  resolve: {
    alias: {
      '@': path.resolve(__dirname, '.'),
    },
  },
  test: {
    // e2e/ and manual/ are Playwright specs (their own playwright.config.ts testDir) — vitest's
    // default include glob otherwise collects them too and fails every one with "Playwright
    // Test did not expect test() to be called here" (pre-existing collision, just never visible
    // before this repo had a vitest.config.ts at all).
    // F-B (2026-07 review fix) — spread configDefaults.exclude first: a bare array here REPLACES
    // vitest's built-ins (.next/, dist/, .git/, etc.), not merges with them.
    exclude: [...configDefaults.exclude, 'e2e/**', 'manual/**'],
  },
});
