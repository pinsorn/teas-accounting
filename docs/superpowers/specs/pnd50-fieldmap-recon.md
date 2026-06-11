# ภ.ง.ด.50 (`pnd50_050369.pdf`) — field-map recon (2026-06-10, cont.87b)

> **Superseded scope note (cont.89):** v1 (p1+p2) shipped; **v2 (p1+p2+p3 ladder+p6 งบฐานะ) is now
> the only behaviour** — see plan `docs/superpowers/plans/2026-06-11-pnd50-v2-dashboard.md` and the
> p3/p6 render-confirmed radio map in `docs/RD-Forms/pnd50/pnd50_radiomap.md`. This file remains the
> page-structure reference; the v1-scope passages below are historical.
>
> Grounding for the C-C FORM FILL plan. Probed with pymupdf (label-join, same method as ภ.ง.ด.51).
> Raw per-page dumps: `docs/RD-Forms/pnd50/fieldmap/_pnd50_fields_p{1..7}.txt`
> (field name | type [+radio choices] | rect x0,y0,x1,y1 | printed text on the same row).
> ⚠️ Label-join is a HINT (±6pt row tolerance) — every field/radio used by the filler MUST be
> render-confirmed (0-fill diagnostic + raster + Read) before it ships. Radio-map lesson applies.

## Structure (7 pages, 478 widgets)

| Page | Content | Widgets |
|---|---|---|
| 1 | Header (taxid/name/address/web/email) + FY period + filing-status radio + company-type radios (Group00-05) + ม.71ทวิ related-party radio (Group06/07) + คำรับรอง + signatures + auditor block | 76 (63T/11R) |
| 2 | สกุลเงิน (Group4) + **รายการที่ 1 การคำนวณภาษี** ← core of v1 | 44 (24T/19R) |
| 3 | BOI/exempt split note + รายการที่ 2 (กำไรสุทธิเพื่อเสียภาษี — ม.65ทวิ/ตรี adjustments ladder) + รายการที่ 3 (รายได้) | 95 (88T/6R) |
| 4 | รายการที่ 4 ต้นทุนผลิต/บริการ (+5/6?) | 88 (87T) |
| 5 | รายการที่ 7 รายจ่ายในการขายและบริหาร | 94 (93T) |
| 6 | รายการที่ 9 งบแสดงฐานะการเงิน (สินทรัพย์/หนี้สิน/ส่วนผู้ถือหุ้น) ← `BalanceSheetAsync` feeds this | 42 (33T/8R) |
| 7 | แบบแจ้งข้อความของกรรมการ/ผู้เป็นหุ้นส่วน/ผู้จัดการ | 39 (28T/10R) |

Field names are MESSY: bare numerics (`1`, `19`, `665`), dotted (`23.1`, `54.1`, `166.1`),
prefixed (`Text661`, `Text2000-1`, `44-2`). No usable naming convention — rect+label is the map.

## Page 2 — รายการที่ 1 การคำนวณภาษี (v1 target, draft map)

Boxes (printed box numbers from form margin):
- `Text661` (462,348) — 1.(3) รายรับก่อนหักรายจ่าย [boxes 48-49]
- `662` (462,458) — 2. ภาษีที่คำนวณได้ [50-51]
- `663` (327,480) — 3.หัก (1) ภาษียกเว้น พรฎ.18/463 [52]
- `664` (327,502) — (2) ภาษียกเว้น พรฎ.300 [53]
- `665` (328,524) — **(3) ภาษีหัก ณ ที่จ่าย** [54] ← `WhtReceivableReport` / store
- `666` (327,546) — **(4) ภาษีที่ชำระแล้วตาม ภ.ง.ด.51** [55] ← `cit_year_summaries.pnd51_prepaid`
- `667` (327,567) — (5) ภาษีส่วนลดหย่อนอัตรา ≤50% [56]
- `668` (327,590) — (6) ภาษีตาม ภ.ง.ด.50 เดิม (ยื่นเพิ่มเติม) [57]
- `669` (461,590) — รวมหัก (1)-(6)
- `670` (461,613) — **4. คงเหลือภาษีที่ ชำระเพิ่มเติม/ชำระไว้เกิน** [58-59]
- `671` (461,634) — 5. บวกเงินเพิ่ม (ถ้ามี) [60] ← ม.67ตรี/ม.27 surface here
- `672` (461,656) — 6. รวมภาษีที่ชำระเพิ่มเติม/ไว้เกิน [61-62]
- `400-403` — อัตราแลกเปลี่ยน date+rate (non-THB only) · `404-409` — (1)-(3) ×FX [176-178] (non-THB only)
- `53`, `54.1` — สกุลเงินอื่น ระบุ + รหัสสกุลเงิน (non-THB only)

Radios (choices verified from widget states):
- `Group4`: สกุลเงิน — Choice1 บาท / Choice2 อื่นๆ
- `Group5`: ฐานภาษี — Choice1 กำไรสุทธิ / Choice2 ขาดทุนสุทธิ / Choice3 รายรับก่อนหักรายจ่าย
- `Group21`: อัตรา — Choice1 กรณีทั่วไป / Choice2 กรณีลดอัตรา / Choice3 เสียจากยอดรายรับ
- `Group6` (sub of ลดอัตรา): Choice1 SMEs / Choice2 ร้อยละ15 / Choice3 ร้อยละ10 / Choice4 ร้อยละ8?
  / Choice5 ร้อยละ5? / Choice6 ร้อยละ3? / Choice17 อื่นๆ — **x-order vs choice-order NOT confirmed;
  must render-confirm every one before use**
