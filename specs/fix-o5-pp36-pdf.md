# O5 — ภ.พ.36 PDF export (parity with ภ.ง.ด.54)

Ham's O5 ruling (docs/archive/DECISIONS-army-2026-07-25.md): ภ.พ.36 gets a printable PDF,
matching the ภ.ง.ด.3/53/54 pattern. Presentation only — does NOT touch
`WhtFilingService.GeneratePnd36Async`'s computation (payer/vendor amounts, VAT 7%, the
unreconciled-AP guard, the FIX-3b ack-figure guard). Route naming: `pp36` (not `pnd36`,
which stays the existing on-screen POST route/hooks/types) — confirmed intentional via
`docs/phase6-testing/co3-nonvat/test-cases.md:78` and `driver.py` which already probe
`/tax-filings/pp36/pdf` expecting 404 pre-fix.

## Template verdict: NOT BLOCKED

Official RD template found at `docs/RD-Forms/pp36/pp36_010968.pdf` (same "010968" release
as pp01/pp09/pp30 — all fillable AcroForms). `_meta.md`'s "Binary not downloaded" note is
STALE; the file is present (734,135 bytes) and has a real `/AcroForm` with named `/Tx`/`/Btn`
fields (confirmed via a field-roster dump, not grep-guessing). Rendered page 1 to PNG (Edge
via Playwright — no poppler on this box) and visually confirmed the layout/labels below.

## Verified field map (measured, not pattern-matched from pp01/09 — this form's numbering
does NOT follow visual order, same trap those forms' docs warn about)

**Payer identity header** (from `CompanyProfile`, same source as `Pnd54Model`):
| Field | Comb | Meaning |
|---|---|---|
| Text1.0 | comb17 | เลขประจำตัวผู้เสียภาษีอากร (payer TaxId) |
| Text1.1 | comb5 | สาขาที่ (BranchCode) |
| Text1.2 | — | ชื่อผู้นำส่งภาษีมูลค่าเพิ่ม (PayerName) |
| Text1.3 / 1.4 / 1.5 | — | อาคาร / ห้องเลขที่ / ชั้นที่ (Building/RoomNo/Floor) |
| Text1.6 / 1.7 / 1.8 / 1.9 | — | หมู่บ้าน / เลขที่ / หมู่ที่ / ตรอกซอย (Village/HouseNo/Moo/Soi) |
| Text1.10 / 1.11 / 1.111 | — | ถนน / ตำบลแขวง / อำเภอเขต (Road/SubDistrict/District) — **1.11 and 1.111 are appended out of numeric order in the AcroForm /Fields array; do not assume 1.11 precedes 1.12 visually** |
| Text1.12 | — | จังหวัด (Province) |
| Text1.14 | comb5 | รหัสไปรษณีย์ (PostalCode) — sits on ITS OWN row, not beside จังหวัด |
| Text1.13, Text1.15 | — | phone-adjacent fields; SKIPPED (no phone in our data model — matches pnd54/pnd3/53, none of which fill phone either) |

**Payee (foreign vendor) identity** (from `Pnd36Row`):
| Field | Meaning |
|---|---|
| Text1.18 | ชื่อผู้ประกอบการซึ่งเป็นผู้รับเงิน (row.VendorName) |
| Text1.23 | ประเทศ (row.CountryCode) — Text1.22 (เมือง) left blank, no city data |

**Payment date** (from `row.DocDate`, optional cheap win — WHT fillers already print BE dates):
| Field | Comb | Meaning |
|---|---|---|
| Text1.26 | maxLen2 | วันที่ (day) |
| Text1.27 | — | เดือน (Thai month name, local `ThaiMonths` array per Wht50TawiFormFiller precedent) |
| Text1.28 | maxLen4 | พ.ศ. (BE year = CE+543) |

**Radios** (selected by **on-state**, not WidgetIndex — Radio Button1's two widgets differ
by <1pt in Y, the exact same tie-break hazard Pnd54FormFiller's own doc comment warns
about; on-states dumped via a temp probe, not assumed):
| Group | OnState | Meaning |
|---|---|---|
| Radio Button1 | `"0"` | ยื่นปกติ (normal filing — we never emit an amended filing) |
| Radio Button2 | `"1"` | Top-right box, case (1): "จ่ายเงินค่าซื้อสินค้าหรือบริการ...หรือให้แก่ผู้ประกอบการที่ได้ให้บริการในต่างประเทศ" — the ม.83/6(2) import-of-service reverse-charge case, which every `Pnd36Row` is BY CONSTRUCTION (`RequiresPnd36ReverseCharge` foreign-vendor PVs only) |
| Radio Button3 | `"0"` | Payee-status box, case (2): "เป็นผู้ประกอบการที่ได้ให้บริการในต่างประเทศ และได้มีการใช้บริการนั้นในราชอาณาจักร" — same reasoning |

