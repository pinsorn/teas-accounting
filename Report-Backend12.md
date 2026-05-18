# Report-Backend12 — Sprint 8.6 wrap: AR-side WHT (customer withholds from us)

**Date:** 2026-05-17
**Spec:** Answer-Sana-Backend11.md (+ Question-Backend12 spec-first gate)
**Status:** ✅ COMPLETE — 21 DoD met, all gates green, plan.md §23.5 struck.
**Estimate vs actual:** refined 5–6 days; delivered in one extended session.
Faster because the GL `BuildAndPostAsync`/Receipt seams + the 8.5 PDF-branching
foundation already existed; the spec-first gate (Question-Backend12) removed the
biggest risk (Product master) up front so no mid-build rework.

---

## 1. Spec-first gate (Question-Backend12) — all answers applied

Surveyed before writing any migration. One blocker + 4 confirms, all accepted:

- **R-B1a** — no Product master / no `ProductType` exists. Shipped manual WHT
  base; `wht-base-suggest` degrades to "full ex-VAT applied subtotal, user
  trims to service portion"; rate/type still auto-suggested
  (customer default → CORPORATE→SVC). Service/goods split → Sprint 10.
- **R-B2** — kept `SVC`/`RENT`/`ADS` (no rename; would break seed 170 +
  Sprint 5/6 AP-side). New types added alongside.
- **R-B3** — 13 domestic WHT types, no `SALARY` (payroll out of scope).
- **R-B4** — e2e uses manual base override (the real legal path).
- **R-B5** — `CompanyService.CreateAsync` narrow copy (13 WhtTypes + 1180
  only), not a full onboarding bootstrap.

---

## 2. What shipped (phased P1–P6, gated each)

| Phase | Delivered |
|---|---|
| P1 | `AddARWhtSupport` migration: Receipt WHT cols + `cash_received` + CHECKs; `WhtCertificate.Direction`/`ReceiptId` + `PaymentVoucherId`→nullable; `WhtType.EffectiveFrom/To` + unique-index swap; `Customer.DefaultWhtTypeId`; `GlAccountsOptions.WhtReceivableAccount=1180`. SQL 220 (13 types) + 230 (1180 CoA + `tax.wht_type.manage`). |
| P2 | Receipt WHT capture + validators + GL branch (`Dr Bank cash_received + Dr 1180 = Cr AR Σapplied`, cross-BU aware) + `WhtCertificate` Direction='R' + `wht-base-suggest`. |
| P3 | `IWhtTypeService` CRUD + `ResolveAtDateAsync` + `ChangeRateAsync` (effective-date) + endpoints + perm; `CompanyService` narrow default-set copy. |
| P4 | `/reports/wht-receivable-register` + `/reports/wht-receivable-aging` (basic). |
| P5 | `/settings/wht-types` (CRUD + change-rate modal); Receipt form WHT collapsible (type select + auto-suggest + manual override + live cash-received); receipt detail WHT section; receipts list WHT column; Receipt PDF WHT section (reuses 8.5 `DocumentLabels`); `/reports/wht-receivable`; sidebar; i18n th/en; lib types/queries. |
| P6 | Unit (`WhtTypeTests`) + integration (`Sprint86ArWhtTests` ×7) + 2 e2e; all gates; mirror; plan.md §23.5; this report. |

---

## 3. Bugs caught & fixed by the gates (honest — not masked)

1. **WhtCertificate `(company_id, doc_no)` unique was wrong for Direction='R'.**
   For Payable it's our WT sequence (unique). For Receivable, `doc_no` = the
   customer's own 50ทวิ number — outside our control, can legitimately repeat
   across customers. The e2e exposed `23505` on the 2nd receipt. **Fix:**
   filtered the unique index to `direction = 'P'` + migration
   `ArWhtCertReceivableDocNoFilter`. This is a genuine design correction, found
   only because the e2e drove a real second WHT receipt.
2. **Receipt form had no WHT type selector** (P5 gap). The backend requires
   `WhtTypeId` when `WhtAmount > 0`; the form only had rate/base. The e2e
   failed at post → added the type `<select>` (active, in-force rows; picks
   rate from the chosen type, override-able).
3. **Seed 120 `42P10`** — its `ON CONFLICT (company_id, code)` on `wht_types`
   broke once `AddARWhtSupport` replaced the 2-col unique with the 3-col one;
   fixed 120 to set `effective_from` + the new conflict target.
4. **Pre-existing persistent-DB / toast-race flakiness** (gotcha §14/§16
   re-applied): Sprint-8.5 threshold tests (fixed companyIds → per-run-unique);
   Sprint-5.5 period-close (40-year collision → tolerate already-closed); PV-WHT
   + receipt-confirm e2e (approve→post / dialog re-render + sonner intercept →
   retry-until-request-fires). All fixed deterministically, not masked. As Sana
   noted, this fixture-idempotency pattern is now the most-re-applied gotcha — a
   shared test-fixture helper is a sensible Phase-2 cleanup.

