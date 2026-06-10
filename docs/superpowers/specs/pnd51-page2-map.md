# ภ.ง.ด.51 — Page 2 (Computation Worksheet) Field Map & Data-Gap Spec

> **Status:** Reference + design spec. Page 2 is **NOT wired** in `Pnd51FormFiller` yet.
> Page 1 (header/period/address/amount/filing-date/ยื่นปกติ) ships and is geometry-aligned.
> This document maps every page-2 AcroForm field to its worksheet line + RD box code + the
> `CitComputation` value it needs, and flags the **data gap** that blocks a full page-2 fill.
>
> Template: `docs/RD-Forms/pnd51/pnd51_020768.pdf` (จัดทำ มิ.ย. 2568). Page index 1 (0-based).
> Geometry already extracted into the embedded `pnd51_cells.json` (24 page-2 box fields, key `page=1`).

---

## 1. Worksheet structure

Page 2 is the **การคำนวณภาษี** worksheet, two sections:

- **รายการที่ 1 — การคำนวณฐานภาษี** (compute the tax base). Three mutually-exclusive methods,
  the filer uses exactly one:
  - **1. ม.67ทวิ(1)** — กึ่งหนึ่งของ *ประมาณการ* กำไรสุทธิ (half of the **estimated** full-year net
    profit). **This is Method A — the method our engine implements.** Boxes 51–60.
  - **2. ม.67ทวิ(2)** — กำไรสุทธิจริงของ *รอบหกเดือนแรก* (actual H1 net profit; banks, finance,
    insurance, and filers who elect it). Boxes 61–66.
  - **3.** ยอดรายรับก่อนหักรายจ่าย (gross-receipts basis, by approval). Box 66.1.
- **รายการที่ 2 — เงินได้ที่ต้องเสียภาษี และการคำนวณภาษี** (taxable income → tax → net payable).
  Boxes 28–41. Pulls the chosen base from รายการที่ 1, applies the rate, subtracts credits,
  adds surcharge, yields net payable (which is what page-1 row-75 amount shows).

The small printed numbers (51, 52, 53-54, …) are the RD's **official box codes** for e-filing.

---

## 2. Field map (AcroForm name → worksheet line → RD box → data source)

All page-2 amount fields are 14-cell combs (non-uniform; centres in `pnd51_cells.json`).
`รายการ1` rows are right-aligned baht with the satang in the last 2 cells (same convention as page 1).

| Field      | Cells | Worksheet line (รายการที่ 1)                                                        | RD box | Source in `CitComputation` / gap |
|------------|-------|-------------------------------------------------------------------------------------|--------|----------------------------------|
| `Text4.2`  | 2     | รหัสสกุลเงิน (currency code; THB = 01)                                               | 67-68  | constant `01` (THB). |
| `Text4.3`  | 14    | **1.(1)** ประมาณการยอดรายรับ/ยอดขายก่อนหักรายจ่าย (full year)                        | 51     | `H1 Totals.Revenue × 2`. ✅ **default path only** — null when caller overrides `estimatedAnnualProfit`. |
| `Text4.4`  | 14    | **1.(2)** หัก ประมาณการรายจ่าย (full year)                                           | 52     | `H1 Totals.Expense × 2`. ✅ **default path only** (as 51). |
| `Text4.5`  | 14    | **1.(3)** คงเหลือ ประมาณการกำไรสุทธิ/ขาดทุนสุทธิ                                     | 53-54  | `= 51 − 52` (`H1 NetProfit × 2` = estimate). ✅ default path. |
| `Text4.6`  | 14    | **1.(4)** หัก ขาดทุนสุทธิยกมาไม่เกิน 5 ปี                                            | 55     | **GAP** — loss carryforward (not modelled; clean filing = 0). |
| `Text4.7`  | 14    | **1.(5)** หัก ประมาณการกำไรสุทธิที่ได้รับยกเว้นตามกฎหมาย                             | 56     | **GAP** — BOI/exempt profit (clean filing = 0). |
| `Text4.8`  | 14    | **1.(6)** ประมาณการกำไรสุทธิที่ต้องคำนวณภาษี/ขาดทุนสุทธิ                             | 57-58  | `= estimate` (`EstimatedAnnualProfit`, = 53-54 when 55=56=0). ✅ |
| `Text4.9`  | 14    | **1.(7)** กึ่งหนึ่งของประมาณการกำไรสุทธิที่ต้องเสียภาษี                              | 59-60  | `EstimatedAnnualProfit / 2`. ✅ derivable. |
| `Text4.10` | 14    | **2.(1)** กำไรสุทธิ/ขาดทุนสุทธิ ของรอบหกเดือนแรก (ม.67ทวิ(2))                        | 61-62  | n/a Method A (Method B only). |
| `Text4.11` | 14    | **2.(2)** หัก ขาดทุนสุทธิยกมาไม่เกิน 5 ปี                                            | 63     | n/a Method A. |
| `Text4.12` | 14    | **2.(3)** หัก กำไรสุทธิที่ได้รับยกเว้น                                               | 64     | n/a Method A. |
| `Text4.13` | 14    | **2.(4)** กำไรสุทธิที่ต้องเสียภาษี/ขาดทุนสุทธิ                                       | 65-66  | n/a Method A. |
| `Text4.14` | 14    | **3.** ยอดรายรับก่อนหักรายจ่ายของรอบหกเดือนแรก (gross-receipts method)               | 66.1   | n/a Method A. |

