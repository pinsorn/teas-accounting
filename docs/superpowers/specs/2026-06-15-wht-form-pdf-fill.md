# Spec — Auto-filled RD-form PDFs for ภ.ง.ด.1 / 3 / 53 / 54

> Ham (2026-06-15): "เน้นเอาข้อมูลมาฟิลฟอร์มให้อัตโนมัติ ไม่ต้อง submit แค่ generate เอกสารออกมาได้ก็พอ"
> + chose **build PDF-fill ใหม่ (backend)** for ภ.ง.ด.3/53/54. Goal = system generates the **filled official
> RD form PDF** (like 50ทวิ / CIT / pp01), NOT RD-API submission.

## Status / scope
| Form | Filled-PDF today | Work |
|---|---|---|
| **ภ.ง.ด.1** (payroll WHT) | ✅ DONE (07.06) | `Pnd1FormFiller` + `/payroll/runs/{id}/pnd1/pdf` (works on DRAFT run — no post needed). |
| **ภ.พ.30** (VAT return) | ❌ none (07.01 = on-screen only) | **Build** filler + endpoint. Data: `GetPnd30Async` (the 07.01 `Pnd30Filing`). Template `docs/RD-Forms/pp30/pp30_010968.pdf` = AcroForm 2p/76 widgets (+ `pp30-2/PP30.2` + `pp30-attach/AttachPP30`). Ham asked for it (2026-06-15 "30 ละ"). |
| **ภ.ง.ด.3** (WHT, individuals) | ❌ none | **Build** filler + endpoint. Data: `WhtFilingService.GeneratePnd3Async` rows. Template `pnd3_270360.pdf` = AcroForm 2p / 54 widgets p1 (+ `pnd3_attach`). |
| **ภ.ง.ด.53** (WHT, juristic) | ❌ none | **Build** (near-clone of pnd3). co2 has data (50ทวิ). Template `pnd53_041060.pdf` (+attach). |
| **ภ.ง.ด.54** (foreign ม.70) | ❌ none | **Build** + **seed ม.70 data** (co2 = 0 rows every month). Template `pnd54_050369.pdf`. |

## Assets (confirmed present)
- Templates in **`docs/RD-Forms/`** (untracked scratch — do NOT commit that dir; COPY into `Pdf/Templates/`):
  `pnd3/pnd3_270360.pdf` (+`pnd3_attach.pdf`) · `pnd53/pnd53_041060.pdf` (+attach) · `pnd54/pnd54_050369.pdf`.
- Existing embedded templates + fillers prove the pattern: `Pnd51FormFiller` / `Pnd50FormFiller` / `Wht50TawiFormFiller`
  / `VatRegFormFillers` (pp01/09), generic `RdAcroFormFiller.Render(template, fields, radios, cellCenters)`.

## Build pattern (per form, from pnd51)
1. Copy `docs/RD-Forms/<f>/<f>_NNNNNN.pdf` → `backend/.../Pdf/Templates/<f>_main.pdf` (+ `<f>_attach.pdf`);
   add `<EmbeddedResource>` lines to `Accounting.Infrastructure.csproj`.
2. **Decode AcroForm fields** of the template: field name → `/Rect`. Use PyMuPDF (`fitz`, available) —
   `for w in page.widgets(): w.field_name, w.rect`. Join with printed labels to map field→meaning →
   write `<f>_fieldmap.md`. For comb (กล่องช่องๆ) amount fields, extract per-cell centre X →
   `<f>_cells.json` (embed). (Same offline decode the team did for pnd51 via field-dump + label-join.)
3. `Pnd{3,53,54}FormFiller.cs` — `record <F>Model(...)` + `Fill(model)` mapping to `RdField`/`RdRadio`.
   Header (payer taxId/name/address, period, filing date) is shared; body = per-payee rows on the **ใบแนบ**
   (attach page) + page-1 totals. Right-justify comb amounts (baht / satang split, no comma).
4. **Service + endpoint**: extend `IWhtFilingService` (or new) → `Build<F>PdfAsync(period, ct)` reusing the
   existing `Generate<F>Async` computation for rows; map → `<F>Model` → filler. Add
   `GET /tax-filings/{pnd3,pnd53,pnd54}/pdf?period=YYYYMM` in `TaxFilingEndpoints.cs`
   (`.RequireAuthorization(preview)`), `Results.File(..., "application/pdf")`. Update openapi.yaml.
5. **FE**: add "ดาวน์โหลด PDF" button on the WHT filing pages (`WhtFilingClient` + pnd36/pnd54 page) →
   `openPdf('tax-filings/<f>/pdf?period=…')`.
6. **Tests** (Api): a smoke test that the endpoint 200s + returns a multi-page PDF for a period with certs
   (use `TestCompanyFactory` + post a PV→50ทวิ; never co1). Render-verify the filled boxes visually once.
7. **Manual**: render page-1 via `render-pdf-samples.py` → embed in a ch7 walkthrough (07.0x), like 50ทวิ.

## Phasing (commit per phase)
- **A — ภ.ง.ด.1 ✅ DONE (07.06):** `/payroll/runs/{id}/pnd1/pdf` works on the DRAFT run → no seed/post,
  no 08.02 cascade. Rendered + embedded.
- **B — ภ.พ.30 filler** (Ham-prioritised): decode `pp30_010968.pdf` (76 widgets) → map `Pnd30Filing`
  (ขาย/ภาษีขาย, ซื้อ/ภาษีซื้อ, สุทธิ) → filler → `GET /tax-filings/pnd30/pdf?period=` → FE button on
  `/reports/pnd30` → test → manual (07.0x). Representative AcroForm build.
- **C — ภ.ง.ด.3 filler** (53/54 follow this shape): decode → cells.json → filler → endpoint → FE → test → manual.
- **D — ภ.ง.ด.53** (near-clone of C).
- **E — ภ.ง.ด.54** + seed ม.70 (foreign payment with WHT) so the form has rows.
- **F** — openapi delta (flag Sana), progress.md, ref-modal page if needed.

## Build mechanics per form (the long part)
Field→box mapping is the cost: dump widgets (`fitz` `page.widgets()` → name + rect), render the BLANK
template to PNG, visually map each `Text*` field to its printed box (pnd51 did this via a label-join), then
fill + render + eyeball + nudge coordinates (comb/`cells.json` for grid amount boxes). Each form = its own
decode + render-verify loop. Compliance-critical (a wrong box = a wrong filing) → eyeball every filled output
against the official form before committing.

## Compliance / guardrails
- WHT amounts on the form MUST equal the issued 50ทวิ certs (cite the คำสั่ง/section in tests).
- "generate only, no submit" — no RD-API call; PDF is print-and-file.
- §6 footguns: build the **solution** first (kill API on :5080 before a full build that relocks `Accounting.Api.exe`);
  no EF migration expected (PDF endpoints only). Run tests from `W:` with `TEAS_TEST_PG`.
- Templates are official RD PDFs → embed under `Pdf/Templates/` (committed, like pnd1_main.pdf); the
  `docs/RD-Forms/` source dir stays uncommitted scratch.

## Effort note
Each form's field-decode + render-verify is intricate (pnd51's comments show "off by one field" / label-join /
coordinate drift). This is a multi-step build per form, best done with focused render-verify loops — not a
one-shot. Phase A ships value immediately; B–D are the real engineering.
