import { defineConfig, devices } from '@playwright/test';

// Stack is started externally (API :5080 + next start :3000) — no webServer here.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  retries: 0,
  workers: 1,
  reporter: [['list']],
  timeout: 30_000,
  use: {
    baseURL: 'http://localhost:3000',
    headless: true,
    trace: 'retain-on-failure',
  },
  // Use the system browser (Edge ships with Windows; Chrome if present) — no
  // Playwright chromium download / version-skew. Override via PW_CHANNEL.
  projects: [
    {
      name: 'system',
      use: { ...devices['Desktop Chrome'], channel: process.env.PW_CHANNEL ?? 'msedge' },
    },
  ],
});
