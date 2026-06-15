# Handoff — tax-form PDF-fill (next session prompt)

> Paste the **PROMPT** block below into a fresh session. Context recorded in `progress.md` (cont.98j, top).

## DONE (committed, branch `feat/rbac-per-company-admin-ui`, 17 commits `e8a1a79`..`01081dc`)
4 RD tax-forms generate filled, print-and-file PDFs (no RD submission), all render-verified box-for-box:
- **ภ.พ.30** (full): `Pnd30FormFiller` + `GET /tax-filings/pnd30/pdf` + FE button on `/reports/pnd30`.
- **ภ.ง.ด.3 / ภ.ง.ด.53** (main page + multi-page ใบแนบ): `WhtFormFiller` + endpoints + FE button (WhtFilingClient).
  ภ.ง.ด.53 verified with co2's 13 real rows; ภ.ง.ด.3 = main page (co2 has 0 individual payees).
- **ภ.ง.ด.54** (single-page ม.70, header + amounts): `Pnd54FormFiller` + endpoint + FE button.
- Comb alignment solved (the hard part): non-uniform taxId (1-4-5-2-1) + amount baht|satang dividers →
  `<form>_cells.json` cell-centres (line-extract `_scratch/extract_cells.py`; dashed/group combs via
  group-rect + pixel `_scratch/pixel_cells.py`); WHT totals put the decimal `.` on the satang divider.
- income description (not the code), เงื่อนไข 1/2, pay-date, ☑ใบแนบ + ราย/แผ่น counts, radio on-state selection.
- `TaxFormFillDiagnostic` (gated `TEAS_DIAG=1`) fills EVERY box with synthetic data → `docs/RD-Forms/_fills/_diag_*.pdf`.
- openapi.yaml (4 GETs), smoke tests (pass 2× on teas_test).

## REMAINING — STATUS (updated cont.98k 2026-06-15)
1. ✅ **DONE — Manual walkthroughs 07.07–07.10.** render-pdf-samples targets + 4 `showPdfSample()` walkthroughs +
   run-capture register + capture 4/4 + gen-md (41wt/159steps) + mkdocs 0 err + ম=0, eyeballed 4/4. Page-1-only
   samples = the main page, so the co2 ภ.ง.ด.53 e2e-noise (which lives on the ใบแนบ pages) never appears — no filtering needed.
2. ✅ **DONE (cont.98l) — ภ.ง.ด.54 ม.70 routing (Decision 1 Tier A, Ham approved).** `PaymentVoucherService`
   now honours `whtType.FormType == Pnd54` (FOR-SVC / FOR-ROYAL) → a ม.70 income type routes the cert to
   ภ.ง.ด.54 regardless of payee kind (the income type, not a vendor flag, is the ม.70 discriminator — a foreign
   co. with a Thai PE still files ภ.ง.ด.53). Per-line WHT rate already user-editable → 15% (no treaty) vs 10%/DTA.
   `BuildPnd54PdfAsync` now multi-sheet (was Rows[0] only). Seed 251 backfills FOR-* for older companies (co2).
   Verified end-to-end on co2 (PV → Pnd54 cert → ภ.ง.ด.54 PDF with 50,000×15%=7,500) + the matrix test
   (`WhtFormRoutingTests`, 2×). **DTA reduced-rate modelling per treaty = deferred Tier B.**
   ⚠️ The end-to-end co2 seed PV left co2 LIVE data polluted (it's a real FY2026 expense → breaks the CIT /
   ภ.พ.36 / tax-summary tie-outs); it can't be cleanly deleted (immutability triggers, no void endpoint).
   **Cleanup for Ham** (manual was reverted to honest, so committed docs are clean): recreate accounting_dev from
   seed (cleanest — the PV was runtime-added, not seeded), or post a reversing JE (partial), or add a void feature.
3. ✅ **DONE — ภ.ง.ด.3 ใบแนบ — and it was BROKEN.** The best-guess scheme was wrong for every row: pnd3_attach
   uses a flat `Text1.*` namespace (header at `.0–.3`), so row-1 data is shifted +3 (taxId=`Text1.4`, not `.1`)
   and rows 2–6's date→cond block starts at `.6` not `.9`. Fixed `Pnd3Layout.AttachRow` (k==1 branch); render-
   verified every column + the now-filled row-1 taxId; guarded by `WhtFormPdfFillTests` (pass 2×).
