# ภ.ง.ด.3 / ภ.ง.ด.53 (WHT remittance) — AcroForm field map

Templates: `docs/RD-Forms/pnd3/pnd3_270360.pdf` (+`pnd3_attach.pdf`) → `pnd3_main.pdf` / `pnd3_attach.pdf`;
`docs/RD-Forms/pnd53/pnd53_041060.pdf` (+`pnd53_attach.pdf`) → `pnd53_main.pdf` / `pnd53_attach.pdf`.
Filled by `WhtFormFiller` (main page + ใบแนบ, merged). Decoded with PyMuPDF.

## Main page (shared field names — pnd3 595×842 p1 / pnd53 612×859 p1; page 2 = instructions)
| Field | meaning | source |
|---|---|---|
| `Text1.0`  | เลขประจำตัวผู้เสียภาษี (payer, comb 13) | TaxId |
| `Text1.1`  | สาขาที่ (comb 5)                        | BranchCode |
| `Text1.2`  | ชื่อผู้มีหน้าที่หักภาษี ณ ที่จ่าย        | LegalName |
| `Text1.3`..`Text1.15` | อาคาร·ห้อง·ชั้น·หมู่บ้าน·เลขที่·หมู่·ซอย·แยก·ถนน·ตำบล·อำเภอ·จังหวัด·ไปรษณีย์ | CompanyProfile address |
| **YEAR** `Text1.18` (pnd3) / `Text1.17` (pnd53) | พ.ศ. (ml=4) | periodYearCe + 543 |
| `Text2.1`  | รวมยอดเงินได้ทั้งสิ้น                    | Totals.Income |
| `Text2.2`  | รวมยอดภาษีที่นำส่งทั้งสิ้น               | Totals.Wht |
| `Text2.3`  | เงินเพิ่ม (blank)                       | — |
| `Text2.4`  | รวม (2 + 3) = total WHT                 | Totals.Wht |

### Main radios
- **pnd3**: `Radio Button0`#0 = ยื่นปกติ · `Radio Button2`#0 = (1) ม.3 เตรส (legal basis).
- **pnd53**: `Radio Button0`#0 = (1) ม.3 เตรส (legal basis) · `Radio Button2`#0 = ยื่นปกติ.
- **Tax month** = `Radio Button10` (12 boxes). The widget array order ≠ visual order, so select by the
  AcroForm **export value (on-state)**, NOT positional index. month→on-state (decoded from /AP/N):
  - pnd3:  `["0","4","8","1","5","9","2","6","11","3","7","10"]`
  - pnd53: `["2","4","8","1","5","9","0","6","10","3","7","11"]`
  - (pp30's `Radio Button3` happens to have widget-order == on-state == column-major index, so it stays positional.)

## ใบแนบ (attachment) — landscape, 6 payee rows/page, multiple pages merged
Each payee row K (1-based on the page) — **schemes differ per form**:
| col | pnd53 | pnd3 |
|---|---|---|
| ลำดับ (seq)        | `Text{K}.4`  | `Text{K}.27` |
| payee taxId        | `Text{K}.5`  | `Text{K}.1`  |
| ชื่อ payee         | `Text{K}.6`  | `Text{K}.3`  |
| วันที่จ่าย (blank — DTO has no per-row date) | `Text{K}.10` | `Text{K}.9` |
| ประเภทเงินได้      | `Text{K}.11` | `Text{K}.10` |
| อัตราร้อยละ        | `Text{K}.12` | `Text{K}.11` |
| จำนวนเงินที่จ่าย   | `Text{K}.13` | `Text{K}.12` |
| ภาษีที่หักนำส่ง    | `Text{K}.14` | `Text{K}.13` |
| เงื่อนไข (=1)      | `Text{K}.15` | `Text{K}.14` |

Page-header on each ใบแนบ: `Text1.0` payer taxId, `Text1.1` branch.

## Comb cell-centres (`pnd53_cells.json`, `pnd3_cells.json`)
The taxId (`Text1.0`) and postal (`Text1.15`) combs are NON-uniform: the taxId is a 1-4-5-2-1 grid
(13 digits + 4 dash gaps), so equal division drifts. Cell-centres are extracted from the template's
printed dividers (`_scratch/extract_cells.py` greedy walk: a wide segment = a digit cell; two narrow
segments summing to ~one cell = a digit split by a spurious divider; a lone narrow segment = a dash)
and passed to `RdAcroFormFiller.Render(..., cellCenters)`. The ใบแนบ payee-taxId comb is left on
equal division (its grid has ambiguous narrow cells; co2 payees are foreign → no Thai taxId anyway).

## Income type + condition (from the cert, not hard-coded)
- ① ประเภทเงินได้ = `WhtCertificate.IncomeDescription` (e.g. "ค่าบริการ"); falls back to `ม.40(<code>)`.
  (NOT the bare numeric income-type code.)
- ② เงื่อนไข = `WhtCertificate.WhtCondition` — **1 = หัก ณ ที่จ่าย, 2 = ออกภาษีให้** (form's หมายเหตุ ②).

## Verification (pnd53 202606, 13 rows / co2)
Render-verified vs official: taxId/postal combs land per printed cell (1-4-5-2-1); main totals tie
(รวมเงินได้ 78,309.28 / รวมภาษี 4,164.28); month=มิถุนายน; ม.3เตรส + ยื่นปกติ ticked; attach rows show
the income description + foot (amount × rate% = WHT). **Caveats (v1):** pay-date column blank (DTO gap);
the "ใบแนบ จำนวน ราย/แผ่น" count boxes left blank; co2 ภ.ง.ด.53 carries e2e-suite noise in payee names
(data, not the filler) — filter for the manual sample.
