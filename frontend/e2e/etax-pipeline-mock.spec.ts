import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice } from './_helpers';

// Sprint 13c — Tier-1 e-Tax pipeline end-to-end. Requires the full Tier-1
// stack: MailHog (docker compose -f docker-compose.dev.yml up -d mailhog) +
// the API run with ETax:Enabled=true + ETax:AutoSendOnTaxInvoicePost=true +
// a dev signing cert (dev-tools/gen-test-cert.sh) + ETax:Email→MailHog.
//
// The standard two-pass gate harness starts only API:5080 + next:3000 (no
// Docker/MailHog, ETax disabled), so this spec SKIPS cleanly there — same
// honest discipline as the PostgresFixture SkipReason / non-VAT-mode split.
// It runs green in a real Tier-1 environment (also the manual "Tier 1 startup
// smoke" gate). Never a fake pass.
const MAILHOG = process.env.MAILHOG_URL ?? 'http://localhost:8025';
const API = '/api/proxy';

test('e-Tax pipeline: post TI → signed XML emailed (MailHog) → audit row SendOk', async ({ page }) => {
  // Tier-1 availability probe — skip if MailHog is not up.
  let mailhogUp = false;
  try {
    const r = await page.request.get(`${MAILHOG}/api/v2/messages`, { timeout: 3000 });
    mailhogUp = r.ok();
  } catch { /* not running */ }
  test.skip(!mailhogUp,
    'Tier-1 MailHog not reachable — run docker-compose.dev.yml + ETax-enabled API. ' +
    'Validated by the manual Tier-1 startup-smoke gate.');

  await login(page, 'admin');

  // Clear the MailHog inbox so the assertion is unambiguous.
  await page.request.delete(`${MAILHOG}/api/v1/messages`);

  const tiId = await createAndPostTaxInvoice(page);

  // Pipeline is post-commit best-effort; poll the audit endpoint.
  let rows: Array<{ outcome: string; redirectApplied: boolean }> = [];
  await expect.poll(async () => {
    const res = await page.request.get(`${API}/etax/submissions?tax_invoice_id=${tiId}`);
    rows = res.ok() ? await res.json() : [];
    return rows.length;
  }, { timeout: 15_000 }).toBeGreaterThan(0);

  expect(rows[0]?.outcome).toBe('SendOk');

  // MailHog captured at least one message; subject carries the TI doc number.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const inbox: any = await (await page.request.get(`${MAILHOG}/api/v2/messages`)).json();
  expect(inbox.total).toBeGreaterThanOrEqual(1);
  const msg = inbox.items[0];
  const subject = String(msg?.Content?.Headers?.Subject?.[0] ?? '');
  expect(subject).toMatch(/e-Tax Invoice/i);
  const parts: Array<{ Body?: string }> = msg?.MIME?.Parts ?? [];
  const body = parts.map((p) => p.Body ?? '').join('');
  expect(body.toLowerCase()).toContain('etax.xml');   // XML attachment present
});
