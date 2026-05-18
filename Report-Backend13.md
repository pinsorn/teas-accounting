# Report-Backend13 — Sprint 8.7 wrap: Online subscriptions + Foreign vendor

**Date:** 2026-05-17
**Spec:** Answer-Sana-Backend12.md
**Status:** ✅ COMPLETE — 17 DoD met, all gates green, plan.md §23.6 struck.
**Estimate vs actual:** refined 4–5 days; delivered in one session. The GL
`PostPaymentVoucherAsync` / `PostVendorInvoiceAsync` seams + the Sprint-6 PV
pipeline already existed, so the change was additive (flags + 2 GL branch
extensions + UI auto-detect).

---

## 1. What shipped (phased P1–P4, gated each)

| Phase | Delivered |
|---|---|
| P1 | `AddForeignVendorSupport`: Vendor `IsForeign`/`HasThaiVatDReg`/`CountryCode`; PV `SelfWithholdMode`/`RequiresPnd36ReverseCharge`; VI `HasInputVat`(def true)/`RequiresPnd36ReverseCharge`. 2 CHECKs. No SQL script (defaults backfill, no model drift). |
| P2 | Vendor DTOs/validators (+`CountryCodes` allowlist; Create+Update mirror CHECKs; foreign⇒VatRegistered locked). PV `selfWithhold = req ?? (foreign&&!vatD)`, auto `requiresPnd36`, `TotalPaid` adjusts, validator blocks self-withhold+VI. GL PV gross-up branch; VI receipt-only branch (`recoverable = HasInputVat && IsRecoverableVat`). |
| P3 | Vendor new foreign section + detail row; PV new self-withhold toggle (auto/lock) + chips; PV detail Self-withhold/ภ.พ.36 badges; VI new chips; i18n th/en; types/queries. No new routes. |
| P4 | `ForeignVendorTests` (Domain ×8) + `Sprint87ForeignVendorTests` (Api ×5) + 2 e2e; all gates; mirror; plan.md §23.6; this report. |

---

## 2. The 3 scenarios — how they resolve

- **A (domestic auto-charge):** manual `SelfWithholdMode` toggle → gross-up:
  `Dr Expense = subtotal + vat + wht`, `Cr Bank = subtotal + vat` (full),
  `Cr WHT-Payable = wht`. Balanced.
- **B (foreign no VAT-D):** vendor flags → PV/VI auto `self_withhold=true` +
  `requires_pnd36_reverse_charge=true` (Sprint-9 ภ.พ.36 generator scans it);
  same gross-up GL. WhtCertificate still Direction='P'.
- **C (foreign with VAT-D):** treated as domestic — normal flow, info chip
  only, no behavior change.

---

## 3. Mechanism notes / premise corrections (flagged, not improvised)

1. **`is_vat_registered` already exists.** Spec §2.1 adds it as NEW, but
   `Vendor.VatRegistered` already exists with identical semantics (stored, in
   DTOs/UI, not read by GL). **Decision: reuse the existing column** — adding a
   second boolean with the same meaning is a real drift hazard. The CHECK
   `is_foreign → is_vat_registered` maps to `is_foreign → vat_registered`;
   `VatRegistered = req.IsForeign || req.VatRegistered`. Unambiguous intent,
   strictly better; not escalated as a blocker (over-escalation avoided) —
   recorded as a mechanism note (same class as 8.5 PdfService / 8.6 namespace).
2. **FOR-SVC 15% not seeded.** Spec §8 prereq says "reuses WhtType FOR-SVC 15%
   seeded in 8.6", but 8.6 R-B3 explicitly cut foreign/SALARY types — FOR-SVC
   was never seeded. Not a blocker: the PV **line** carries `whtRate` (0.15)
   directly, so the gross-up GL needs no FOR-SVC row. The UI "default FOR-SVC"
   pre-fill is a soft convenience deferred with the rest of the foreign WHT-type
   set (Phase 2 / when foreign types are seeded). Flagged.
3. **i18n namespaces** = `ven.foreign.*` / `pv.selfWithhold.*` / `vi.*` (not
   the spec literals `vendor.foreign.*` / `vendorInvoice.*`) for consistency
   with the existing `ven`/`pv`/`vi` namespaces. Behaviour identical.