⚠ CAUGHT IN REVIEW: an earlier draft of this map had Button2/Button3 SWAPPED (assigned by
page-region eyeball, not X-coordinate). Button2's widgets sit at X≈343–356 (right column —
the top-right "(1)(2)(3)" box); Button3's sit at X≈231–243 (mid-page, below the payee
identity rows). Verified against the rendered image before locking.

**Calculation section** (`Text2.*`, all on physical page 1 — page 2 is instructions only,
zero AcroForm fields):
| Field | Comb | Meaning |
|---|---|---|
| Text2.1 | comb13, Right | 1. จำนวนเงินที่จ่าย = `row.ServiceAmountThb` |
| Text2.2 | comb13, Right | 2. จำนวนเงินภาษีมูลค่าเพิ่มที่ต้องนำส่ง = `row.VatAmount` |
| Text2.3 | — | "(ตัวอักษร)" spelled-out amount for row 2 — SKIPPED (no Thai number-to-words anywhere in this repo's filler family) |
| Text2.4 | comb13, Right | 3. เงินเพิ่ม (late-filing surcharge) — SKIPPED/blank (blank asserts zero — we only ever file on time; same convention as ภ.ง.ด.54's รวมทั้งสิ้น) |
| Text2.5 | comb13, Right | 4. เบี้ยปรับ (penalty, case-(2)-only) — SKIPPED/blank |
| Text2.6 | comb13, Right | 5. รวม (2.+3.+4.) = `row.VatAmount` (== row 2, since 3/4 are blank) |
| Text2.7 | — | "(ตัวอักษร)" for row 5 total — SKIPPED |
| Text2.8, Text2.9 | — | signature name / filing date — SKIPPED (never auto-sign, matches every other filler) |

**Money invariant** (per sheet): printed(1.) = `row.ServiceAmountThb`; printed(2.) =
printed(5.) = `row.VatAmount`; rows 3/4 blank. Σ VAT across sheets ties to
`filing.TotalVat`. Guard test asserts this, not just "PDF is non-empty."

**Sheet strategy**: one sheet per `Pnd36Row` (page-2 instructions themselves say "แยกเปน
แต่ละรายผู้รับ และหรือแยกเปนแต่ละรายประเภทการจ่ายเงิน" — separate by payee), merged via
`WhtFormFiller.Merge`, exactly like `Pnd54FormFiller`/`BuildPnd54PdfAsync`. Zero rows →
single header-only sheet (payer identity + ยื่นปกติ + case radios, no payee/calc fields) —
same fallback pnd54 uses.

**Untouched sections** (left entirely blank — no data source, no attempt to pattern-match):
Text1.31–1.72 (ผู้โอนสินทรัพย์/ขายทอดตลาด sections 2 and 3 — auction/asset-transfer cases,
never apply to a foreign-service PV), Radio Button4–9 (ม.40(2)/(3)/(4)/(5)/(6)/(8) income-type
classification — not in our data model), Text1.48–50/1.68–70 (section-2/3 date triples).

## Blast cap: 8 declared, 9 actual (pre-declared one-file overage)

Pre-declared per the dispatch's own advice: cap counted 8 assuming no `pp36_cells.json`;
the taxID comb (Text1.0, 1-4-5-2-1 grouped print, same as every sibling form) needs a
cell-centre override exactly like `pnd54_cells.json`/`pp01_cells.json` — equal-division on
a 17-cell comb cannot land on a non-uniform dash-grouped grid. Money combs (Text2.x) turned
out NOT to need an override (verified via render — equal-division landed cleanly on the
straight 13-digit boxes, no dash grouping there). Files (9):

1. `backend/src/Accounting.Infrastructure/Pdf/Templates/pp36_main.pdf` (asset, copied)
2. `backend/src/Accounting.Infrastructure/Pdf/Templates/pp36_cells.json` (asset, Text1.0 only)
3. `backend/src/Accounting.Infrastructure/Accounting.Infrastructure.csproj` (embed both)
4. `backend/src/Accounting.Infrastructure/Pdf/Pp36FormFiller.cs` (new filler)
5. `backend/src/Accounting.Application/TaxFilings/TaxFilingDtos.cs` (interface: `BuildPp36PdfAsync`)
6. `backend/src/Accounting.Infrastructure/TaxFilings/WhtFilingService.cs` (impl + PayerTaxIdRules guard)
7. `backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs` (route `GET /tax-filings/pp36/pdf`)
8. `frontend/app/(dashboard)/tax-filings/pnd36/page.tsx` (download-PDF button, pnd54 idiom)
9. `backend/tests/Accounting.Api.Tests/TaxFilings/WhtFormPdfFillTests.cs` (extended, not new file)

Not touched: `docs/rbac/endpoint-permission-map.generated.md` (auto-regenerated by
`RbacAuthMapTests` as a test-run byproduct — never hand-edited); `docs/api/openapi.yaml`
(no contract test enforces it, skipped — same tier as the other WHT PDF routes which also
lack an openapi entry... verify before skipping); `_meta.md`'s stale download-status note
(not in scope).

## Payer-tax-ID guard (U1/U10 convention)

`GeneratePnd36Async` (the on-screen/finalize computation path) does **not** carry
`PayerTaxIdRules.EnsureUsable` — confirmed by grep, only `BuildPnd54PdfAsync` and
`BuildWhtPdfAsync` (pnd3/53) call it. `BuildPp36PdfAsync` is a NEW unguarded path by
construction, so it gets its own `PayerTaxIdRules.EnsureUsable(payerTaxId)` call, resolved
ONCE before the row loop (not per-sheet — same N3 Tier-2 lesson as `BuildPnd54PdfAsync`).
Noted per the dispatch's ask; `GeneratePnd36Async` itself is out of scope (presentation-only).

## Checklist

- [x] Template found, not blocked (evidence above)
- [x] Field map decoded + render-verified (screenshot viewed, not pdftotext-inferred)
- [x] `Pp36FormFiller.cs` written (model + Fill, mirrors `Pnd54FormFiller` shape). Render-verify
      caught and fixed one real bug: Text1.10/1.11/1.111/1.12 were each one slot ahead of what
      the numeric suffix implied (Text1.10 prints under "แยก" not "ถนน"); corrected + re-verified
      via a second screenshot before locking. All fields confirmed via cropped screenshots:
      taxID 1-4-5-2-1 grouping, full address chain, payee name/state, payment date (BE),
      all three radio ticks (ยื่นปกติ / case(1) / payee-status case(2)), and the calc section
      money invariant (row1=service, row2=row5=vat, rows 3/4 blank).
- [x] `pp36_main.pdf` + `pp36_cells.json` embedded in csproj (cells.json holds only Text1.0 —
      the taxID comb; Text2.x money combs render correctly with the generic equal-division
      default, confirmed by the same render-verify pass — no override needed there)
- [x] `IWhtFilingService.BuildPp36PdfAsync` + `WhtFilingService` impl + guard
- [x] `GET /tax-filings/pp36/pdf` route, same permission as `/tax-filings/pnd36` (preview)
- [x] FE download button on the pnd36 page, pnd54's `openPdf` idiom (tsc clean)
- [x] Tests: money-invariant test (service-layer assertion, matching the ACTUAL shape of
      `WhtFormPdfFillTests.cs`'s existing pnd54/pnd3 tests — none of them text-extract a
      flattened comb render either; box placement is render-verified above, not re-asserted via
      fragile Thai-glyph extraction), one-sheet-per-row test, guard-refusal test — 3 new,
      8/8 green in the extended file
- [x] Render-verify the PRODUCTION path output (not just the marker probe) — done via
      `Fill_every_box_pp36`-style temp diagnostic, screenshotted, reverted after use
- [x] Targeted `dotnet test` green (WhtFormPdfFillTests 8/8; broader TaxFilings/Rbac/Sprint9/
      Sprint87 sweep — see report)
- [x] Diagnostic scratch (`Dump_pp36_*`/`Fill_every_box_pp36`/`Dump_pp01_taxid_rect` methods in
      `TaxFormFillDiagnostic.cs`) reverted — `git diff` on that file is empty; decode knowledge
      lives in this spec + the filler's own doc comment
