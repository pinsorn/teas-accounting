# Report-Backend10 — Sprint 8 wrap: Business Units (first wired GL dimension)

**Date:** 2026-05-17
**Spec:** Answer-Sana-Backend9.md
**Status:** ✅ COMPLETE — all 4 phases gated, all DoD §11 items met, plan.md §23.3 struck.
**Estimate vs actual:** spec estimated 5–7 days; delivered in a single focused
working span (~1 day of session time) — faster than estimate because the
PV sub-prefix numbering infra (§2.5) and the GL `BuildAndPostAsync` seam already
existed and only needed a `businessUnitId` snapshot parameter, and the schema was
purely additive (no backfill, no data migration, no compliance-trigger rewrite —
only a one-clause append to the existing TI immutability function).

---

## 1. What shipped (by phase)

### P1 — domain + data + migration
- `BusinessUnit` entity (`ITenantOwned`/`IAuditable`/`IConcurrencyVersioned`) +
  `BusinessUnitConfiguration` (`master.business_units`, unique `(company_id,code)`,
  `default_revenue_account_id`→CoA Restrict).
- `Company.RequiresBusinessUnit` (default `false`).
- Nullable `int? BusinessUnitId` + FK (Restrict) + filtered index on
  `TaxInvoice`, `Receipt`, `TaxAdjustmentNote`, `JournalLine`.
- EF migration `20260517021031_AddBusinessUnits` — verified `dotnet ef
  has-pending-model-changes` = **none** (model == migration).
- `200_add_business_units.sql` — RLS (ENABLE+FORCE+company-isolation) on
  `master.business_units` + `CREATE OR REPLACE sales.fn_enforce_ti_immutability`
  adding `OR OLD.business_unit_id IS DISTINCT FROM NEW.business_unit_id`
  (verbatim 040 body + the new clause; schema owned by EF, mirrors the 060
  split). Additive, idempotent, **no backfill**.

### P2 — service + endpoints + GL + reports
- `IBusinessUnitService` + impl (CRUD, dup-code guard `bu.duplicate`, soft
  `DeactivateAsync`, `Get/SetCompanyRequiresBuAsync`) + validators.