4. ✅ **DONE — `reference-modals-buttons.md` §3.7** — added the "ดาวน์โหลด PDF" buttons (ภ.พ.30 / ภ.ง.ด.3/53/54).
5. 🛑 **HELD (cont.98l) — Branch scope (Decision 2).** Ham approved merge *conditional on a green suite*. Full
   suite: Domain 146/146 ✅, but Api has **11 PRE-EXISTING reds** (confirmed identical at session-start 39d0a2a —
   NOT this work): 9× stale test DI (hand-built providers don't register `IFileStorageService`, needed by the
   `PaymentVoucherService` ctor since Sprint 13k) + 2× RBAC map/matrix drift on this RBAC branch. Per
   "don't merge over red / don't fix pre-existing reds overnight" → merge HELD for Ham: fix those reds (the 9 DI
   ones look like a small shared test-provider fix; the 2 RBAC ones are the branch owner's), then merge — OR merge
   accepting the pre-existing debt. The tax-pdf work is fully green and committed on the branch.
6. **(optional)** WHT total digit spacing slightly loose — tighten `pnd3_cells.json`/`pnd53_cells.json` if Ham wants.

## ENV (hard-won — read §6 of CLAUDE.md)
- `subst U:`/`W:`; build/test/run from `W:`. Kill :5080 before a full solution build, restart after:
  `cd W:\; $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'; dotnet run --project src\Accounting.Api` (bg).
- Tests from `W:\tests\Accounting.Api.Tests` with `$env:TEAS_TEST_PG=...teas_test...` (login co2 = `demo-admin`/`Demo@1234`).
- Render-verify a filled PDF: `python docs/RD-Forms/_scratch/fill_render.py <key> "<endpoint?query>"` → writes
  `docs/RD-Forms/_fills/<key>.pdf` + page PNGs. Cell-overlay-on-blank before trusting a new cells.json.
- Commit messages slash-free. grep Bengali `ม` = 0 before doc commits. NEVER stage `docs/RD-Forms`,
  `frontend/screenshots`, `docs/SSO-Forms` (other-session scratch). `_fills/` + `_scratch/` are review-only.

---

## PROMPT (paste into next session)
```
อ่าน CLAUDE.md → progress.md (cont.98j ด้านบน) → docs/superpowers/plans/2026-06-15-tax-form-pdf-fill.md
+ docs/superpowers/plans/2026-06-15-tax-form-pdf-fill-HANDOFF.md ก่อน.

tax-form PDF-fill (ภ.พ.30 + ภ.ง.ด.3/53/54) ของจริง 4 ฟอร์ม ทำเสร็จ + alignment ครบแล้ว (committed
branch feat/rbac-per-company-admin-ui). งานที่เหลือ เรียงความสำคัญ:

1) (หลัก) คู่มือ ch7 07.07–07.10 — เพิ่ม target ใน frontend/manual/render-pdf-samples.py (pnd30/pnd3/
   pnd53/pnd54) + walkthrough 07.07-pnd30 / 07.08-pnd3 / 07.09-pnd53 / 07.10-pnd54 ฝัง showPdfSample()
   + register run-capture.spec.ts → capture → gen-markdown.mjs → mkdocs build. กรอง e2e-noise ในชื่อ
   payee ของ ภ.ง.ด.53 co2. ดู INSTRUCTIONS 2026-06-14-manual-build-all-modules.
2) seed ม.70 (PV ต่างประเทศ + WHT line FormType=Pnd54) ให้ GeneratePnd54Async มี ≥1 row → render-verify
   ภ.ง.ด.54 จริงมี amount.
3) seed individual payee (PND3) → verify ใบแนบ ภ.ง.ด.3 layout (scheme เดายังไม่เคยมี data).
4) docs/manual/reference-modals-buttons.md §3 เพิ่มปุ่ม "ดาวน์โหลด PDF".

env: §6 (subst W:, kill :5080 ก่อน full build, TEAS_TEST_PG, login demo-admin/Demo@1234).
render-verify: python docs/RD-Forms/_scratch/fill_render.py <key> "<endpoint>". commit slash-free,
grep ม=0, ห้าม stage docs/RD-Forms · frontend/screenshots · docs/SSO-Forms.
```
