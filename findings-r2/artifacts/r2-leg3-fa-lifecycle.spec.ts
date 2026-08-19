import { test, expect, type Page, type APIResponse } from '@playwright/test';
import { login } from './_helpers';

// R2 Leg3 (throwaway, findings-only) — Fixed Assets + Depreciation full walkthrough.
// NEVER commit. company_id=1 (Demo Company) only.
test.describe.configure({ mode: 'serial' });
test.setTimeout(120_000);

function bkk(offsetDays = 0): string {
  const d = new Date(Date.now() + offsetDays * 86400000);
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Bangkok' }).format(d);
}
function todayYm(): { y: number; m: number } {
  const [y, m] = bkk().split('-').map(Number);
  return { y, m };
}
function shiftYm(y: number, m: number, delta: number): { y: number; m: number } {
  const total = y * 12 + (m - 1) + delta;
  return { y: Math.floor(total / 12), m: (total % 12) + 1 };
}
const { y: CUR_Y, m: CUR_M } = todayYm();
const PREV = shiftYm(CUR_Y, CUR_M, -1); // July 2026 for co1 (explicitly OPEN)
const CLOSED = shiftYm(CUR_Y, CUR_M, -4); // definitely closed, no row for co1

interface AssetInput {
  name: string; acquireDate: string; cost: number; salvage?: number;
  lifeMonths: number; depStart?: string;
}

async function createDraftAsset(page: Page, a: AssetInput): Promise<number> {
  await page.goto('/fixed-assets/new');
  await page.getByLabel(/^Name$|^ชื่อสินทรัพย์$/).fill(a.name);
  await page.getByLabel(/^Acquire date$|^วันที่ได้มา$/).fill(a.acquireDate);
  await page.getByLabel(/^Cost$|^ราคาทุน$/).fill(String(a.cost));
  if (a.salvage) await page.getByLabel(/^Salvage value$|^มูลค่าซาก$/).fill(String(a.salvage));
  await page.getByRole('spinbutton', { name: /Useful life|อายุการใช้งาน/ }).fill(String(a.lifeMonths));
  if (a.depStart) await page.getByLabel(/^Depreciation start date$|^วันเริ่มคิดค่าเสื่อมราคา$/).fill(a.depStart);
  const [resp] = await Promise.all([
    page.waitForResponse((r) => r.url().includes('/api/proxy/fixed-assets') && r.request().method() === 'POST'),
    page.getByRole('button', { name: /^Save$|^บันทึก$/ }).click(),
  ]);
  await page.waitForURL(/\/fixed-assets\/\d+$/, { timeout: 15000 });
  const body = await resp.json().catch(() => null);
  console.log(`>>> CREATE ${a.name} status=${resp.status()} body=${JSON.stringify(body)}`);
  const m = page.url().match(/\/fixed-assets\/(\d+)$/);
  if (!m) throw new Error('no id in url after save');
  return Number(m[1]);
}

async function activateAsset(page: Page, id: number, tag: string): Promise<APIResponse> {
  await page.goto(`/fixed-assets/${id}`);
  const [resp] = await Promise.all([
    page.waitForResponse((r) => r.url().includes(`/api/proxy/fixed-assets/${id}/activate`) && r.request().method() === 'POST'),
    (async () => {
      await page.getByRole('button', { name: /^Activate$|^เริ่มใช้งาน$/ }).click();
      await page.getByTestId('alert-dialog-confirm').click();
    })(),
  ]);
  console.log(`>>> ACTIVATE ${tag} id=${id} status=${resp.status()}`);
  return resp;
}

async function runDepreciation(page: Page, year: number, month: number): Promise<{ status: number; body: unknown }> {
  await page.goto('/depreciation');
  await page.getByText(/^Year$|^ปี$/).locator('xpath=following::input[1]').fill(String(year));
  await page.getByText(/^Month$|^เดือน$/).locator('xpath=following::select[1]').selectOption(String(month));
  const [resp] = await Promise.all([
    page.waitForResponse((r) => r.url().includes('/api/proxy/depreciation-runs') && r.request().method() === 'POST'),
    (async () => {
      await page.getByRole('button', { name: /^Generate depreciation$|^คิดค่าเสื่อมราคา$/ }).click();
      await page.getByTestId('alert-dialog-confirm').click();
    })(),
  ]);
  const body = await resp.json().catch(() => null);
  console.log(`>>> DEPRECIATION-RUN ${year}-${month} status=${resp.status()} body=${JSON.stringify(body)}`);
  return { status: resp.status(), body };
}

