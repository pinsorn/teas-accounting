import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// R2 Leg3 (throwaway, findings-only) — permission (403) + malformed-payload probes for
// Fixed Assets. Direct API calls only, per the guard-bypass-probe carve-out. NEVER commit.
test.setTimeout(60_000);

test('permission probe: rbac_sales_staff (no fixedasset.* perms) gets 403 on create/run/dispose', async ({ page }) => {
  await login(page, 'rbac_sales_staff');

  const create = await page.request.post('/api/proxy/fixed-assets', {
    data: {
      name: 'SHOULD-NOT-CREATE', category: null, acquireDate: '2026-08-01', vendorInvoiceId: null,
      cost: 1000, salvageValue: 0, usefulLifeMonths: 12, depreciationStartDate: null,
      assetCostAccountId: null, accumDepAccountId: null, depExpenseAccountId: null, notes: null, businessUnitId: null,
    },
  });
  console.log(`>>> PERM-PROBE create status=${create.status()} body=${JSON.stringify(await create.json().catch(() => null))}`);
  expect(create.status()).toBe(403);

  const run = await page.request.post('/api/proxy/depreciation-runs', { data: { year: 2026, month: 8 } });
  console.log(`>>> PERM-PROBE depreciation-run status=${run.status()} body=${JSON.stringify(await run.json().catch(() => null))}`);
  expect(run.status()).toBe(403);

  // Asset A (fixed_asset_id=1) is Active from the lifecycle spec — attempt dispose as low-priv user.
  const dispose = await page.request.post('/api/proxy/fixed-assets/1/dispose', {
    data: { disposalDate: '2026-08-19', proceeds: 0, vatAmount: null, buyerName: null },
  });
  console.log(`>>> PERM-PROBE dispose status=${dispose.status()} body=${JSON.stringify(await dispose.json().catch(() => null))}`);
  expect(dispose.status()).toBe(403);
});

test('malformed probes: negative cost, zero useful life -> typed 400, not raw 500', async ({ page }) => {
  await login(page);

  const negCost = await page.request.post('/api/proxy/fixed-assets', {
    data: {
      name: 'R2L3-Malformed-NegCost', category: null, acquireDate: '2026-08-01', vendorInvoiceId: null,
      cost: -500, salvageValue: 0, usefulLifeMonths: 12, depreciationStartDate: null,
      assetCostAccountId: null, accumDepAccountId: null, depExpenseAccountId: null, notes: null, businessUnitId: null,
    },
  });
  console.log(`>>> MALFORMED negative-cost status=${negCost.status()} body=${JSON.stringify(await negCost.json().catch(() => null))}`);

  const zeroLife = await page.request.post('/api/proxy/fixed-assets', {
    data: {
      name: 'R2L3-Malformed-ZeroLife', category: null, acquireDate: '2026-08-01', vendorInvoiceId: null,
      cost: 1000, salvageValue: 0, usefulLifeMonths: 0, depreciationStartDate: null,
      assetCostAccountId: null, accumDepAccountId: null, depExpenseAccountId: null, notes: null, businessUnitId: null,
    },
  });
  console.log(`>>> MALFORMED zero-life status=${zeroLife.status()} body=${JSON.stringify(await zeroLife.json().catch(() => null))}`);

  // Bonus: salvage > cost (validator rule) and negative useful life.
  const salvageOverCost = await page.request.post('/api/proxy/fixed-assets', {
    data: {
      name: 'R2L3-Malformed-SalvageOverCost', category: null, acquireDate: '2026-08-01', vendorInvoiceId: null,
      cost: 1000, salvageValue: 5000, usefulLifeMonths: 12, depreciationStartDate: null,
      assetCostAccountId: null, accumDepAccountId: null, depExpenseAccountId: null, notes: null, businessUnitId: null,
    },
  });
  console.log(`>>> MALFORMED salvage-over-cost status=${salvageOverCost.status()} body=${JSON.stringify(await salvageOverCost.json().catch(() => null))}`);
});
