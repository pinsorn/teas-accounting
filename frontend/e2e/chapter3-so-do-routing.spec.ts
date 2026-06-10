import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 13e P1 regression — /sales-orders/new and /delivery-orders/new used
// to fall through to [id]/page.tsx → parseInt("new")=NaN → GET /…/NaN → 404
// → infinite spinner. The dedicated new/page.tsx files (now real P4 forms)
// must render immediately and never fire the NaN fetch.
test('SO /new renders the real form, no NaN fetch', async ({ page }) => {
  await login(page);

  const bad: string[] = [];
  page.on('request', (r) => {
    if (/\/sales-orders\/NaN\b/.test(r.url())) bad.push(r.url());
  });

  await page.goto('/sales-orders/new');
  await expect(page.getByRole('heading', { name: /สร้างใบสั่งขาย/ })).toBeVisible({
    timeout: 15_000,
  });
  // Customer picker trigger (modal opener) — two generations: the create-form
  // redesign's PartySelectBox empty-state "เลือกลูกค้า" button OR the legacy
  // CustomerSelector whose accessible name is its placeholder text.
  await expect(
    page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first(),
  ).toBeVisible();
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
  expect(bad, `unexpected NaN fetch: ${bad.join(', ')}`).toHaveLength(0);
});

test('DO /new renders the real form, no NaN fetch', async ({ page }) => {
  await login(page);

  const bad: string[] = [];
  page.on('request', (r) => {
    if (/\/delivery-orders\/NaN\b/.test(r.url())) bad.push(r.url());
  });

  await page.goto('/delivery-orders/new');
  await expect(page.getByRole('heading', { name: /สร้างใบส่งของ/ })).toBeVisible({
    timeout: 15_000,
  });
  // Customer picker trigger (modal opener) — two generations: the create-form
  // redesign's PartySelectBox empty-state "เลือกลูกค้า" button OR the legacy
  // CustomerSelector whose accessible name is its placeholder text.
  await expect(
    page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first(),
  ).toBeVisible();
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
  expect(bad, `unexpected NaN fetch: ${bad.join(', ')}`).toHaveLength(0);
});
