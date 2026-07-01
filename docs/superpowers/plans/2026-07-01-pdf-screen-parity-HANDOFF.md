# Handoff — Self-generated PDF vs on-screen parity (items 1–3)

**Date:** 2026-07-01 · **Branch:** `fix/pdf-footer-sequence` (off `main`, 2 commits, NOT pushed/PR'd yet)
**Reporter:** Ham noticed self-generated docs (quotation, DO, tax invoice, receipt, CN/DN — NOT the RD
template forms) show different details **on-screen vs the printed PDF**: layout, logo size, typos, and
fields that appear in one but not the other.

## Root cause (established)
There are **two independent renderers** of the "same" document that have drifted:
- **On-screen** = React `frontend/components/paper/PaperDocument.tsx` (+ `PaperHead/PaperMeta/PaperItems/PaperFoot/PaperSign`), built from React-Query data (mostly **live** master data), types in `frontend/components/paper/types.ts`.
- **PDF** = QuestPDF `backend/src/Accounting.Infrastructure/Pdf/PaperDocumentPdf.cs` (+ `PaperDocModel.cs`, `PaperFootPlan.cs`), built by each doc service from **stored/snapshot** data.

`PaperDocModel.cs:3-6` states the C# model is *meant* to mirror the FE `PaperDocumentProps` 1:1 — but the two build paths diverge, so any field the two compute differently drifts.

**STRATEGIC FIX for the whole class (recommended before/around items 1–3):** have the **backend build ONE canonical paper DTO** (the `PaperDocModel` data — composed notes, resolved customer fields, totals) and serve it to BOTH the PDF renderer AND the FE screen (the FE consumes the server DTO instead of re-deriving from live master). Then field presence + values + notes always match; the only allowed difference is pixel layout (React vs QuestPDF). Items 1–3 below are the concrete symptoms; the DTO unification is the durable cure.

## ✅ DONE this session (branch `fix/pdf-footer-sequence`)
- **Footer sequence** (Ham's spec) fixed in BOTH renderers — screen==print now:
  `Subtotal·VAT (only if the company charges VAT) → จำนวนเงินรวมทั้งสิ้น/Grand Total (ALWAYS) → หัก WHT → ยอดรับสุทธิ/Net (only if WHT)`; lines that duplicate the Grand Total anchor are hidden.
  - New pure+tested `PaperFootPlan.cs` (`PaperFootPlanTests` 4 cases); `PaperDocumentPdf.Foot` renders from it; FE `PaperFoot.tsx` mirrors the same sequence + labels. commit `cd88bba` (BE) + `8d7336f` (FE).
  - **Money semantic to KNOW:** `PaperSummary.Total` = the **NET** when `Wht` is set (BE PV passes `Total=d.TotalPaid`), so `grand = Total + Wht`. The FE renderer treats `summary.total` as the **Grand Total** with `net = total − wht`. Both DISPLAY the same grand+net; do NOT "align" them blindly — verify per doc which value each page/service feeds.
- **Receipt blank-reference bug** fixed: `ReceiptService.Read.cs` only appends `"อ้างอิงใบกำกับภาษี: …"` when there ARE applied TIs (was printing a dangling blank on non-VAT / cash receipts). commit `cd88bba`.
- **`Microsoft.OpenApi` 2.7.5 security pin** — **main's build is currently RED** on NU1903/GHSA-v5pm-xwqc-g5wc (pre-existing latent CVE surfaced by a fresh restore). This branch pins it (Directory.Packages.props + direct ref in Api/Api.Tests csproj). Keep this in whatever PR merges first. (The OAuth PR #31 also carries the same pin — whichever lands first fixes main.)
- **Verified:** build 0/0 · `PaperFootPlanTests` 4/4 · `PurchasePdfTests` 7/7 (PV/WHT footer) · FE `tsc` 0.

---

## ITEM 1 — ใบเสร็จ should show the VAT/WHT breakdown when applicable
**Now:** `backend/.../Sales/ReceiptService.Read.cs` BuildPdfAsync (~L221) passes a bare summary (no VAT, no WHT — comment L204-205: *"No VAT row … WHT is NOT printed on the receipt (only recorded)"*). The receipt screen (`frontend/app/(dashboard)/receipts/[id]/page.tsx:157`) passes `summary={{ subtotal: d.amount, vat: 0, total: d.amount }}`.
**Want:** when the customer withheld WHT (receipt has `WhtAmount` + `ReceiptWhtLines`), the receipt footer should show the full sequence `Subtotal → VAT → Grand → หัก WHT → Net(received)`.
**Approach:** feed `PaperSummary` from the receipt's real numbers — VAT (from the settled TIs), `Wht = d.WhtAmount` (when > 0), and `Total` per the money-semantic note above. Mirror on the FE receipt page's `summary` prop.
**DECISION NEEDED FROM HAM:** the receipt "settles already-VAT'd TIs" — does it (a) show the VAT breakdown again, or (b) only Grand → หัก WHT → Net without a VAT row? Ask before wiring. Also confirm whether `d.amount` on a WHT receipt is the gross (grand) or the net received.
**Files:** `ReceiptService.Read.cs`, `receipts/[id]/page.tsx`, receipt DTO (`ReceiptService.Read.cs` GetDetailAsync — check it exposes `WhtAmount`/subtotal/vat).

## ITEM 2 — screen must show the "อ้างอิงใบกำกับภาษี" note when there IS a real reference
**Now:** the PDF composes `notes = d.Notes + "อ้างอิงใบกำกับภาษี: {tiRefs}"` (`ReceiptService.Read.cs`), but the screen passes only raw `d.notes` (`receipts/[id]/page.tsx:158`). So a receipt with real applied TIs shows the reference on the PDF but NOT on-screen.
**Approach (do this the DTO way = the strategic fix in miniature):** compose the display-notes ONCE in the backend receipt DTO (`GetDetailAsync` returns `notes` already including the ref line when applicable), and have BOTH the PDF service and the FE page consume that single field. Removes the drift instead of duplicating the string-building on the FE.
**Files:** receipt DTO + `ReceiptService.Read.cs` (stop composing in BuildPdf; compose in the DTO) + `receipts/[id]/page.tsx`.

## ITEM 3 — other screen↔PDF divergences
- **Logo size/rendering:** `backend/.../Pdf/PaperSellerSource.cs` embeds the logo as a **raster** and **skips SVG**; the FE uses `logoUrl` (may be an SVG) → header logo differs / missing on PDF for SVG logos. Fix: rasterize SVG for the PDF (or standardize the stored logo format) + match sizing.
- **Q/SO/DO customer fields:** the FE detail pages (e.g. `quotations/[id]/page.tsx:~182`) render the customer from the **live customer master** (`useCustomer` → taxId/branchCode/billingAddress/contact/phone), while the PDF (`backend/.../Sales/SalesChainPdfService.cs:~70`) uses the **stored doc snapshot** and passes `branchCode=null` + no contact/phone. → screen shows branch/contact/phone the PDF omits (and if the customer master changed after the doc was drafted, screen=new / PDF=old). Fix via the DTO-unification (single customer source) or make the PDF carry the same fields.
- **Layout / typos:** a visual pass comparing each doc type's screen vs PDF (spacing, labels, wording). Low-risk, case-by-case.
- **(context) TaxInvoice date drift** (NOT screen-vs-print, but related, obs 8329): `PostAsync` re-pins `DocDate`+`TaxPointDate` to the post day (`TaxInvoiceService.cs:300-302`), so a TI drafted Mon and posted Tue prints a different date before vs after post. Separate finding; flag if it resurfaces.

---

## Env / footguns for the next session (§6)
- subst drives vanish on resume — `subst U: <repo>`, `subst W: <repo>\backend`. Run `dotnet`/`ef` from **W:** (PowerShell `Set-Location W:\`; bash `cd //W/` does NOT switch to a subst drive).
- Build from the REAL path or W: (0/0). Integration tests need `$env:TEAS_TEST_PG=Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`. RBAC tests (`RbacAuthMapTests`) additionally need `$env:TEAS_REPO_ROOT=Y:\ClaudePlayground\TEAS-Project` AND flag any new authn-only/anonymous endpoint in their allowlists — see [[teas-repo-root-rbac-tests]].
- FE gate: `node node_modules/typescript/bin/tsc --noEmit` = 0 (pnpm/next often not on PATH). Never `next build` while `next dev` runs.
- **Do NOT commit on `feat/mcp-oauth`** (that's the OAuth PR #31) — this PDF work is its own branch off main.
- TDD the shared footer/notes logic as a pure function (like `PaperFootPlan`) so BE+FE can't silently drift again.

## Verification gates before PR
build 0/0 · new pure-logic unit tests 2× · `PurchasePdfTests` + any sales-PDF integration green on teas_test · FE `tsc` 0 · a manual screen-vs-PDF eyeball on one VAT+WHT doc, one non-VAT doc, and one receipt.
