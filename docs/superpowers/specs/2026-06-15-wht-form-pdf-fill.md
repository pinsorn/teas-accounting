# Spec — Auto-filled RD-form PDFs for ภ.ง.ด.1 / 3 / 53 / 54

> Ham (2026-06-15): "เน้นเอาข้อมูลมาฟิลฟอร์มให้อัตโนมัติ ไม่ต้อง submit แค่ generate เอกสารออกมาได้ก็พอ"
> + chose **build PDF-fill ใหม่ (backend)** for ภ.ง.ด.3/53/54. Goal = system generates the **filled official
> RD form PDF** (like 50ทวิ / CIT / pp01), NOT RD-API submission.

## Status / scope
| Form | Filled-PDF today | Work |
|---|---|---|
| **ภ.ง.ด.1** (payroll WHT) | ✅ `Pnd1FormFiller` + `/payroll/{id}/pnd1/pdf` | **Unblocked** — needs a POSTED payroll run (202602 is DRAFT). Seed+post a *separate* run (202601, MD-EMP-001/002 hired 2026-01-01) → generate. Manual 07.06. |
| **ภ.ง.ด.3** (WHT, individuals) | ❌ none | **Build** filler + endpoint. Data: `WhtFilingService.GeneratePnd3Async` already computes rows. |
| **ภ.ง.ด.53** (WHT, juristic) | ❌ none | **Build** (near-clone of pnd3). co2 has data (50ทวิ). |
| **ภ.ง.ด.54** (foreign ม.70) | ❌ none | **Build** + **seed ม.70 data** (co2 = 0 rows every month). |

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
- **A — ภ.ง.ด.1 (now, unblocked):** seed+post 202601 run via API → `pnd1/pdf` → `render-pdf-samples.py`
  (add pnd1) → walkthrough **07.06**. ⚠️ posting a Jan run adds a Jan row to tax-summary → **re-capture 08.02**.
  Do NOT post 202602 (breaks 06.01).
- **B — ภ.ง.ด.3 filler** (representative): decode → cells.json → filler → endpoint → FE button → test → manual.
- **C — ภ.ง.ด.53** (near-clone of B).
- **D — ภ.ง.ด.54** + seed ม.70 (foreign payment with WHT) so the form has rows.
- **E** — openapi delta (flag Sana), progress.md, ref-modal page if needed.

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
