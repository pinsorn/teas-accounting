# RD-Forms — TEAS PDF-Fill Integration Plan

> Drafted: 2026-05-30 (cont.75, overnight autonomous run). Decision input needed from Ham
> where flagged 🟠. Cross-ref: `INDEX.md`, `REPORT.md` (Sana), `Tax-Reference-TH.md`.

## TL;DR

- **A generic, /Rect-driven PDF-fill engine now exists** (`RdAcroFormFiller`). It needs **no
  per-form coordinate fine-tuning** — every value lands at the position defined by the
  template's own AcroForm field. The disaster Ham feared (tuning overlay coords per form)
  does **not** happen.
- **50ทวิ is shipped** through it (single-record withholding certificate — the one RD form
  that genuinely must be issued as a filled official PDF). ✅
- **Most other RD forms do NOT need PDF-fill.** The monthly returns (ภ.ง.ด.1/2/3/53/54,
  ภ.พ.30/36) are filed as **data → RD Open API** — which TEAS already does via the
  `TaxFilings` module (`WhtFilingService`, `TaxFilingStore` → `IRdEfilingClient.Submit*`).
  Filling their paper PDFs is throwaway for production.
- **Remaining engine-suitable targets are few and Tier 2** (ภ.พ.01 / ภ.พ.09 — print-and-sign
  onboarding). Everything else is either API-submitted or out of scope.

## The engine — what it is and its boundaries

`Accounting.Infrastructure/Pdf/RdAcroFormFiller.cs`

- **Input:** an RD AcroForm template (PDF bytes) + a `IReadOnlyCollection<RdField>`
  (field-name → value, with `Right`/`Check` flags).
- **How:** reads each field widget's `/Rect` from the template; QuestPDF (Skia/HarfBuzz —
  shapes Thai correctly **and embeds Sarabun**) renders a transparent overlay at those
  positions; PdfSharp composites it via `XPdfForm` (vector; the embedded font travels), then
  **flattens** (drops `/AcroForm` + widget `/Annots`).
- **Why not fill AcroForm `/V` + NeedAppearances:** PdfSharp can't shape Thai (drops tone
  marks like ่ mai ek), and non-Acrobat viewers don't shape Thai when regenerating field
  appearances. The overlay approach renders **identically in every viewer** (Acrobat, Chrome,
  mobile, print, headless pdfium) — proven in `_PdfSharpProbe` + visual pypdfium2 checks.
- **A new single-record form = a field-name→value mapper only** (~the 40-line
  `Wht50TawiFormFiller.MapFields`). No layout/coordinate work.

### ⚠️ Boundaries (do not assume past these)

1. **Single-page, single-record.** `ReadFieldRects`/`Composite` operate on `Pages[0]`, and the
   copy mechanism duplicates `/Kids[0]`. Multi-page templates and **tabular forms that repeat
   N rows** (every monthly return) are **not** supported without real extension work.
2. **Field-name→data mapping is compliance-critical and must be human-verified.** RD form
   fields are often generically named (`Text1.0`, `Text1.1`, …); which one is "payee TIN" vs
   "amount" can only be known by rendering + inspecting `/Rect`. Guessing a tax-return column
   is a filing error → **§11: do NOT auto-generate filled return PDFs from guessed maps.**
3. **Blanket `/Annots` removal** is safe for 50ทวิ (verified) but on an unknown form could
   drop legitimate non-widget annotations (links/stamps). Re-verify per form.

## Per-form strategy (Tier 1 + Tier 2)

| Form | Need | Strategy | Engine fit | Status |
|---|---|---|---|---|
| **50 ทวิ** | per-payee certificate (must be official PDF) | **A — PDF-fill** | ✅ single-record | **✅ DONE** |
| ภ.ง.ด.1 / 1ก | monthly/annual employee WHT | B/C — API/XML | ❌ tabular | filing via TaxFilings/API |
| ภ.ง.ด.2 | interest/dividend WHT | B/C — API/XML | ❌ tabular | filing via API |
| ภ.ง.ด.3 | individual contractor WHT | B/C — API (`SubmitPnd3Async`) | ❌ tabular (2-pg) | filing via API |
| ภ.ง.ด.53 | juristic WHT | B/C — API (`SubmitPnd53Async`) | ❌ tabular | filing via API |
| ภ.ง.ด.54 | foreign-payment WHT | B/C — API (`SubmitPnd54Async`) | ❌ tabular | filing via API |
| ภ.พ.30 (+ใบแนบ) | VAT monthly return | B/C — API (`SubmitPnd30Async`) | ❌ calc/tabular | filing via API |
| ภ.พ.36 | self-assess import-service VAT | B/C — API (`SubmitPnd36Async`) | ❌ | filing via API |
| ภ.ง.ด.50 / 51 | CIT annual / mid-year | B/C — API/XML | ❌ complex | Phase 2/3 |
| 🟠 **ภ.พ.01** | VAT registration (one-time) | **D — print/sign PDF** | ✅ single-record | candidate — confirm |
| 🟠 **ภ.พ.09** | VAT change notification | **D — print/sign PDF** | ✅ single-record | candidate — confirm |
| อ.ส.4 | stamp duty (paper) | D — conditional | ✅ likely | Tier 2, rare |

## Recommended next scope (for Ham to confirm 🟠)

1. **Treat 50ทวิ as the PDF-fill deliverable for Phase 1** — it's the only RD form that must
   be a filled official PDF per transaction. Done.
2. **Do NOT build PDF-fill for the monthly returns.** They submit as data (Open API, already
   wired). Building/maintaining 30-row tabular PDF mappers is wasted effort + compliance risk.
3. **If a print-and-sign deliverable is wanted**, ภ.พ.01 / ภ.พ.09 are the clean single-record
   candidates for the existing engine — each ~1 mapper + a verified field map. Confirm whether
   onboarding paperwork is in TEAS scope before building.
4. **Template acquisition is unblocked:** official PDFs download fine from the `_meta.md` URLs
   (Sana's sandbox blocked it; this environment does not). Fetch + embed per form *when* a
   form is greenlit for PDF-fill — not speculatively.

## Done this session

- `RdAcroFormFiller` generic engine + `Wht50TawiFormFiller` refactored to a thin mapper.
- Sarabun embedded into `Accounting.Infrastructure` (self-contained Thai rendering).
- 50ทวิ verified: build 0/0, full Api.Tests 180/180 (×2), visual render correct in headless
  pwdfium (Thai tone marks intact, 2 copies, flattened).
