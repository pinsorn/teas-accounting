# Report-Backend11 — Sprint 8.5 wrap: VAT-mode polish (non-VAT companies)

**Date:** 2026-05-17
**Spec:** Answer-Sana-Backend10.md
**Status:** ✅ COMPLETE — 4 gaps closed, all gates green (1 DoD item flagged as
agent-infeasible, see §5).
**Estimate vs actual:** spec ~2 days; delivered same session. Faster because the
config + DI seams already existed and the change was purely additive (one new
pure helper + branching + one read service + one endpoint + UI gate).

---

## 1. What shipped

| Gap | Fix |
|---|---|
| (1) PDF says "ใบกำกับภาษี" for non-VAT | `DocumentLabels.TaxInvoiceHeader` swaps to configured neutral label; VAT subtotal/VAT rows hidden under non-VAT (single "ยอดรวม"). |
| (2) CN/DN hardcode ม.86/10 · ม.86/9 | `DocumentLabels.AdjustmentNote` → ม.82/9 under non-VAT. |
| (3) e-Tax CTA visible to non-VAT | `useSystemInfo().vatMode` gates XML-download + resend on TI detail. |
| (4) No ม.85/1 threshold warning | `IVatThresholdService` + `GET /system/vat-threshold-status` + dashboard banner. |

- New config: `TaxConfig.NonVatDocLabelTh/En` (API) + `VatModeOptions` (Infra,
  bound from the same `Tax` section) + appsettings/Development keys.
- `DocumentLabels` is a **pure** resolver in `Accounting.Domain` — the
  legally-sensitive branching is unit-tested directly; the PDF builders just
  call it.

---

## 2. Structural premises corrected (mechanism notes — not improvised)

The spec's §2.1 assumed separate `TaxInvoicePdfService` / `ReceiptPdfService` /
`CreditNotePdfService` / `DebitNotePdfService` classes. They do not exist —
mapped to the real structure (intent unchanged, equivalent behavior):

- **(a) PDF is inline `BuildPdfAsync`** in `TaxInvoiceService.Read.cs`,
  `ReceiptService.Read.cs`, `TaxAdjustmentNoteService.Read.cs` — no `*PdfService`
  classes. Branched in place; injected `IOptions<VatModeOptions>` into the two
  affected services' constructors.
- **(b) CN + DN share ONE `BuildPdfAsync`** (`TaxAdjustmentNoteService.Read`,
  NoteType-branched) — not two services. Single branch covers both.
- **(c) `TaxConfig` is API-layer** (Program.cs, `Configure<TaxConfig>`); the PDF
  builders are Infrastructure (Clean Architecture forbids Infra→API). Added a
  separate `VatModeOptions` bound from the same `Tax` appsettings section —
  identical config source/values, layering-correct. Mirrors the existing
  `ETaxBehaviorOptions` (binds `ETax`).
- **(d) CN/DN currently had NO legal-ref string at all** (only a title). The
  ม.86/10 · ม.86/9 ↔ ม.82/9 reference was *added* (additive) and branched, not
  "swapped from a hardcoded value".
- **(e) RC PDF unchanged** per spec §2.1 (receipt header is VAT-status
  independent) — `ReceiptService.Read` untouched.
- **(f) e-Tax CTA only exists on the TI detail page** (XML-download + resend).
  RC/CN/DN detail pages have none — audited, nothing to gate (gap 3 = TI only).

---

## 3. Threshold mechanics

`IVatThresholdService.CheckAsync`: `NotApplicable` immediately when
`VatMode=true`; else rolling-12-month (`IClock.UtcNow.AddYears(-1)`) sum of
`TotalAmountThb` over `Posted` Tax Invoices (tenant query filter scopes by
company) → `≥1.8M Exceeded`, `≥1.5M Approaching`, else `Ok`. Rolling-12-month is
deliberately more conservative than the official ปีปฏิทิน rule (documented in
plan §23.4). Endpoint `GET /system/vat-threshold-status` requires auth (tenant
context needed for the query), no specific permission.

---

## 4. e2e harness mechanism note (flagged per §8 — important)

`Tax:VatMode` is a **process-global env/config value**. The e2e stack is a single
external API process; 15 specs need `VatMode=true`. The new
`non-vat-mode-pdf.spec.ts` needs `VatMode=false`. A single shared stack cannot
satisfy both, and a per-spec env toggle would require a harness rewrite (out of
the ~2-day scope).

**Resolution (yields a true 16/16, transparently):** the gate runs two passes —
15 specs vs the normal `VatMode=true` stack, then the 1 new spec vs a dedicated
`VatMode=false` API instance (same DB, env override `Tax__VatMode=false`,
verified via `/system/info` → `vat_mode=False`). Both passes green → 16/16.