async function disposeAsset(
  page: Page, id: number, tag: string,
  opts: { disposalDate: string; proceeds: number; useVatButton?: boolean },
): Promise<{ status: number; body: unknown }> {
  await page.goto(`/fixed-assets/${id}`);
  await page.getByRole('button', { name: /^Dispose$|^จำหน่ายสินทรัพย์$/ }).click();
  const modal = page.locator('.modal-box');
  await expect(modal).toBeVisible();
  await modal.getByLabel(/Disposal date|วันที่จำหน่าย/).fill(opts.disposalDate);
  await modal.getByLabel(/Proceeds|ราคาขาย/).fill(String(opts.proceeds));
  if (opts.useVatButton) await modal.getByRole('button', { name: '7%' }).click();
  const [resp] = await Promise.all([
    page.waitForResponse((r) => r.url().includes(`/api/proxy/fixed-assets/${id}/dispose`) && r.request().method() === 'POST'),
    modal.getByRole('button', { name: /^Dispose$|^จำหน่ายสินทรัพย์$/ }).click(),
  ]);
  const body = await resp.json().catch(() => null);
  console.log(`>>> DISPOSE ${tag} id=${id} status=${resp.status()} body=${JSON.stringify(body)}`);
  return { status: resp.status(), body };
}

let idA: number, idB: number, idC: number, idD: number;

// Makes every test independently runnable (e.g. via `-g`) by re-resolving the module-scope
// ids from the API by name when a filtered/partial run skipped test 01. Picks the LOWEST
// matching id per name so re-runs stay anchored to the original (already has July's
// depreciation history) instead of drifting to a duplicate created by a later partial re-run.
async function resolveIds(page: Page): Promise<void> {
  if (idA && idB && idC && idD) return;
  const list = (await (await page.request.get('/api/proxy/fixed-assets')).json()) as Array<{ fixedAssetId: number; name: string }>;
  const byName = (n: string) => list.filter((x) => x.name === n).sort((a, b) => a.fixedAssetId - b.fixedAssetId)[0]?.fixedAssetId;
  idA = idA ?? byName('R2L3-A-Laptop');
  idB = idB ?? byName('R2L3-B-Copier');
  idC = idC ?? byName('R2L3-C-RoundTest');
  idD = idD ?? byName('R2L3-D-PreDispose');
  console.log(`>>> RESOLVED-IDS A=${idA} B=${idB} C=${idC} D=${idD}`);
}