- `BusinessUnitEndpoints` (`/business-units` POST/PUT/DELETE-deactivate/GET/
  GET{id} gated `master.business_unit.manage`; `/business-units/company-setting`
  GET authn-only + PUT manage). `210_seed_business_unit_perm.sql` (perm + grants
  SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT — **no `$`-literal**, so
  it doesn't re-trip gotcha §17).
- BU on `Create{TaxInvoice,Receipt,TaxAdjustmentNote}Request`; **service-layer**
  company-flag enforcement (`bu.required`/`bu.invalid`).
- Numbering: `MM-YYYY-PREFIX[-BU]-NNNN` — BU code passed as `subPrefix` at TI/RC/
  CN post (reused PV infra; §2.5 was a no-op as predicted).
- `GlPostingService.BuildAndPostAsync(... int? businessUnitId = null)` →
  `l.BusinessUnitId ??= businessUnitId` on every line. TI/CN pass the doc BU.
  Receipt cross-BU: per-application AR-clearing credit lines tagged each TI's BU,
  single cash/bank debit line BU NULL, header BU = shared|NULL,
  `CrossesBusinessUnits` returned in `ReceiptPostedResult`.
- Report filters `business_unit_id` + `include_unspecified` on `/tax-invoices`,
  `/receipts`, `/tax-adjustment-notes`.

### P3 — UI
- `BusinessUnitSelector`; lib types/queries (`useBusinessUnits`, CRUD hooks,
  `useCompanyBuSetting`/`useSetCompanyBuSetting`, `apiPut`/`apiDelete`,
  `qs` widened for booleans).
- `/settings/business-units` (list + create/edit modal + soft-deactivate +
  company-requires toggle); sidebar "ตั้งค่า" → Business Units.
- BU dropdown on TI/Receipt/CN/DN new forms (required-asterisk + `buRequired`
  guard); list filter chips + `include_unspecified` on TI/Receipt/CN/DN;
  detail-page BU chips on TI/Receipt/CN/DN; cross-BU receipt-detail warning chip;
  ReceiptDetail per-application BU column. i18n th/en `businessUnit.*`.

### P4 — tests + gates
- Domain: `BusinessUnitTests` (BU active-by-default; JournalLine BU optional).
  The BU domain surface is intentionally anemic (it's a dimension tag — behavior
  lives in services + GL snapshot), so substantive coverage is integration-level.
- Integration `Sprint8BusinessUnitTests` (10 cases, native PG): flag-off allows
  NULL; flag-on `bu.required` then valid-BU OK; inactive BU `bu.invalid`;
  duplicate code `bu.duplicate`; soft-deactivate keeps historical reference +
  list filtering; **GL snapshot integrity** (every journal_line == TI BU);
  single-BU receipt inherits shared BU; cross-BU receipt header NULL + per-line
  BU + cash NULL; TI list BU filter + `include_unspecified`; **posted-TI BU
  immutability** trigger.
- e2e: `business-units-setup.spec.ts`, `receipt-cross-bu-warning.spec.ts`.

---

## 2. The 4 mid-sprint design flags — ACCEPTED by Sana (mechanism notes)

All four were escalated mid-sprint (not improvised) and accepted. Recorded here
per the spec instruction:

- **(a) `/reports/sales-summary` BU filter — DEFERRED to Sprint 9.** The endpoint
  does not exist (only `vat-register`/`pnd30`/`number-gaps`). Sprint-8 scope is
  *filter params on existing list/report endpoints*; a full P&L-by-BU /
  sales-summary report is explicitly a Sprint-9 item. Nothing was created.
- **(b) number-gaps BU filter — DEFERRED.** The gap audit is a sequence-by-
  `(doc_type, sub_prefix, month)` view. Because the BU code is already the
  sub-prefix, per-BU counters are *already independent*; a BU filter on the gap
  view is not meaningful and would need a view rewrite. Deferred, not built.
- **(c) `requires_business_unit` enforced at the SERVICE layer** instead of
  `ITenantContext` + a validator (spec §4.4). Mechanism: putting the flag on
  `ITenantContext` would require the DbContext to read `Company`, creating a
  `DbContext ← ITenantContext` DI cycle; the JWT-carried value would also go
  stale after a toggle. The service already loads `Company` in `CreateDraft`, so
  the check is always-fresh with zero new dependencies. **Same observable
  behavior** (`bu.required`/`bu.invalid` at draft create). Accepted as the better
  design.
- **(d) Company toggle via `/business-units/company-setting` GET/PUT** instead of
  reworking `CompanyDto`/`CompanyService` across the app. Minimal blast radius,
  same persisted effect (`companies.requires_business_unit`). The GET is
  authn-only (clerks need it to drive the required-asterisk); the PUT is
  `master.business_unit.manage`.

---

## 3. Gotchas / issues caught by the gates

1. **e2e combobox collision (latent P3 regression, caught by the e2e gate).**
   The new `BusinessUnitSelector` is a native `<select>` → ARIA role
   `combobox`. The `CustomerSelector` is an `<input role="combobox">`. The
   shared e2e helper `createAndPostTaxInvoice` (and two specs) used
   `getByRole('combobox')` unscoped → Playwright strict-mode violation once a BU
   selector was on the TI/Receipt forms. **Fix:** repointed the three customer
   locators to the unique search placeholder
   `getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี')`. No product change. This
   is the kind of cross-cutting break that only surfaces under the e2e gate —
   not tsc/next build — which is why P3 was *not* declared done on the
   build-only gates.
2. **Next.js route-announcer also has `role="alert"`.** `receipt-cross-bu-
   warning.spec` first asserted `getByRole('alert')` → matched both the warning
   div and `#__next-route-announcer__`. Scoped to `.alert-warning`. (Generalises
   gotcha §15/§16 family — prefer a class/text-scoped locator over a bare role
   when a framework injects same-role nodes.)
3. **Did not re-trip gotcha §17.** `210_seed_business_unit_perm.sql` deliberately
   contains no literal bcrypt `$2a$12$` (it only inserts a perm + grants), so the
   Npgsql whole-file positional-param parser issue from Sprint 7-half does not
   recur. PostgresFixture re-ran 200/210 every session with no errors.

---

## 4. Doc nit (flagged, not silently worked around)

Answer-Sana-Backend9 instructed striking **plan.md §23.3**, but plan.md had no
§23.2/§23.3 (same situation as Answer-Sana-Backend8 → §23.1 in Report-Backend9).
Per the established pattern and the Claude-owned status of `plan.md`, §23.2
(reserved) + §23.3 (Sprint-8 shipped) were added so the reference resolves, and
a Sprint-8 ☑ DONE bullet was added to the Phase 2/3 backlog. Minor — raising it
here for visibility, consistent with the R9 handling.

---

## 5. Gate results (all green)

| Gate | Result |
|---|---|
| Backend build (`-m:1`, U: short path) | **0 error / 0 warning** |
| `Accounting.Domain.Tests` | **34/34** (32 baseline + 2 new) |
| `Accounting.Api.Tests` (native PG :5433) | **37/37** (27 baseline + 10 new) — **0 regression, 0 skip** |
| Frontend `tsc --noEmit` | **0** |
| `next build` | **0** (31 routes incl. `/settings/business-units`) |
| Playwright (system Edge) | **15/15** (13 prior + 2 new) |
| `dotnet ef has-pending-model-changes` | **none** (model == migration) |
| DbInitializer idempotency | PostgresFixture re-runs all SqlScripts incl. 200/210 each session w/o tracking → 37/37 proves idempotency; API startup applied them to `teas_app` cleanly |
| GL snapshot integrity | asserted (`Posted_ti_snapshots_bu_onto_every_journal_line`) |
| Posted-TI BU immutability | asserted (trigger blocks raw `business_unit_id` UPDATE) |

---

## 6. Definition-of-Done (spec §11) — 15/15

1. ✅ `master.business_units` + RLS + EF migration (no drift)
2. ✅ `companies.requires_business_unit` opt-in (default false)
3. ✅ Nullable BU FK on TI/Receipt/TaxAdjustmentNote/JournalLine
4. ✅ Numbering `MM-YYYY-PREFIX[-BU]-NNNN` (reused PV sub-prefix infra)
5. ✅ Company-flag enforcement (service layer — accepted flag c)
6. ✅ GL snapshots doc BU onto every journal_line
7. ✅ Receipt cross-BU: header NULL + per-line BU + warn (no block)
8. ✅ `IBusinessUnitService` CRUD + endpoints + perm + seed
9. ✅ Report filters `business_unit_id` + `include_unspecified`
10. ✅ `/settings/business-units` + company toggle
11. ✅ BU dropdowns + list filter chips + detail chips + cross-BU chip + i18n
12. ✅ Unit + integration tests (Domain 34, Api 37, 0 regression)
13. ✅ 2 e2e (business-units-setup, receipt-cross-bu-warning) → Playwright 15/15
14. ✅ All gates green + DbInitializer idempotency + schema diff + snapshot integrity
15. ✅ plan.md §23.3 struck "✅ Shipped Sprint 8 (2026-05-17)" + this report

**Scope cuts honored (escalated, never improvised):** no AP-side BU (VI/PV),
no Q/SO/DO BU, no full P&L-by-BU report (→ Sprint 9), no cost_center/project,
no retroactive backfill, no multi-BU per doc, no BU hierarchy, no BU-level RBAC.

---

## 7. Recommended next (Sprint 9 candidates)

- P&L / sales-summary **by Business Unit** (the deferred flag-a report; the GL
  dimension is now fully populated on every journal_line so the read side is the
  only remaining work).
- Optional: BU-aware number-gap view (flag b) if Sana wants per-BU gap audit
  (needs the gap view reworked to group by sub-prefix explicitly).