**Assertion choice:** asserting Thai PDF *text* from Playwright is unreliable —
QuestPDF Flate-compresses content streams and embeds subset fonts (verified: the
raw PDF bytes contain neither "TAX INVOICE" nor "Delivery Order" literally). So
the new spec asserts the cleanest deterministic `VatMode=false` signal: the
e-Tax CTA (XML/resend) is absent on a posted TI, and the doc still issues (PDF
downloads). **The PDF label correctness itself is proven deterministically by
`DocumentLabelsTests` (unit) — that is the authoritative compliance assertion.**

---

## 5. DoD deviations (flagged, not silently skipped)

- **DoD #9 — manual ×8 visual PDF inspection + screenshots.** Not executable by
  an automated CLI agent (requires a human viewing rendered PDFs; the bytes are
  Flate-compressed so no programmatic text scrape). Substituted by the
  deterministic `DocumentLabels` unit suite + the e2e wiring check.
  **Recommend Ham/Sana do the visual ×8 spot-check** (4 docs × `VatMode` on/off)
  before declaring the visual gate closed.
- **DoD #7 — `nonVat.docLabel.*` i18n.** The non-VAT doc label lives in backend
  `Tax` config and is server-rendered into the PDF; it has **no frontend string
  surface**. Adding dead i18n keys just to tick the box would be the kind of
  improvisation we avoid — intentionally NOT added. Only the rendered
  `dashboard.vatThreshold.{approaching,exceeded}` keys (th/en) were added.
- **Doc-numbering nit.** Spec said "strike plan.md §23.3"; §23.3 is the Sprint-8
  section. Sprint-8.5 recorded as **§23.4** (numbering grows; mirrors the
  §23.1/§23.3 pattern from Report-Backend9/10).

---

## 6. Gate results

| Gate | Result |
|---|---|
| Backend build (`-m:1`, U: short path) | **0 / 0** |
| `Accounting.Domain.Tests` | **41/41** (34 baseline + 7 `DocumentLabelsTests`) |
| `Accounting.Api.Tests` (native PG :5433) | **41/41** (37 baseline + 4 `Sprint85VatThresholdTests`) — **0 regression, 0 skip** |
| Frontend `tsc` | **0** |
| `next build` | **0** (no new routes — conditional render + banner only) |
| Playwright (system Edge) | **16/16** — 15 @ VatMode=true + 1 @ VatMode=false |
| Manual ×8 PDF inspection | ⚠ agent-infeasible — see §5; human spot-check recommended |
| Mirror sync `Y:\AccountApp` | ✅ |

---

## 7. Definition of Done (spec §5) — status

1. ✅ `TaxConfig` + `NonVatDocLabelTh/En` (+ Infra `VatModeOptions`, mechanism §2c)
2. ✅ PDF branched on VatMode — TI + CN/DN (inline `BuildPdfAsync`, §2a/b)
3. ✅ CN/DN legal-ref parameterized (ม.86/10·ม.86/9 ↔ ม.82/9)
4. ✅ `useSystemInfo()` exposes `vatMode`
5. ✅ e-Tax CTA gated (TI detail; RC/CN/DN have none — §2f)
6. ✅ `IVatThresholdService` + endpoint + dashboard banner
7. ◐ i18n — `vatThreshold.*` added; `nonVat.docLabel.*` intentionally omitted (§5, flagged)
8. ✅ 1 Playwright spec + threshold unit/integration tests (4) + `DocumentLabels` unit (7)
9. ⚠ Manual ×8 — agent-infeasible; substituted + human spot-check recommended (§5)
10. ✅ All automated gates green
11. ✅ Mirror sync `Y:\AccountApp`
12. ✅ `Report-Backend11.md` (this file)
13. ✅ plan.md updated — §23.4 "✅ Shipped Sprint 8.5" + Phase 2/3 ☑ bullet (numbering nit §5)

---

## 8. After this sprint

Per spec §6 and plan §23.4 order: **Sprint 8.6 — AR-side WHT**. The
`DocumentLabels` + PDF-branching foundation laid here is directly reusable by
8.6's Receipt-PDF WHT section. Sana writes the 8.6 spec when 8.5 is in P-final.

**Open items for Sana:** (i) confirm the e2e two-pass / assertion approach (§4)
is acceptable as the standing pattern for env-global features; (ii) confirm the
manual ×8 visual inspection owner (§5); (iii) confirm `nonVat.docLabel.*` i18n
is correctly omitted as dead (§5).
