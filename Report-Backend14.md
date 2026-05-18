# Report-Backend14 — Sprint 9 wrap: Reports + Tax Filings (the big one)

**Date:** 2026-05-17
**Spec:** Answer-Sana-Backend14.md (+ Question-Backend13 → R-Q1a/R-Q2/R-Q3 accepted)
**Status:** ✅ COMPLETE — 25/25 DoD met, all gates green, plan.md §23.7 struck.
**Estimate vs actual:** spec'd ~10–13 days (3 Parts A/B/C, gate between each).
Delivered phased, never bundled. Spec-first gate first (Question-Backend13)
caught 3 premise gaps before any migration — all recommended zero-scope
degrades accepted, so build proceeded without rework.

---

## 1. What shipped (phased A → gate → B → gate → C → gate → wrap)

| Part | Delivered | Gate |
|---|---|---|
| **A** Financial Reports | `GET /reports/trial-balance` (as-of, normal_balance, **Σ Dr == Σ Cr** invariant), `/reports/profit-loss` (R-Q1a flat Rev−Exp=Net by BU + payload `note` on GP/COGS Phase-2 deferral), `/reports/sales-summary` (R-Q2 customer\|business_unit; product→400), WHT-Receivable aging buckets + CertReceived/Reconciled. 3 UI routes + sidebar + i18n. | Domain 53/53, Api 58/58, **Pw 22/22** |
| **B** VAT compliance | R-Q3 `TaxCode.Category` `[NotMapped]` (derived from IsExempt/IsZeroRated) + only `LegalRef` col + `EnsureValid()` invariant; seed 240 + `CompanyService` default-copy; ม.82/6 `IProportionalInputVatService`; ภ.พ.30 preview/finalize → immutable `tax.tax_filings`; in/out VAT registers; `tax.filing.*` perms (seed 241); UI `/reports/pnd30`. | Domain 60/60, Api 63/63, **Pw 23/23** |
| **C** WHT compliance | `WhtFormType.Pnd54` (8.7-deferred enum); seed 250 FOR-SVC/FOR-ROYAL + copy; ภ.ง.ด.3/53/54 generators; ภ.พ.36 reverse-charge + finalize auto-JV (Dr 1170 / Cr 2151, net 0); shared `TaxFilingStore`; `/tax-filings` index + 4 sub-pages + i18n. | Domain 60/60, Api **66/66**, **Pw 25/25** |

**Final sprint-close gate:** build 0/0, no EF drift (migration
`Sprint9TaxFilingAndLegalRef` = `legal_ref` + `tax.tax_filings`), Domain
**60/60**, Api **66/66** (0 skip, 0 regression), tsc 0, next 0 (9 new routes),
**Playwright 25/25** (two-pass: 24 @ `Tax__VatMode=true` incl. the 5 new specs;
1 @ `false`), mirror `Y:\AccountApp` synced.

---

## 2. Compliance highlights

- **Trial Balance Σ Dr == Σ Cr** is the headline invariant — asserted in
  `Sprint9FinancialReportTests` (totals + per-row Net=Debit−Credit) and the
  `trial-balance.spec.ts` e2e (success badge, never "ไม่สมดุล").
- **ภ.พ.30** categorises sales by tax-code (ม.81 exempt / ม.80/1 zero / taxable),
  ม.82/6 claim ratio surfaced; finalize writes an **immutable** `tax.tax_filings`
  row, re-finalize → `tax_filing.already_finalized` (amendment = Phase 2).
- **ภ.พ.36 reverse-charge:** finalize posts the **balanced** auto-JV
  Dr 1170 Input VAT / Cr 2151 Output VAT (net 0); integration test asserts both
  legs + Σ debit == Σ credit. Pre-finalize guard prevents an orphan JV on
  re-finalize.