- `Group7`: คงเหลือภาษี — Choice1 ชำระเพิ่มเติม / Choice2 ชำระไว้เกิน (same-row pair — pnd51 lesson)
- `Group8`: รวมภาษี — Choice1 ชำระเพิ่มเติม / Choice2 ชำระไว้เกิน

### ✅ RESOLVED by 0-fill raster (cont.87b, `fieldmap/zfill.py` — marker #N = dump line N)
**`Text661` is the ONE shared base-amount box [48-49]** sitting between the Group5 radio rows —
it serves whichever base is ticked (กำไรสุทธิ / ขาดทุนสุทธิ / รายรับก่อนหักรายจ่าย). There is no
separate กำไรสุทธิ box; the margin numbering on this form revision is 48-49 only.
Raster-confirmed margin numbers: #18=`Text661`[48-49] · #30=`662`[50-51] · #31-36=`663-668`[52-57]
· #37=`669`(รวม) · #38=`670`[58-59] · #41=`671`[60] · #42=`672`[61-62]. All are 14-cell comb grids
(baht|satang) like pnd51 → geometry extraction required.

## Page 1 — header (v1 target, draft hints)

- `1` (148,86→331) wide box = เลขประจำตัวผู้เสียภาษี 13-digit grid → needs printed cell-centre
  extraction (generalise `_pnd51_geo.py` → `pnd50_cells.json` embedded resource, same mechanism
  `RdAcroFormFiller` already supports via `cellCenters`)
- `17/18/19` + `20/21/22` = รอบบัญชี ตั้งแต่ วันที่/เดือน/พ.ศ. + ถึง วันที่/เดือน/พ.ศ.
- `2` = ชื่อนิติบุคคล · `3-15` + `Text10.1` = structured address (อาคาร/ห้อง/ชั้น/หมู่บ้าน/เลขที่/
  หมู่/ตรอกซอย/ถนน/ตำบล/อำเภอ/จังหวัด/รหัสไปรษณีย์/โทรศัพท์) — same CompanyProfile source as pnd51
- `166.1/166.2/166.3` = เว็บไซต์ / อีเมลผู้ประกอบการ / อีเมลผู้ทำบัญชี
- `Group1`: ยื่นปกติ Choice1 / ยื่นเพิ่มเติม Choice2 / Choice3 = ? (3 widgets at y158/179/199 — likely
  ยื่นปกติ/เพิ่มเติม/อื่น — **render-confirm**); `Text1` = เพิ่มเติมครั้งที่
- `Group00-05`: ประเภทนิติบุคคล (1) กฎหมายไทย … (6) อื่นๆ — six separate single-choice groups
  (Choice1/Off each), not one group — tick = set the right GROUP
- `Group06/07`: ม.71ทวิ related-party มี/ไม่มี (two separate groups)
- ✅ จำนวนเงิน boxes (raster-confirmed): **ภาษีที่ชำระเพิ่มเติม** baht=`Text2000-1` + satang=`Text3`
  [30-31] · **ภาษีที่ชำระไว้เกิน** baht=`Text2000` + satang=`Text3-2` (fill exactly ONE pair per the
  p2 box-58/59 sign — mirrors pnd51 ชำระเพิ่มเติม/ไว้เกิน semantics)
- `29-42` signatures/dates · `43-49` auditor name/number/report date · `44-2/50/52` bottom row

## v1 scope decision (proposed — matches locked decisions + C-C foundations)

Fill in v1: p1 header (taxid/name/address/period/ยื่นปกติ/company-type (1)/ม.71ทวิ radio per data)
+ p2 รายการที่ 1 (THB only: Group4=บาท; base= กำไรสุทธิ/ขาดทุน from store; Group21 กรณีทั่วไป or
SMEs via `ProfileAsync`; boxes 662/665/666/669/670/672 + radios 7/8 by sign).
DEFER: p3 รายการที่ 2/3 detail (adjustments ladder — data exists in `cit_adjustments`, mapping is
large), p4-5 cost/expense detail, p6 งบฐานะ (data ready via `BalanceSheetAsync`), p7.
⚠️ Same legal posture as pnd51 §4: a blank box asserts zero — v1 must REFUSE (422) any case it
cannot honestly render (e.g. รายการที่ 2 ladder omitted while adjustments ≠ 0 → require them filled
or refuse; exact guard rules to be decided in the plan).

## Next-session method (proven on pnd51 — reuse verbatim)

1. ☑ 0-fill diagnostic of p1+p2 done (`fieldmap/zfill.py`; box-role questions resolved above).
   ☑ **Radio map p1+p2 RENDER-CONFIRMED** (`fieldmap/radio_confirm.py`, all choices ticked+rastered)
   → `docs/RD-Forms/pnd50/pnd50_radiomap.md`. p1+p2 mapping is now COMPLETE for the v1 scope.
2. Extract printed cell-centres for box fields (generalised `_pnd51_geo.py`) → `pnd50_cells.json`.
3. Write `Pnd50FormFiller` (embed template, `RdAcroFormFiller` page-aware) + TDD structural tests.
4. Visual gate: clean worked case rendered → every tick/box confirmed by raster + Read → crops to Ham.
5. `Pnd50FilingService` (P&L FY + `cit_adjustments` Σ + loss c/f + WHT cert credit + 51 prepaid from
   store + `CitCalculator.Compute` + `UnderEstimatePenalty`) + endpoint + FE.