4. **Self-withhold for VI-linked PV** is out of scope (Phase 2) — enforced by
   `CreatePaymentVoucherValidator` (400 "not yet supported for VI-linked PV").

---

## 4. Bugs caught & fixed by the gates (honest)

1. **PV "missing WhtType"** — `PaymentVoucherService` requires a resolvable
   WhtType when a line has `whtRate > 0` (category-default fallback). The first
   integration draft passed `whtTypeId = null` with a category that had no
   default → threw. Fixed the test to resolve and pass an explicit `WhtTypeId`
   (the production path is unchanged; the form uses category-default like the
   working Sprint-6 PV e2e).
2. **Fragile e2e locators** — `getByLabel(/regex/)` didn't bind the country
   `<select>` and an `xpath=preceding::input` for the self-withhold toggle was
   ambiguous. Switched to `select[aria-label="ประเทศ"]` and
   `label:has-text(...) input[type=checkbox]` (gotcha §15/§16 family — prefer a
   structural/attribute locator over role-regex/xpath when a framework injects
   sibling nodes).

---

## 5. Gate results

| Gate | Result |
|---|---|
| Backend build (`-m:1`, U:) | **0 / 0** |
| `Accounting.Domain.Tests` | **53/53** (45 baseline + 8 `ForeignVendorTests`) |
| `Accounting.Api.Tests` (native PG :5433) | **53/53** (48 baseline + 5 `Sprint87ForeignVendorTests`) — **0 regression, 0 skip** |
| Frontend `tsc` / `next build` | **0 / 0** (no new routes) |
| Playwright (system Edge) | **20/20** — 19 @ VatMode=true + 1 @ VatMode=false |
| `dotnet ef has-pending-model-changes` | **none** |
| GL balance | self-withhold gross-up `Dr Expense = Cr Bank + Cr WHT-Payable` ±0.01; receipt-only VI `Dr Expense(gross) = Cr AP` — both asserted |
| CHECK constraint | `ck_vendors_vatd_foreign` enforced (integration: VAT-D without foreign → throws) |
| `requires_pnd36_reverse_charge` integrity | asserted true on foreign-no-VAT-D PV |
| Mirror sync `Y:\AccountApp` | ✅ |

---

## 6. Definition of Done (spec §9) — 17/17

1. ✅ `AddForeignVendorSupport` applied
2. ✅ Vendor entity + DTOs + service + endpoint + 3 flags
3. ✅ CHECK in DB + validator at app layer
4. ✅ PV `SelfWithholdMode` + 3-branch GL (existing + gross-up + VI-linked)
5. ✅ PV `requires_pnd36_reverse_charge` auto-set
6. ✅ VI `HasInputVat` + 2-branch GL (existing + receipt-only)
7. ✅ VI `requires_pnd36_reverse_charge` auto-set
8. ✅ Vendor management UI + foreign section + validation lock
9. ✅ PV form auto-detect + chips + auto-locked toggle
10. ✅ VI form auto-detect + chips
11. ✅ PV detail "Self-withhold" badge
12. ✅ i18n th/en
13. ✅ unit + integration + 2 e2e green
14. ✅ all gates green
15. ✅ mirror `Y:\AccountApp`
16. ✅ plan.md §23.6 "✅ Shipped Sprint 8.7"
17. ✅ Report-Backend13 (this)

---

## 7. After this sprint

Per spec §10: **Sprint 9 — Reports + Tax Filings** (Trial Balance, ภ.พ.30,
ภ.ง.ด.3/53/54 generators, **ภ.พ.36 reverse-charge generator** consuming the
`requires_pnd36_reverse_charge` flag this sprint set, P&L by BU — the deferred
8-flag from Sprint 8, VAT exemption ม.81, ม.82/6). Est. ~9–11 days.

**Open items for Sana:** (i) confirm reusing `Vendor.VatRegistered` as
`is_vat_registered` (no duplicate column) is the intended model; (ii) the
foreign WHT-type set (FOR-SVC/FOR-ROYAL 15%, PND.54) was never seeded (8.6
cut) — seed it in Sprint 9 alongside the ภ.ง.ด.54 generator; (iii) the
test-fixture-idempotency gotcha (§14) remains the most-re-applied — a shared
fixture-randomization helper is still a sensible Phase-2 cleanup.
