# Whole-Codebase Review — TEAS — 2026-06-17 (Consolidated Index)

Multi-agent read-only review across 9 lenses. Each lens has a detail report in this folder.
This index de-duplicates overlaps, **re-rates findings after overseer gating against live code**, and
orders by true severity. Raw agent counts: **7 Critical · ~17 High**. After gating, the genuinely
production-blocking set is much smaller (see "Gated Critical verdicts").

> Gating note: every Critical below was re-checked by the overseer against the actual source.
> Severities here may differ from the individual reports — this index wins where they disagree.

## Detail reports
| # | Lens | File | Raw C/H |
|---|------|------|---------|
| 01 | Compliance / Thai law | `01-compliance-thai-law.md` | 1 / 0 |
| 02 | Security | `02-security.md` | 0 / 1 |
| 03 | Performance | `03-performance.md` | 1 / 3 |
| 04 | Backend architecture & quality | `04-backend-architecture.md` | 2 / 2 |
| 05 | Feature correctness & calculations | `05-feature-correctness.md` | 1 / 1 |
| 06 | Frontend code quality | `06-frontend-quality.md` | 0 / 3 |
| 07 | UI/UX & i18n | `07-uiux-i18n.md` | 1 / 3 |
| 08 | Data model / EF / RLS | `08-data-model-ef.md` | 1 / 1 |
| 09 | Test coverage & quality | `09-test-coverage.md` | 0 / 3 |

---

## Gated Critical verdicts (overseer re-rating)

| Raw | Finding | Gate verdict | True severity |
|-----|---------|--------------|---------------|
| 01-C1 | Tax-point `doc_date`/`TaxPointDate` taken from `req.DocDate`, not `IClock.TodayInBangkok()` (`Infrastructure/Sales/TaxInvoiceService.cs:237-238`) | **CONFIRMED** vs live code | **Critical** (compliance — ม.86/4(7), §10) |
| 05-C1 | Foreign-currency VAT/WHT summed unconverted into ภ.พ.30 + VAT registers (`Reports/VatReportService.cs`, `TaxSummaryService.cs`) | **CONFIRMED latent** — path open, validators accept any currency/rate; active only if a non-THB doc exists | **Critical (latent)** — wrong RD filing |
| 08-C1 / 02-H1 | No RLS on `quotations`,`sales_orders`,`delivery_orders`,`receipts` (+ sub-tables) — EF filter only | **CONFIRMED** — no SqlScript enables RLS on these | **High** (no leak demonstrated in this review — tenant id token-sourced + EF filter active; but §4.7 DB backstop absent) |
| 04-C1 | `ContinueWith(t => t.Result)` ×4 in `Master/MasterDataServices.cs:47,348,383,421` | **CONFIRMED** (line 47 exact) | **High** (live admin path; §5; fault-masking) |
| 04-C2 | Bare `catch {}` swallows all exceptions in `Identity/ApiKeyResolver.cs:70` | **CONFIRMED** | **Medium** (best-effort telemetry; narrow + log) |
| 03-C1 | Sync `FirstOrDefault` (not async) in `ETax/ETaxXmlBuilder.cs:23-28` | **CONFIRMED but INERT** — e-Tax is Phase-1 scaffolding, not on any live path | **Medium** (fix before e-Tax goes live) |
| 07-C1 | "VAT rate editable in Companies settings UI" (`settings/companies/page.tsx`) | **FALSE POSITIVE / DOWNGRADED** — page is **super-admin-only** (`page.tsx:42-55` hides body unless `isSuperAdmin`); this is the §3.4-sanctioned super-admin company form, the *only* allowed path to set `vat_rate`. Agent's "any Master.CompanyManage user" premise is wrong. | **Low / needs Ham's ruling** (§10-vs-§3.4 doc tension) |

**Bottom line after gating:** 2 true compliance Criticals (**01-C1 doc_date**, **05-C1 FX → ภ.พ.30**),
a confirmed High RLS-backstop gap, a confirmed High async-safety defect, and several Medium items.
The flashiest raw Critical (VAT-in-UI) is a false positive.