| Field      | Cells | Worksheet line (รายการที่ 2)                                                        | RD box | Source in `CitComputation` / gap |
|------------|-------|-------------------------------------------------------------------------------------|--------|----------------------------------|
| `Text4.15` | 14    | **1.** กึ่งหนึ่งของประมาณการกำไรสุทธิที่ต้องเสียภาษี (จาก รายการ1 1.(7))             | 28-29  | `EstimatedAnnualProfit / 2`. ✅ (Method A base.) |
| `Text4.16` | 14    | **2.** กำไรสุทธิที่ต้องเสียภาษี (จาก รายการ1 2.(4)) — Method B base                  | 30-31  | n/a Method A. |
| `Text4.17` | 14    | **3.** รายรับก่อนหักรายจ่าย (จาก รายการ1 3.) — gross-receipts base                   | 31.1   | n/a Method A. |
| `Text4.18` | 14    | **4.** ภาษีที่คำนวณได้ (rate × base from 1/2/3)                                       | 32     | `TaxOnProfit(estimate, schedule) × 0.5`. ✅ flat **and** SME brackets (`CitRateSchedule.Sme()` via `isSme`). |
| `Text4.19` | 14    | **5.(1)** หัก ภาษีเงินได้หัก ณ ที่จ่าย + ภาษีที่บุคคลอื่นเสียแทน                      | 33     | `WhtH1`. ✅ |
| `Text4.20` | 14    | **5.(2)** หัก ภาษีในส่วนที่ลดหย่อนอัตราไม่เกินร้อยละ 50 ของอัตราปกติ                 | 34     | **GAP** — rate-reduction credit (BOI etc.). |
| `Text4.21` | 14    | **5.(3)** หัก ภาษีที่ชำระแล้วตามแบบ ภ.ง.ด.51 (กรณียื่นเพิ่มเติม) — left column        | (35)†  | **GAP** — only for an amended filing (ยื่นเพิ่มเติม). 0 for normal. |
| `Text4.22` | 14    | **5. รวม** (รวมรายการ 5.(1)–(3)) — right column                                       | 35     | Σ of `Text4.19`+`Text4.20`+`Text4.21` (= WhtH1 when (2)=(3)=0). |
| `Text4.23` | 14    | **6.** คงเหลือ ภาษีที่ชำระเพิ่มเติม/ชำระไว้เกิน                                       | 36-37  | `HalfYearPrepayment` (= 32 − รวม). ✅ |
| `Text4.24` | 14    | **7.** บวก เงินเพิ่ม (ถ้ามี) ม.27 / ม.67ตรี                                          | 38-38.1| **GAP** — surcharge (late/under-estimate). 0 if on-time & accurate. |
| `Text4.25` | 14    | **8.** รวม ภาษีที่ชำระเพิ่มเติม/ชำระไว้เกิน (= 6 + 7)                                 | 39-40  | `HalfYearPrepayment` (+ surcharge). ✅ when 7 = 0. |

† Box code **35** is printed once, beside the **รวม** row (`Text4.22`). The 5.(3) prior-paid
sub-line (`Text4.21`, left column) shares the row and has no separate printed code — confirm
against the e-filing schema before relying on a per-sub-line code. (The dump can't disambiguate;
this is the one box-code uncertainty in the map.)

Radios on page 2 (the method/rate selectors `Radio Button15`/`Button14`-family, currency,
SME-rate, exempt) were confirmed to be the page-2 widgets the page-1 code does **not** touch.
They must be mapped here when page 2 is wired — left as TODO (need the diag dump's page-2 radio
group → export-value list before selecting any).

---

## 3. Data-gap analysis

`Pnd51FilingService.BuildPnd51Async(year, estimatedAnnualProfit?, whtSufferedH1, isSme, ct)`:

```
estimate  = estimatedAnnualProfit ?? max(0, H1 Totals.NetProfit) × 2     // two input paths
schedule  = isSme ? CitRateSchedule.Sme() : CitRateSchedule.General()
taxAmount = CitCalculator.HalfYearPrepayment(estimate, whtSufferedH1, schedule)   // = TaxOnProfit(estimate,schedule)×0.5 − whtH1
```

`IFinancialReportService.ProfitLossAsync` returns `ProfitLossReport` whose `Totals` is a
`ProfitLossGroup(…, Revenue, Expense, NetProfit)` — a **flat** Revenue/Expense/NetProfit triple
(no GP/COGS, no per-account rows; "R-Q1a"). That flat triple is exactly the shape rows 51/52/53-54
need, so the engine **can** fill them — *but only in the default (H1×2) path*. Two input paths:

