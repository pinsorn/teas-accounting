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

## REMAINING
1. **Manual walkthroughs 07.07–07.10** (the main remaining work — heavy Playwright). Add render-pdf-samples
   targets (pnd30/pnd3/pnd53/pnd54) + ch7 walkthroughs embedding `showPdfSample()`, then gen-md + mkdocs.
   See `docs/superpowers/plans/2026-06-15-tax-form-pdf-fill.md` B8/C8/D8/E8. Filter the co2 ภ.ง.ด.53
   e2e-noise payee names (or seed clean) for the sample.
2. **ภ.ง.ด.54 ม.70 seed** — co2 has 0 ม.70 rows, so the REAL pnd54 shows no amounts (only the diagnostic does).
   Seed a foreign-vendor PV with a ม.70 WHT line so `GeneratePnd54Async(period)` returns ≥1 row, then
   render-verify the real pnd54 amounts.
3. **ภ.ง.ด.3 ใบแนบ verify** — seed one INDIVIDUAL payee (PND3) and render-verify the ใบแนบ row layout
   (the pnd3 attach field scheme `Text{k}.27/.1/...` is a best-guess, never exercised with data).
4. **`docs/manual/reference-modals-buttons.md` §3** — add the new "ดาวน์โหลด PDF" buttons (Phase F2, not done).
5. **Branch decision** — this rides on `feat/rbac-per-company-admin-ui` (unrelated RBAC + manual work).
   Decide PR/merge scope with Ham.
6. **(optional)** WHT total digit spacing is a bit loose (each char in a cell so the `.` lands on the divider);
   tighten the baht cell width in `pnd3_cells.json`/`pnd53_cells.json` if Ham wants it more compact.

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