test('01 register + activate assets A/B/C/D', async ({ page }) => {
  await login(page);

  // sanity: co1 fixed-assets list should be empty at start (clean slate confirmed via psql pre-flight)
  await page.goto('/fixed-assets');
  await expect(page.locator('table')).toBeVisible();

  idA = await createDraftAsset(page, {
    name: 'R2L3-A-Laptop', acquireDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-05`,
    cost: 24000, salvage: 0, lifeMonths: 24,
  });
  const rA = await activateAsset(page, idA, 'A');
  expect(rA.ok()).toBeTruthy();
  // FA-A guardrail: no VendorInvoiceId -> the cost-not-on-GL warning banner must show
  await expect(page.getByTestId('fa-no-gl-cost-warning')).toBeVisible();

  idB = await createDraftAsset(page, {
    name: 'R2L3-B-Copier', acquireDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-01`,
    cost: 100000, salvage: 10000, lifeMonths: 36,
  });
  expect((await activateAsset(page, idB, 'B')).ok()).toBeTruthy();

  // C: backdated depreciation start into an already-closed month (June), life=3mo so the
  // final SCHEDULED month lands on the current (open) month — tests the rounding plug +
  // "skipped closed month absorbed by the plug" behavior in one asset.
  idC = await createDraftAsset(page, {
    name: 'R2L3-C-RoundTest', acquireDate: `${PREV.y}-${String(PREV.m).padStart(2, '0')}-01`,
    cost: 100, salvage: 0, lifeMonths: 3,
    depStart: `${shiftYm(CUR_Y, CUR_M, -2).y}-${String(shiftYm(CUR_Y, CUR_M, -2).m).padStart(2, '0')}-01`,
  });
  expect((await activateAsset(page, idC, 'C')).ok()).toBeTruthy();

  // D: for the "disposed before a run -> excluded from that run" probe.
  idD = await createDraftAsset(page, {
    name: 'R2L3-D-PreDispose', acquireDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-01`,
    cost: 12000, salvage: 0, lifeMonths: 12,
  });
  expect((await activateAsset(page, idD, 'D')).ok()).toBeTruthy();

  console.log(`>>> IDS A=${idA} B=${idB} C=${idC} D=${idD} CUR=${CUR_Y}-${CUR_M} PREV=${PREV.y}-${PREV.m} CLOSED=${CLOSED.y}-${CLOSED.m}`);
});

test('02 probe: close current-month period BEFORE any run — expect period.depreciation_required, zero side effects', async ({ page }) => {
  await login(page);
  await page.goto('/period-close');
  await page.getByText(/^Year$|^ปี$/).locator('xpath=following::input[1]').fill(String(CUR_Y));
  await expect(page.locator('table')).toBeVisible();

  const monthLabel = new Intl.DateTimeFormat('en', { month: 'long' }).format(new Date(2000, CUR_M - 1, 1));
  const monthLabelTh = new Intl.DateTimeFormat('th', { month: 'long' }).format(new Date(2000, CUR_M - 1, 1));
  const tr = page.locator('tr', { hasText: new RegExp(`${monthLabel}|${monthLabelTh}`) }).first();

  const [resp] = await Promise.all([
    page.waitForResponse((r) => r.url().includes(`/api/proxy/periods/${CUR_Y}/${CUR_M}/close`) && r.request().method() === 'POST'),
    (async () => {
      await tr.getByRole('button', { name: /^Close$|^ปิดงวด$/ }).click();
      await page.getByTestId('alert-dialog-confirm').click();
    })(),
  ]);
  const body = await resp.json().catch(() => null);
  console.log(`>>> CLOSE-BLOCK-PROBE ${CUR_Y}-${CUR_M} status=${resp.status()} body=${JSON.stringify(body)}`);
});

test('03 run depreciation for PREV month (only C due) then re-run (idempotent)', async ({ page }) => {
  await login(page);
  const first = await runDepreciation(page, PREV.y, PREV.m);
  const second = await runDepreciation(page, PREV.y, PREV.m);
  console.log(`>>> PREV-RUN-1=${JSON.stringify(first)} PREV-RUN-2=${JSON.stringify(second)}`);
});

test('04 closed-period refusals: depreciation run + disposal into a definitely-closed month', async ({ page }) => {
  await login(page);
  await resolveIds(page);
  const runResp = await runDepreciation(page, CLOSED.y, CLOSED.m);
  console.log(`>>> CLOSED-MONTH-RUN-PROBE=${JSON.stringify(runResp)}`);

  const disposeResp = await disposeAsset(page, idA, 'A-closed-period-probe', {
    disposalDate: `${CLOSED.y}-${String(CLOSED.m).padStart(2, '0')}-15`, proceeds: 0,
  });
  console.log(`>>> CLOSED-MONTH-DISPOSE-PROBE=${JSON.stringify(disposeResp)}`);
});

test('05 disposal-date-before-acquisition probe (real UI path, OPEN period so not masked by period.closed)', async ({ page }) => {
  await login(page);
  // Sacrificial asset F: acquired this month, disposed with a date in the OPEN prior month
  // (before its own acquire date) — proves/disproves date-order validation with a clean signal.
  const idF = await createDraftAsset(page, {
    name: 'R2L3-F-DateOrderProbe', acquireDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-10`,
    cost: 5000, salvage: 0, lifeMonths: 10,
  });
  expect((await activateAsset(page, idF, 'F')).ok()).toBeTruthy();
  const resp = await disposeAsset(page, idF, 'F-date-order-probe', {
    disposalDate: `${PREV.y}-${String(PREV.m).padStart(2, '0')}-15`, proceeds: 0,
  });
  console.log(`>>> DATE-ORDER-PROBE idF=${idF} ${JSON.stringify(resp)}`);
});