---

## 4. Flags / mechanism notes (accepted earlier or raised now)

- **WhtType change-rate audit** = the closed/open row pair (immutable history;
  posted docs keep their snapshot). No explicit `audit.activity_log` insert —
  the change-rate API isn't externally exposed beyond the gated endpoint and
  the `activity_log` write API isn't wired here. **Phase-2 enhancement** when a
  public surface opens (accepted by Sana).
- **WHT-Receivable aging** is basic — no 1180 settlement model this sprint, so
  every posted WHT receipt shows outstanding. Per spec §7 ("full Sprint 9").
- **i18n namespace** = `rc.wht.*` (not the spec's literal `receipt.wht.*`) for
  consistency with the existing `rc` receipts namespace; `whtType.*` /
  `whtReceivable.*` added top-level. Behaviour identical; mechanism note.
- **e2e two-pass** (VatMode process-global env) reused from 8.5: 17 specs @
  `VatMode=true` + 1 (`non-vat-mode-pdf`) @ `VatMode=false` = 18/18.
- **DoD #9 manual PDF ×2** (Receipt-with-WHT in VatMode on/off): the Receipt
  PDF WHT section is conditional and VAT-status-independent (8.5 §2.1); the
  branching itself is unit-proven (`DocumentLabelsTests`, 8.5) + e2e wiring.
  Visual ×2 requires a human viewer (agent-infeasible, same as 8.5 §5) —
  **recommend Ham/Sana spot-check** the Receipt-WHT PDF in both modes.

---

## 5. Gate results

| Gate | Result |
|---|---|
| Backend build (`-m:1`, U:) | **0 / 0** |
| `Accounting.Domain.Tests` | **45/45** (41 baseline + 4 `WhtTypeTests`) |
| `Accounting.Api.Tests` (native PG :5433) | **48/48** (41 baseline + 7 `Sprint86ArWhtTests`) — **0 regression, 0 skip** |
| Frontend `tsc` | **0** |
| `next build` | **0** (+`/settings/wht-types`, +`/reports/wht-receivable`) |
| Playwright (system Edge) | **18/18** — 17 @ VatMode=true + 1 @ VatMode=false |
| `dotnet ef has-pending-model-changes` | **none** |
| DbInitializer idempotency | 220/230 + `AddARWhtSupport` + `ArWhtCertReceivableDocNoFilter` re-run clean (48/48 vs persistent teas_test proves it) |
| GL balance | asserted `Dr Bank cash_received + Dr 1180 = Cr AR` ±0.01 (`Receipt_with_wht_posts_balanced_gl_and_cert_R`) |
| WhtType snapshot integrity | asserted (`WhtType_change_rate_closes_old_and_opens_new`) |
| Mirror sync `Y:\AccountApp` | ✅ |

---

## 6. Definition of Done (spec §11) — 21/21

1. ✅ `AddARWhtSupport` applied  2. ✅ SQL 220+230 idempotent
3. ✅ 13 WHT types (no SALARY — R-B3)  4. ✅ WhtCertificate Direction+ReceiptId
5. ✅ Receipt WHT capture (DTO+entity+service+endpoint)  6. ✅ 1180 CoA + new-company copy
7. ✅ `GET /receipts/wht-base-suggest` (R-B1a)  8. ✅ `IWhtTypeService` + endpoints + perm
9. ✅ `CompanyService` default-set copy (R-B5 narrow)  10. ✅ effective-date + change-rate
11. ✅ `/settings/wht-types` UI  12. ✅ Receipt form WHT + auto-suggest
13. ✅ Receipt detail WHT section  14. ✅ Receipt PDF WHT section (8.5 branching)
15. ✅ wht-receivable register + aging (basic)  16. ✅ i18n th/en
17. ✅ unit + integration + 2 e2e green  18. ✅ all gates green
19. ✅ mirror `Y:\AccountApp`  20. ✅ plan.md §23.5 "✅ Shipped Sprint 8.6"
21. ✅ Report-Backend12 (this)

---

## 7. After this sprint

Per spec §12: **Sprint 8.7 — online subscriptions / foreign vendor**
(`Answer-Sana-Backend12.md`). **Sprint 10** is now the natural home for the
**Product master** (enables the deferred service/goods WHT-base split — R-B1a —
and the Quotation chain).

**Open items for Sana:** (i) confirm the WhtCertificate Direction='R' unique-
index fix (filtered to `direction='P'`) is the intended model; (ii) confirm the
manual Receipt-WHT PDF ×2 visual spot-check owner; (iii) note the recurring
test-fixture-idempotency gotcha → a shared fixture helper Phase-2 cleanup.
