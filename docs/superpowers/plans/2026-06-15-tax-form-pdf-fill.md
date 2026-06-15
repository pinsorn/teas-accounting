# Tax-Form PDF-Fill (ภ.พ.30 + ภ.ง.ด.3/53/54) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to implement task-by-task. Steps use `- [ ]` checkboxes.
> **Read first:** the spec `docs/superpowers/specs/2026-06-15-wht-form-pdf-fill.md`, then this plan top-to-bottom,
> then §0 (environment) before touching anything.

**Goal:** Make the system generate the official, auto-filled RD-form PDFs for **ภ.พ.30** (VAT return) and
**ภ.ง.ด.3 / ภ.ง.ด.53 / ภ.ง.ด.54** (WHT remittance) — print-and-file, **no RD submission** — and document each in
the user manual (how to use it).

**Architecture:** Reuse the existing `RdAcroFormFiller.Render(template, fields, radios, cellCenters)` engine
(proven for ภ.ง.ด.1/1ก/50/51/50ทวิ/ภ.พ.01/09). Per form: embed the RD AcroForm template → decode its field
names→boxes → write a `<Form>FormFiller` mapping the already-computed filing data onto the form → expose a
`GET /tax-filings/<form>/pdf` endpoint → add an FE download button → render page-1 into the manual via the
existing `render-pdf-samples.py` + `showPdfSample()` pipeline.

**Tech Stack:** .NET 10 / EF Core / Minimal APIs (BE) · PyMuPDF `fitz` (field decode + render-verify) ·
Next.js 15 (FE button) · Playwright manual-capture + MkDocs (manual). No EF migration (PDF endpoints only).

---

## §0. Environment briefing (READ — fresh session, hard-won)

- **subst drives** (recreate if missing): `subst U: <repo>\code`, `subst W: <repo>\code\backend`. Run
  `dotnet build/test/ef/run` **from `W:`** (long real path throws Win32Exception 87 otherwise).
- **Backend** on `http://localhost:5080`, MUST run `ASPNETCORE_ENVIRONMENT=Development`. The running API **locks
  `Accounting.Api.exe` + dependency DLLs** → before any **full solution build that touches Infrastructure**,
  KILL it: `Get-NetTCPConnection -LocalPort 5080 -State Listen | % { Stop-Process -Id $_.OwningProcess -Force }`,
  build, restart: `cd W:\; $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'; dotnet run --project src\Accounting.Api` (background).
- **Frontend** on `http://localhost:3000`. pnpm often not on PATH → `node node_modules\next\dist\bin\next dev`.
  Never `next build` while `next dev` runs. `tsc --noEmit` is the fast FE gate.
- **Login:** `demo-admin / Demo@1234` (co2, manual demo). super-admin `admin / Admin@1234` (co1 — do NOT mutate).
- **Tests:** from `W:\tests\Accounting.Api.Tests` with `$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'`.
  Every inserted row with a UNIQUE constraint uses `Accounting.TestKit.TestIds.*`; new companies via
  `TestCompanyFactory.CreateAsync` — **never flip co1**. Pass new tests **2× consecutive** on the shared DB.