- **ภ.ง.ด.3/53/54** routed by `Direction='P'` + payee type / `FormType==Pnd54`.
- Period-immutability + `tax.filing.preview/finalize/read` RBAC (finalize
  enforced in-handler so the spec's single mode-param endpoint is preserved).

---

## 3. Mechanism notes / premise corrections (flagged, not improvised)

1. **R-Q3 applied — no `category` column.** `tax_codes` already has
   `IsExempt`/`IsZeroRated`; `Category` is a `[NotMapped]` computed property
   (single source of truth). Only `LegalRef` added. `EnsureValid()` rejects the
   exempt⊕zero-rated conflict. (Same single-source discipline as 8.7
   `VatRegistered` reuse.)
2. **Spec SQL illustrative vs real schema.** Spec B2 inserts into
   `master.tax_codes(name_en, rate)`; the real table is `tax.tax_codes` with no
   `name_en` and rate in `tax.tax_rates` (not a scalar). Seed 240 adapted to the
   actual columns (+ standard taxable VAT7/VAT-IN7 for ภ.พ.30 join completeness).
   "Actual schema authoritative" — the accepted convention (cont.27/28).
3. **Pre-existing Sprint-6 scaffold left intact.** `Pnd30Summary` /
   `IVatReportService` (flat, no category split, no finalize) is unused-but-DI-
   registered. Built the richer `ITaxFilingService` + `IWhtFilingService`
   contract alongside; scaffold + `GET /reports/pnd30` untouched (no breaking
   change; Phase-2 consolidate). **5th instance** of the duplicate-source-drift
   catch (after VatRegistered 8.7, tax_codes Q3, GlReportDtos Part A).
4. **`tax.tax_filings` (spec C8) pulled forward to Part B.** B5 ภ.พ.30 finalize
   is a hard dependency on it. Built in Part B per the C8 contract; Part C
   reused the same table + `tax.filing.*` perms, adding only form_type values +
   the 4 generators. Internal build-order resolution (not a user escalation).
5. **`WhtFormType.Pnd54` enum extension required.** The enum was Pnd3/Pnd53/Pnd1
   only (PND54 explicitly cut in 8.6). C1/C4 require it — added the member
   (string-converted, no schema change, no migration). Deferred-from-8.7,
   documented.
6. **ม.82/6 standalone endpoint not exposed.** `IProportionalInputVatService`
   exists and is consumed by the ภ.พ.30 generator (spec B3: "Used by ภ.พ.30
   generator"); the ratio surfaces in the ภ.พ.30 payload + page. Per-line
   direct vs shared input-VAT classification = **Phase 2 (§508)** — shared
   apportionment is 0 this sprint (spec-sanctioned simplification); a warning
   is emitted when mixed exempt sales are detected.
7. **ภ.ง.ด.54 discriminator** = `FormType==Pnd54` (only FOR-SVC/FOR-ROYAL carry
   it). is_foreign cross-check via PV→Vendor is redundant; not used.
8. **tax_code line-badge deferred.** Spec B2 wants a category badge "when
   picking tax_code", but the TI/RC line form uses a numeric rate field, not a
   tax_code picker — there is no picker to badge. Retrofitting one is a form
   redesign beyond Part B's compliance scope. Category is fully covered in the
   backend + surfaced on `/reports/pnd30`. Flagged, not improvised.
9. **`/tax-filings/pnd30` sub-page** = the existing `/reports/pnd30` (shipped
   Part B); the `/tax-filings` index links to it (avoids a duplicate page).
   4 new sub-pages + index delivered.

---

## 4. Bugs caught & fixed by the gates (honest)

- **`from`/`to` LINQ-keyword collision** (Part A `ProfitLossAsync`, Part B
  `SalesCategorizer`): query-syntax `from … where … && to` is a parse error.
  Rewrote in method syntax + renamed params `fromDate`/`toDate`.
- **`GlReportDtos` name collision** (Part A): renamed Sprint-9 DTOs `*Report`.
- **`ck_vendors_foreign_vatreg`** (Part C test): a foreign vendor must be
  `vat_registered` — test fixture corrected.
- **PostgresFixture persists rows across `dotnet test` runs** (re-applies
  SqlScripts idempotently but inserted data survives — *not* a clean DB per
  run). Fixed-period finalize tests therefore collide on the **second** run
  with `tax_filing.already_finalized`. Switched every ภ.พ.30/ภ.พ.36/ภ.ง.ด.
  immutability test to a unique far-future random period (and retro-fixed
  Part B's Pnd30 finalize test). Idempotency discipline (gotcha family).
- **e2e strict-mode violation** (pnd36): a text regex matched 2 nodes →
  `data-testid=pnd36-jv-note` + scoped assertion.

---

## 5. e2e stack note (carry forward)

The built API must run with **CWD = its bin dir**
(`…\Accounting.Api\bin\Debug\net10.0`) before `dotnet exec .\Accounting.Api.dll`
— the host ContentRoot defaults to CWD; running from elsewhere → "Configuration
section 'Jwt' is required" (appsettings.json not found). `appsettings*.json` are
copied to bin on build. Two-pass e2e: `Tax__VatMode=true` (24 specs,
`--grep-invert "non-VAT mode"`) then restart `false` (1 spec).

---

## 6. DoD — 25/25

Part A 5/5 · Part B 8/8 · Part C 8/8 · Wrap 4/4 (all gates green; mirror;
plan §23.7 struck "✅ shipped Sprint 9 (2026-05-17)"; this report).

**Sprint 9 closed.** Awaiting the next sprint spec.