test('06 dispose D BEFORE current-month run, then run current month (A,B,C due; D excluded)', async ({ page }) => {
  await login(page);
  await resolveIds(page);
  const disposeD = await disposeAsset(page, idD, 'D-pre-run', {
    disposalDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-10`, proceeds: 5000, useVatButton: true,
  });
  console.log(`>>> DISPOSE-D-PRE-RUN=${JSON.stringify(disposeD)}`);

  const run1 = await runDepreciation(page, CUR_Y, CUR_M);
  console.log(`>>> CUR-RUN-1=${JSON.stringify(run1)}`);
  const run2 = await runDepreciation(page, CUR_Y, CUR_M);
  console.log(`>>> CUR-RUN-2 (idempotent re-run)=${JSON.stringify(run2)}`);
});

test('07 dispose B (has accumulated depreciation from current-month run) — loss scenario', async ({ page }) => {
  await login(page);
  await resolveIds(page);
  const resp = await disposeAsset(page, idB, 'B-loss', {
    disposalDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-19`, proceeds: 90000, useVatButton: true,
  });
  console.log(`>>> DISPOSE-B=${JSON.stringify(resp)}`);
});

test('08 edit door: unchanged resave of Draft asset E, and refused edit of Active asset A', async ({ page }) => {
  await login(page);
  await resolveIds(page);
  const idE = await createDraftAsset(page, {
    name: 'R2L3-E-EditDoor', acquireDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-01`,
    cost: 8000, salvage: 0, lifeMonths: 8,
  });

  const getResp = await page.request.get(`/api/proxy/fixed-assets/${idE}`);
  const detail = await getResp.json();
  console.log(`>>> EDIT-DOOR E detail before=${JSON.stringify(detail)}`);

  const unchangedBody = {
    name: detail.name, category: detail.category, acquireDate: detail.acquireDate,
    vendorInvoiceId: detail.vendorInvoiceId, cost: detail.cost, salvageValue: detail.salvageValue,
    usefulLifeMonths: detail.usefulLifeMonths, depreciationStartDate: detail.depreciationStartDate,
    assetCostAccountId: detail.assetCostAccountId, accumDepAccountId: detail.accumDepAccountId,
    depExpenseAccountId: detail.depExpenseAccountId, notes: detail.notes, businessUnitId: detail.businessUnitId,
  };
  const putResp = await page.request.put(`/api/proxy/fixed-assets/${idE}`, { data: unchangedBody });
  console.log(`>>> EDIT-DOOR E unchanged-PUT status=${putResp.status()}`);
  const getAfter = await (await page.request.get(`/api/proxy/fixed-assets/${idE}`)).json();
  console.log(`>>> EDIT-DOOR E detail after=${JSON.stringify(getAfter)}`);

  // now try editing ACTIVE asset A — expect a clean refusal, not a silent overwrite.
  const activeBody = {
    name: 'SHOULD-NOT-APPLY', category: null, acquireDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-05`,
    vendorInvoiceId: null, cost: 999999, salvageValue: 0, usefulLifeMonths: 1,
    depreciationStartDate: `${CUR_Y}-${String(CUR_M).padStart(2, '0')}-05`,
    assetCostAccountId: null, accumDepAccountId: null, depExpenseAccountId: null,
    notes: null, businessUnitId: null,
  };
  const putActive = await page.request.put(`/api/proxy/fixed-assets/${idA}`, { data: activeBody });
  const putActiveBody = await putActive.json().catch(() => null);
  console.log(`>>> EDIT-DOOR A(active) edit-attempt status=${putActive.status()} body=${JSON.stringify(putActiveBody)}`);
});

test('09 year-close probe: close-year for the current year — expect year.periods_not_closed (zero mutation)', async ({ page }) => {
  await login(page);
  const resp = await page.request.post(`/api/proxy/periods/${CUR_Y}/close-year`, { data: {} });
  const body = await resp.json().catch(() => null);
  console.log(`>>> YEAR-CLOSE-PROBE ${CUR_Y} status=${resp.status()} body=${JSON.stringify(body)}`);
});