- **PowerShell sandbox gotcha:** a literal `" / "` in a `git commit -m` string is rejected ("Remove-Item on
  system path '/'") → write commit messages slash-free.
- **Manual pipeline** (see also `docs/superpowers/plans/2026-06-14-manual-build-all-modules-INSTRUCTIONS.md`):
  capture `cd frontend && node node_modules/@playwright/test/cli.js test -c manual/playwright.config.ts -g "<id>"`
  · gen `node manual/gen-markdown.mjs` · build `cd docs/manual && python -m mkdocs build -f mkdocs.yml`.
  PDF samples: `python manual/render-pdf-samples.py` (renders RD PDFs→`manual/pdf-samples/*.png`).
- **Commit discipline:** stage `frontend/manual docs/manual docs/_site` (+ backend paths) — NEVER
  `frontend/screenshots`, `docs/RD-Forms`, `docs/SSO-Forms` (other-session scratch). After every doc change:
  gen-md + mkdocs build + grep `ম` (Bengali) must be **0**.

## Reference pattern (study before Phase B)

- Filler: `backend/src/Accounting.Infrastructure/Pdf/Pnd51FormFiller.cs` — `record Pnd51Model(...)`,
  `Fill(model)` builds `List<RdField>` (`new("Text1.1", value, Right: true)` for right-justified combs) +
  `List<RdRadio>` (`new("Radio Button1", 0)`), then `RdAcroFormFiller.Render(Template("pnd51_main.pdf"),
  fields, radios, CellCenters.Value)`. Comb (กล่องช่องๆ) amount fields read per-cell-centre X from an embedded
  `<form>_cells.json`. `Template()`/`LoadCellCenters()` read embedded resources.
- Engine: `backend/src/Accounting.Infrastructure/Pdf/RdAcroFormFiller.cs` — read its `Render` signature +
  `RdField`/`RdRadio` records before writing a filler.
- Templates embedded via `<EmbeddedResource Include="Pdf\Templates\<f>_main.pdf" />` in
  `Accounting.Infrastructure.csproj` (+ Sarabun fonts already embedded).
- Endpoints: `backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs` — existing
  `app.MapGet("/tax-filings/pnd51/pdf", ...).WithTags("TaxFilings").RequireAuthorization(preview)` is the shape.
  `preview` is the policy const already in that file.

## Field-decode method (every form — the empirical core)

```bash
# from repo root; PYTHONIOENCODING=utf-8
python -c "import fitz; d=fitz.open(r'docs/RD-Forms/<dir>/<file>.pdf'); \
[print(pi+1, w.field_name, w.field_type_string, [round(x) for x in w.rect]) \
 for pi,pg in enumerate(d) for w in (pg.widgets() or [])]"
```
Then render the BLANK template to PNG (`pg.get_pixmap(dpi=140).save(...)`), open it, and visually map each
`Text*`/`Radio*` field to its printed box → record in `Pdf/Templates/<form>_fieldmap.md`. For comb amount
boxes, extract per-cell-centre X (divider midpoints) → `<form>_cells.json`. This decode + the later
**render-verify loop** (fill → render page → eyeball vs the official form → nudge) is the real cost per form.

## File structure (created/modified)

```
backend/src/Accounting.Infrastructure/
  Pdf/Templates/  pnd30_main.pdf · pnd30_cells.json · pnd30_fieldmap.md   (Phase B; repeat per form)
                  pnd3_main.pdf · pnd3_attach.pdf · pnd3_cells.json · pnd3_fieldmap.md   (Phase C)
                  pnd53_main.pdf · pnd53_attach.pdf · …   (Phase D)   pnd54_main.pdf · …  (Phase E)
  Pdf/  Pnd30FormFiller.cs · Pnd3FormFiller.cs · Pnd53FormFiller.cs · Pnd54FormFiller.cs
  <existing VAT/WHT service>.cs   (add Build<Form>PdfAsync — reuse GetPnd30Async / Generate<F>Async)
  Accounting.Infrastructure.csproj   (add <EmbeddedResource> lines)
backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs   (add GET …/pdf per form)
backend/tests/Accounting.Api.Tests/…   (one smoke test per endpoint)
frontend/app/(dashboard)/reports/pnd30/page.tsx · components/tax-filings/WhtFilingClient.tsx ·
  app/(dashboard)/tax-filings/pnd54/page.tsx   (add "ดาวน์โหลด PDF" button)
frontend/manual/render-pdf-samples.py   (add pnd30/pnd3/pnd53/pnd54 targets)
frontend/manual/walkthroughs/07.07-pnd30-pdf.ts … 07.10-pnd54-pdf.ts + run-capture.spec.ts
docs/manual/* (generated) · docs/api/openapi.yaml · progress.md
```

---

## Phase B — ภ.พ.30 (VAT return) filler  *(representative; build this fully first)*

**Files:** Template `docs/RD-Forms/pp30/pp30_010968.pdf` (AcroForm, 2p, 76 widgets) →
`Pdf/Templates/pnd30_main.pdf`. Data: the existing `Pnd30Filing` returned by `GetPnd30Async` (same object the
07.01 page shows: `lines.salesTaxable{amount,vat}`, `salesZeroRated`, `salesExempt`, `outputVatTotal`,
`purchaseTaxable{amount,vat}`, `purchaseProportionalApportionment{claimRatio,claimableAmount}`, `inputVatTotal`,
`netVatPayable`, `creditCarryForward`; `company{nameTh,taxId,...address}`; `filingDueDate`).

- [ ] **B1: Decode the template fields.** Run the field-decode command on `pp30_010968.pdf`; render the blank
  form to PNG; create `Pdf/Templates/pnd30_fieldmap.md` mapping each `Text*`/`Radio*` → ภ.พ.30 box
  (taxId · branch · name · address · period month/year · ยอดขาย & ภาษีขาย · ยอดซื้อ & ภาษีซื้อ · ภาษีสุทธิ
  ชำระ/ยกไป · radios for ชำระ/ขอคืน). Commit the fieldmap + the blank render note.

- [ ] **B2: Embed the template.** Copy `pp30_010968.pdf` → `Pdf/Templates/pnd30_main.pdf`. Add
  `<EmbeddedResource Include="Pdf\Templates\pnd30_main.pdf" />` (and `pnd30_cells.json` once it exists) to
  `Accounting.Infrastructure.csproj`. Build the **solution** (kill :5080 first): `dotnet build W:\Accounting.sln`.
  Expected: 0 errors.

- [ ] **B3: Write `Pnd30FormFiller`.** Create `Pdf/Pnd30FormFiller.cs` with the Pnd51FormFiller structure:
  `record Pnd30Model(string TaxId, string Branch, string Name, /*address parts*/, int PeriodMonth, int PeriodYearCe,
  decimal SalesTaxable, decimal OutputVat, decimal PurchaseClaimable, decimal InputVat, decimal NetPayable,
  bool IsRefund)`, a static `Fill(Pnd30Model m)` that builds `RdField`/`RdRadio` from the B1 fieldmap (baht/satang
  split for combs, `Right: true`), and `RdAcroFormFiller.Render(Template("pnd30_main.pdf"), fields, radios,
  CellCenters.Value)`. Copy the `Template()`/`CellCenters`/`LoadCellCenters()` helpers from Pnd51FormFiller
  (or factor a shared `RdTemplate` helper — judgment call; if factoring, do it in B3 and update existing fillers).

- [ ] **B4: Service + endpoint.** Find the service exposing `GetPnd30Async` (grep `GetPnd30Async` in
  `Accounting.Infrastructure`); add `Task<byte[]> BuildPnd30PdfAsync(int period, CancellationToken ct)` that
  calls the existing computation, maps `Pnd30Filing` → `Pnd30Model`, returns `Pnd30FormFiller.Fill(model)`.
  In `TaxFilingEndpoints.cs` add:
  ```csharp
  app.MapGet("/tax-filings/pnd30/pdf", async ([FromQuery] int period, I<Svc> svc, CancellationToken ct) =>
      Results.File(await svc.BuildPnd30PdfAsync(period, ct), "application/pdf", $"pnd30-{period}.pdf"))
      .WithTags("TaxFilings").RequireAuthorization(preview);
  ```
  Build solution (kill :5080), restart API.

- [ ] **B5: Render-verify loop (compliance gate).** `python -c` GET `/tax-filings/pnd30/pdf?period=202606`
  (login demo-admin), render page 1 with fitz, **eyeball against the official ภ.พ.30**: every amount in the
  right box, VAT shown separately, taxId/branch/period correct, ยอด tie to 07.01 (ขาย 98,600 / ภาษีขาย 6,902 /
  ภาษีซื้อ 1,281 / สุทธิ 5,621). Nudge `pnd30_cells.json` / field map until pixel-correct. Commit filler + cells.

- [ ] **B6: API smoke test.** In `Accounting.Api.Tests`, add a test: `TestCompanyFactory.CreateAsync` (VAT) →
  post a tax invoice in a period → `GET /tax-filings/pnd30/pdf?period=…` returns 200 + `application/pdf` +
  a non-trivial byte length + `fitz` page_count ≥ 1. Run from `W:\tests\Accounting.Api.Tests` with `TEAS_TEST_PG`;
  must pass **2× consecutive**. Commit.

- [ ] **B7: FE download button.** In `frontend/app/(dashboard)/reports/pnd30/page.tsx`, add a "ดาวน์โหลด PDF
  (ภ.พ.30)" button next to ดูตัวอย่าง/ยืนยัน that calls `openPdf(\`tax-filings/pnd30/pdf?period=${toPeriod(ym)}\`)`
  (import `openPdf` from `@/lib/api`; see CIT page `downloadPnd50`). Add i18n keys (`report.pnd30DownloadPdf`)
  to BOTH `messages/th.json` + `en.json`. `tsc --noEmit` 0. Commit.

- [ ] **B8: Manual (how-to-use).** Add `pnd30` to `render-pdf-samples.py` targets
  (`/tax-filings/pnd30/pdf?period=<current 202606>`; period like the cert/run discovery). Run the renderer.
  Create `frontend/manual/walkthroughs/07.07-pnd30-pdf.ts` (chapter 7) embedding `showPdfSample(page,
  'pnd30-p1.png')` with a caption explaining: เปิด /reports/pnd30 → เลือกงวด → "ดาวน์โหลด PDF" → ได้แบบ ภ.พ.30
  กรอกแล้ว (ภาษีขาย−ภาษีซื้อ=สุทธิ). Register in `run-capture.spec.ts`. Capture `-g "07.07"`, **eyeball** the
  in-manual PNG, `node manual/gen-markdown.mjs`, `mkdocs build`, grep `ম`=0. Commit (`frontend/manual docs/manual
  docs/_site`).

## Phase C — ภ.ง.ด.3 (WHT, individuals) filler

**Delta from Phase B** (same filler structure, NEW decode):
- Template `docs/RD-Forms/pnd3/pnd3_270360.pdf` (main, AcroForm 2p / 54 widgets p1) **+ `pnd3_attach.pdf`**
  (ใบแนบ — the per-payee rows). Embed BOTH as `pnd3_main.pdf` + `pnd3_attach.pdf`.
- Data: `WhtFilingService.GeneratePnd3Async(period, "preview")` → `WhtFiling { rows[{certNo, payeeName,
  payeeTaxId, incomeTypeCode, incomeAmount, whtRate, whtAmount}], totals{income,wht}, filingDueDate }`.
- The filler fills the **main page** header + totals AND lays out each `rows[]` entry onto the **ใบแนบ** page
  (repeat the attach template per N rows, or a fixed rows-per-page grid — decode the attach grid in C1).

- [ ] **C1:** Decode `pnd3_270360.pdf` + `pnd3_attach.pdf` fields → `pnd3_fieldmap.md` (header fields on main;
  the repeating row cells on attach: ลำดับ · เลขผู้เสียภาษีผู้ถูกหัก · ชื่อ · ประเภทเงินได้ · วันที่จ่าย ·
  จำนวนเงิน · อัตรา · ภาษีหัก). Render blank. Commit fieldmap.
- [ ] **C2:** Embed both templates in csproj; build solution (kill :5080). 0 errors.
- [ ] **C3:** `Pdf/Pnd3FormFiller.cs` — `record Pnd3Model(/*payer header*/, IReadOnlyList<Pnd3Row> Rows,
  decimal TotalIncome, decimal TotalWht)` + `Pnd3Row(int Seq, string PayeeTaxId, string PayeeName, string
  IncomeType, DateOnly PayDate, decimal Income, decimal Rate, decimal Wht)`; `Fill` writes the main page +
  composes the attach page(s). Same `Template()`/`Render` helpers as Phase B.
- [ ] **C4:** `BuildPnd3PdfAsync(period, ct)` in `WhtFilingService` (reuse `GeneratePnd3Async`); endpoint
  `GET /tax-filings/pnd3/pdf?period=`. Build, restart.
- [ ] **C5:** Render-verify (compliance gate) on a period with ภ.ง.ด.3 certs (an INDIVIDUAL payee — seed one via
  a PV to an individual vendor if co2 has none in any period; check `GeneratePnd3Async` row counts first). Eyeball.
- [ ] **C6:** API smoke test (2×). Commit.
- [ ] **C7:** FE — add "ดาวน์โหลด PDF" to `components/tax-filings/WhtFilingClient.tsx` (covers pnd3 & pnd53),
  `openPdf(\`tax-filings/${form}/pdf?period=…\`)`, gated to `form==='pnd3'||form==='pnd53'`. i18n th/en. tsc 0. Commit.
- [ ] **C8:** Manual — renderer target + `07.08-pnd3-pdf.ts` embed + eyeball + gen-md + build. Commit.

## Phase D — ภ.ง.ด.53 (WHT, juristic) filler  *(near-clone of Phase C)*

**Delta from Phase C:** template `docs/RD-Forms/pnd53/pnd53_041060.pdf` (+`pnd53_attach.pdf`); data
`GeneratePnd53Async`; co2 already has ภ.ง.ด.53 certs (the rent + others). Same `Pnd3Model`/`Pnd3Row` shape is
reusable — make `Pnd53FormFiller` (or parametrise the WHT filler by template+fieldmap if the layouts match).

- [ ] **D1–D8:** Repeat C1–C8 against the pnd53 template/data. **Watch the e2e noise** (cont.98e/g): the live
  ภ.ง.ด.53 table on co2 carries e2e certs — for the manual sample, render a period or scope that reads clean,
  or caption qualitatively (do NOT ship a sample full of "ผู้ขาย e2e"/"XBU"). Manual `07.09-pnd53-pdf.ts`.

## Phase E — ภ.ง.ด.54 (foreign ม.70) filler  *(+ seed data)*

**Delta:** template `docs/RD-Forms/pnd54/pnd54_050369.pdf`; data `GeneratePnd54Async` (co2 = **0 rows every
month** → must seed). ภ.ง.ด.54 has no attach (single-page list).

- [ ] **E1: Seed ม.70 data.** Create a foreign payment that withholds under ม.70 (a PV to a foreign vendor with
  a WHT line whose income type maps to ภ.ง.ด.54). Verify `GeneratePnd54Async(<period>)` returns ≥1 row before
  building the sample. (Mirror how the 05.05 foreign vendors / ภ.พ.36 Amazon data exist.) Commit any seed SQL/data.
- [ ] **E2–E8:** Decode `pnd54_050369.pdf` → fieldmap → embed → `Pnd54FormFiller` → `BuildPnd54PdfAsync` →
  endpoint `GET /tax-filings/pnd54/pdf?period=` → render-verify → smoke test (2×) → FE button on
  `app/(dashboard)/tax-filings/pnd54/page.tsx` (it's a separate page, not WhtFilingClient) → manual
  `07.10-pnd54-pdf.ts`.

## Phase F — finalize

- [ ] **F1:** Update `docs/api/openapi.yaml` with the 4 new `/tax-filings/{pnd30,pnd3,pnd53,pnd54}/pdf` GETs
  (flag the delta for Sana). Commit.
- [ ] **F2:** Update `docs/manual/reference-modals-buttons.md` §3 (detail-page buttons) — add the new
  "ดาวน์โหลด PDF" buttons for ภ.พ.30 / ภ.ง.ด.3/53/54. gen-md + build. Commit.
- [ ] **F3:** Prepend a `progress.md` entry: forms shipped, the per-form fieldmaps, gates (solution build 0/0 ·
  new Api smoke tests 2× · FE tsc 0 · gen-md/mkdocs 0 · ম clean · every filled PDF eyeballed). Tick the spec items.

## Verification gates (every phase, before commit)

- Solution builds **0/0** (kill :5080 before full builds; restart after).
- New Api smoke test passes **2× consecutive** on `teas_test` (`TEAS_TEST_PG`).
- **Compliance eyeball:** the filled PDF matches the official RD form box-for-box; VAT shown separately;
  amounts tie to the on-screen data (ภ.พ.30 ↔ 07.01; WHT ↔ the 50ทวิ certs). A wrong box = a wrong filing.
- FE `tsc --noEmit` 0; i18n keys in BOTH th.json + en.json.
- Manual: walkthrough captured + **in-manual PNG eyeballed** (not just the staging render); `gen-markdown.mjs`
  + `mkdocs build` 0 errors; grep `ম` = 0; scratch dirs not staged.
- Commit per task; commit messages slash-free.

## Notes / risks

- **The decode + render-verify loop is the cost.** pnd51's source comments show iterative coordinate fixing
  ("off by one field", label-join). Budget real time per form; do NOT cold-delegate the compliance eyeball.
- **Data availability:** ภ.พ.30 + ภ.ง.ด.53 have co2 data now; ภ.ง.ด.3 needs an individual payee; ภ.ง.ด.54 needs
  the ม.70 seed (Phase E1). Check `Generate<F>Async` row counts before each manual sample.
- **No submission** — these are print-and-file PDFs; no RD-API call anywhere.
- **No EF migration** expected. If anything wants a schema change, STOP and ask Ham (§11).
- **Templates** copied into `Pdf/Templates/` are committed (embedded resources, like pnd1_main.pdf); the
  `docs/RD-Forms/` source dir stays uncommitted scratch.