---

## Compliance-bearing remediation list (priority order — these touch §4 legal rules)

1. **[Critical · §10/ม.86/4(7)] Pin tax-point server-side.** `TaxInvoiceService.cs:237-238` → derive
   `DocDate`/`TaxPointDate` from `IClock.TodayInBangkok()`, ignore `req.DocDate`. Same root pattern
   should be audited on PV/VI/Receipt/CN-DN/BN/JE create paths (01 report lists them). **ASK Ham**
   before changing — it's a §4 rule touch (§11), though the fix only *enforces* the existing rule.
2. **[Critical (latent) · §4 / ภ.พ.30] Foreign-currency VAT/WHT.** Either convert to THB at the
   register/aggregation boundary (`VatReportService`, `TaxSummaryService`) **or** (simplest, recommended
   if multi-currency is deferred) hard-block non-THB at document create — neutralizes both 05-C1 and
   05-H1 (GL FX misstatement) at once.
3. **[Medium · amplifier] Default-closed periods.** `PeriodCloseService.cs:26` returns OPEN for a
   missing period row → unbounded back/forward-dating. Make unknown periods closed (or bound to the
   current period). This is the mitigating control whose absence elevated #1.
4. **[High · §4.7] RLS backstop on the sales chain.** Add `ENABLE/FORCE ROW LEVEL SECURITY` + tenant
   policy on `quotations`, `sales_orders`, `delivery_orders`, `receipts` (+ line/application sub-tables)
   in a new SqlScript, matching the VI/billing/payroll/cit pattern. Defense-in-depth — no leak
   demonstrated in this review, but the §4.7 DB backstop is genuinely absent.
5. **[High · §4.2] Immutability triggers on CN/DN + Receipts.** `tax_adjustment_notes` and `receipts`
   have `MarkPosted()` but no DB `BEFORE UPDATE/DELETE` trigger (TI and VI do). Add triggers.
6. **[Low → needs ruling · §10 vs §3.4] VAT rate in super-admin company form.** Decide whether the
   super-admin-only Companies screen may keep the `vatRate` field (§3.4 authorizes it) or must hide it
   (§10 absolute). Doc-level contradiction — Ham's call, not an autonomous fix.

## Non-compliance engineering items (de-duplicated)

- **[High] Async-safety:** `MasterDataServices` `.Result` ×4 (04-C1); `PermissionLookup` double
  round-trip per request, no `AsNoTracking` (03-H1, runs on *every* authenticated request — fix is high
  ROI); `ETaxXmlBuilder` sync query (03-C1, inert).
- **[Medium] Fault-masking:** bare `catch {}` in `ApiKeyResolver` (04-C2).
- **[High] Frontend:** hardcoded Thai strings bypassing next-intl in shared components (06, 07-H1);
  see 06 for RSC-boundary/typing items.
- **[High] i18n:** th.json/en.json key-set drift + hardcoded strings (07).
- **[High] Test gaps:** no test for ม.86/4 8-field enforcement, tax-inclusive 7/107, or
  voided-number-not-reused (09) — exactly the paths the compliance findings above touch; add these
  alongside the fixes.

## Cross-agent corroboration (raised confidence)
- RLS sales-chain gap found independently by **02 (Security)** and **08 (Data-model)**.
- FX misstatement found as one root cause spanning **05-C1 (tax)** and **05-H1 (GL)**.
- `.Result`/sync-over-async flagged by both **03 (Performance)** and **04 (Architecture)**.

## What the review verified as genuinely SOUND
Double-locked posted-doc immutability (DB trigger + no app mutate path) on TI/VI; atomic gap-free
POST-only document numbering; append-only audit log; RLS *with FORCE* on the tables that have it;
server-side VAT-rate derivation (SalesLineBackstop) + 7/107 math; PV per-line input-VAT guards in
ม.82/5→ม.81→standard order; WHT 50ทวิ / ภ.ง.ด.3/53/54 / ภ.พ.36; decimal money throughout;
CE-calendar-internal; Clean Architecture layering largely intact.
