# Fixed Assets

สินทรัพย์ถาวรและการคิดค่าเสื่อมราคารายเดือน.

Fixed-asset master data (register), the disposal/write-off lifecycle, and the monthly depreciation-run engine.

## Fixed Assets
Read gated by `fixed_asset.read`; create/edit by `fixed_asset.manage`; dispose/write-off by `fixed_asset.dispose`.
- `POST /fixed-assets` — create (Draft). **Auth:** `fixed_asset.manage`. Body (key fields): `name`, `category?`, `acquireDate`, `vendorInvoiceId?`, `cost`, `salvageValue`, `usefulLifeMonths`, `depreciationStartDate?` (null → defaults to `acquireDate`), `assetCostAccountId?`, `accumDepAccountId?`, `depExpenseAccountId?`, `notes?`, `businessUnitId?`. → `201` `{ fixed_asset_id }`.
- `PUT /fixed-assets/{id}` — edit (Draft only). **Auth:** `fixed_asset.manage`. → `204`.
- `POST /fixed-assets/{id}/activate` — start depreciating (Draft → Active). **Auth:** `fixed_asset.manage`. → `204`.
- `POST /fixed-assets/{id}/cancel` — cancel a Draft. **Auth:** `fixed_asset.manage`. → `204`.
- `POST /fixed-assets/{id}/dispose` — dispose (computes NBV/gain-loss + posts GL). **Auth:** `fixed_asset.dispose`. Body: `disposalDate`, `proceeds`, `vatAmount?`, `buyerName?`. → `200` `{ fixedAssetId, nbv, gainLoss, journalEntryId, status }`.
- `POST /fixed-assets/{id}/write-off` — write off at zero value. **Auth:** `fixed_asset.dispose`. Body: `date`, `reason`. → `200`.
- `GET /fixed-assets` — list. **Auth:** `fixed_asset.read`. Query: `status?`, `category?`, `from?`, `to?`. → `200`.
- `GET /fixed-assets/{id}` — detail. **Auth:** `fixed_asset.read`. → `200` / `404`.
- `GET /fixed-assets/reports/register` — fixed-asset register as of a date. **Auth:** `fixed_asset.read`. Query: `asOf`. → `200`.
- `GET /fixed-assets/reports/accumulated-depreciation` — annual accumulated-depreciation report. **Auth:** `fixed_asset.read`. Query: `year`. → `200`.

## Depreciation Runs
Gated by `fixed_asset.depreciation_run` (create) / `fixed_asset.read` (read).
- `POST /depreciation-runs` — generate the monthly depreciation run for every eligible Active asset. **Auth:** `fixed_asset.depreciation_run`. Body: `year`, `month`. → `200`.
- `GET /depreciation-runs` — list past runs. **Auth:** `fixed_asset.read`. → `200`.
- `GET /depreciation-runs/{year}/{month}` — one month's run detail. **Auth:** `fixed_asset.read`. → `200` / `404`.

### Day-proration
The **first** depreciation charge on an asset is day-prorated by `DepreciationStartDate` — an asset that starts depreciating on the 20th of the month gets a fractional first charge, not a full month. Every following month charges one full unit (`cost − salvageValue) / usefulLifeMonths`, and the **final** charge is trimmed to whatever balance remains so the total sums to exactly `usefulLifeMonths` units in either rounding direction — there is no dribble month and no multi-month lump at the end of the schedule. A run with an eligible asset can never produce a `0.00` line (a self-correcting minimum charge), because a month with zero depreciation lines would otherwise block period close for that month with no in-app way to recover.