- **Default path** (`estimatedAnnualProfit == null`): engine has H1 `Revenue`,`Expense`,`NetProfit`
  → ✅ 51 (=Rev×2), 52 (=Exp×2), 53-54 (=NetProfit×2 = estimate).
- **Override path** (caller supplies `estimatedAnnualProfit`): only the single taxable-net figure
  exists → 51/52 are **null**; the worksheet must start at 53-54/57-58.

Either path, the engine can stand behind:

- ✅ box 57-58 (taxable estimated profit = estimate) — *iff* no carryforward / no exemption
- ✅ box 59-60 / 28-29 (half of it)
- ✅ box 32 (tax computed) — flat **and** SME, via `schedule`
- ✅ box 33 (WHT = `whtSufferedH1`)
- ✅ box 36-37 / 39-40 (net payable) — *iff* no rate-reduction / no amended-prior / no surcharge

Genuine gaps — **not modelled**, legitimately 0 for a clean, on-time, first (not amended) filing,
but the form has no way to *assert* they are 0, so emitting them blind violates §11:

- ❌ loss carryforward (55), exempt profit (56) — would make 57-58 ≠ 53-54
- ❌ rate-reduction credit (34) — BOI etc.
- ❌ prior-ภ.ง.ด.51-paid (35) — only on an amended (ยื่นเพิ่มเติม) filing
- ❌ ม.27 / ม.67ตรี surcharge (38) — late/under-estimate penalty

So the gap is **narrower** than "no breakdown": the simple Method-A worksheet is fully derivable in
the default path; what's missing is the adjustment inputs (55/56/34/35/38) — and a guarantee they're 0.

---

## 4. Decision — do not auto-fill page 2 yet

Filling 51–56 with blanks while 57-60 carry numbers would render a worksheet that **does not
foot** (รายรับ − รายจ่าย ≠ กำไร) — an audit red flag, and §11 forbids improvising tax-form
placement. Two clean paths, **both require Ham's sign-off** (§11):

1. **Minimal honest fill (Method A, simple case).** Fill the boxes the engine can stand behind —
   `Text4.8` (57-58), `Text4.9`/`Text4.15` (59-60/28-29), `Text4.18` (32), `Text4.19` (33),
   `Text4.22` (รวม), `Text4.23` (36-37), `Text4.25` (39-40), **plus `Text4.3`/`4.4`/`4.5` (51/52/53-54)
   in the default (H1×2) path** — and leave `Text4.6`/`4.7` (55/56), `Text4.20` (34), `Text4.21`
   (prior-paid), `Text4.24` (38) blank *only behind a guard* that throws when the company actually
   has carryforward / exemption / rate-reduction / is amended / owes surcharge, so we never emit a
   non-footing or wrong worksheet. Lowest risk; ships page 2 for the common first-filing case (flat
   or SME rate both OK — `schedule` already handles the bracket).
2. **Full Phase C-C.** Build the estimated-P&L model (revenue, expenses, adjustments, carryforward,
   exemptions) + SME bracket schedule + `Company.PaidUpCapital`, then fill 51–66 + รายการ2 fully and
   support Method B / gross-receipts. Correct and complete; larger build.

**Recommendation:** ship **(1)** behind the guard once Ham confirms the box-32 rate assumption
(flat 20% vs SME), then grow into **(2)**. Until then page 2 stays unwired and the RD AcroForm
renders page 2 blank (legal — many filers attach a separately-computed worksheet).

---

## 5. Mechanical prerequisite (no compliance risk) — DEFERRED until §4 path is chosen

`RdAcroFormFiller.Render` currently overlays **page 0 only** (`Composite` writes `doc.Pages[0]`;
`ReadFieldRects` reads `Pages[0]` size). pnd51's 2-page template already emits a **blank** page 2,
so nothing is broken — page 2 just isn't filled. Two ways to fill it; **Ham picks one** (this is the
open design question that, with §4, blocks page-2):

- **(a) Make `Render` page-aware.** Capture each widget's page (from `/P` or per-page `/Annots`),
  build one overlay per page, composite each onto its page. General, but **refactors shared infra
  every RD form depends on** (50ทวิ, ภ.ง.ด.1/1ก) → regression surface. `pnd51_cells.json` already
  carries a `page` key, so the data is ready.
- **(b) Split + `Merge` (matches the shipped pattern).** ภ.ง.ด.1/1ก already do multi-page output by
  rendering **separate single-page templates** and calling `Merge(pages)`. Apply the same here:
  extract page 2 of `pnd51_main.pdf` into its own one-page template, `Render` it, `Merge`. **Zero
  change to `Render`**; the cost is one-time PDF surgery to split the page-2 AcroForm into a
  standalone file (verify the page-2 fields + geometry survive the split).

**Recommendation:** (b) — it leaves the proven, Ham-reviewed `Render` path untouched and reuses the
existing `Merge`. Deferred entirely until Ham confirms a §4 path; building either now is premature
(page-2 fill is gated on his sign-off regardless). (Tracked in progress.md cont.84.)
