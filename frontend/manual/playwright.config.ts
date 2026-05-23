import { defineConfig } from '@playwright/test';

// Sprint 13g — manual capture run (NOT the e2e suite). Assumes backend :5080
// + frontend :3000 + manual-demo seed are already up (pre-condition).
export default defineConfig({
  testDir: '.',
  testMatch: 'run-capture.spec.ts',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  timeout: 180_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: 'http://localhost:3000',
    viewport: { width: 1440, height: 900 },
    locale: 'th-TH',
    actionTimeout: 15_000,
    navigationTimeout: 30_000,
  },
});
