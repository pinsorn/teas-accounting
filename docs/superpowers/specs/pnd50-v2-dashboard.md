# ภ.ง.ด.50 v2 (default) + CIT filing dashboard — spec (Ham mandate 2026-06-11 เช้า)

> Ham: "v2 ต้องเป็น default ไม่ใช่ v1 — แค่ถ้าไม่มีเคสก็ไม่ได้ใส่. ทำ v2 พร้อม dashboard
> แสดงข้อมูลจะ generate ภ.ง.ด.50 (และ 51) พร้อม list ข้อมูลทั้งหมดว่ามีรายการอะไรบ้าง
> ที่เอามาคำนวณใส่ใน 50/51."

## 1. Semantics shift (v1 → v2)

- v1 (shipped cont.88): fills p1 + p2 รายการที่ 1 only; REFUSES any year with
  adjustments ≠ 0 or loss c/f ≠ 0 (blank p3 would assert zero).
- **v2 = the only behaviour (no v1/v2 toggle):** the filler ALWAYS renders the
  รายการที่ 2 ladder (p3) and the balance sheet (p6) from real data. Zero
  adjustments ⇒ the ladder prints with zeros/pass-through values — never blank-as-lie.
  The `attestBlankSchedules` attestation narrows to the pages still not rendered
  (p4-5 ต้นทุน/ขายบริหาร detail, p7 แบบแจ้งกรรมการ) — rename/reword accordingly.
  The adjustments/loss-c/f refusal guards DROP; remaining refusals only for cases the
  form still can't honestly render (ยื่นเพิ่มเติม, non-THB).

## 2. ⚠️ Prerequisite recon (เหมือน p1/p2 — ห้ามข้าม)

p3 (95 widgets) + p6 (42 widgets) มี dump แล้ว (`_pnd50_fields_p{3,6}.txt`) แต่ยัง
**ไม่เคย 0-fill raster / radio-confirm**. ต้องทำก่อน build:
0-fill → margin-number confirm → radio_confirm ทุก choice บน p3/p6 → ขยาย
`pnd50_radiomap.md` + `pnd50_cells.json` (geo.py WANT list + PAGES=(2,5)).

## 3. p3 รายการที่ 2 mapping (data → boxes)

Data source = `CitCalculator.Compute` + `cit_adjustments` rows:
- กำไร(ขาดทุน)สุทธิทางบัญชี = `AccountingProfit`
- บวกกลับ/หักออก: ฟอร์มมีบรรทัดตามหมวด ม.65ทวิ/ตรี — map `cit_adjustments.legal_ref_code`
  → บรรทัดฟอร์ม; รายการที่ไม่ตรงบรรทัดสำเร็จรูป → ช่อง "อื่นๆ". (recon จะบอกว่ามีบรรทัดอะไรบ้าง)
- ขาดทุนยกมาไม่เกิน 5 ปี = `LossApplied` (จาก `CitLossCarryForward`)
- กำไรสุทธิที่ต้องเสียภาษี = `TaxableProfit` → ไหลกลับไป p2 box 48-49 (เลิก derive เอง)
- รายการที่ 3 (รายได้) อยู่ p3 ด้วย — ดู recon ว่า v2 ต้องกรอกขั้นต่ำแค่ไหน (รายรับรวมจาก P&L มีอยู่)

## 4. p6 งบแสดงฐานะการเงิน

`BalanceSheetAsync` (cont.87) → asset/liability/equity sections + CurrentPeriodEarnings.
Map ลงช่อง p6 ตาม recon. ตัวเลขต้อง foot (`Balanced` invariant มีแล้ว).

## 5. CIT filing dashboard (FE ขยาย `/tax-filings/cit`)

เป้าหมาย: เห็นครบก่อนกด generate ว่า "แบบจะถูกกรอกด้วยอะไรบ้าง" ทั้ง 50 และ 51.
- **BE: `GET /tax-filings/pnd50/preview?year`** — JSON dry-run: ทุก figure ที่จะลงแบบ
  (ladder ทุกขั้น, WHT credit + รายใบ 50ทวิ ที่นับ [จาก `WhtReceivableRegister`],
  pnd51 estimate/prepaid, surcharge ม.67ตรี, SME flag+เหตุผล, balance-sheet totals,
  และ "เคสที่ทำให้ refuse" ถ้ามี) — endpoint ใหม่ → ระวัง §11: อยู่ใน mandate นี้แล้ว.
  (51 มี data ใน summary อยู่แล้ว — แสดงจาก store + recompute เบา ๆ)
- **FE sections บนหน้า cit:**
  1. การ์ดสรุป "ภ.ง.ด.51 ที่ยื่นไว้" (estimate, prepaid, วันที่บันทึก)
  2. การ์ด "บันไดคำนวณ ภ.ง.ด.50" (ladder preview ทุกบรรทัด + SME/อัตรา)
  3. ตาราง "เครดิตภาษีถูกหัก ณ ที่จ่าย (ขาเข้า)" — รายใบจาก WHT receivable register FY
  4. ตาราง adjustments (มีอยู่แล้ว) + loss c/f ที่จะใช้
  5. งบฐานะย่อ (totals + balanced badge)
  6. ปุ่ม generate 50 (attest ใหม่) / 51 — ย้าย/รวมการ์ด v1 เดิม
- Permission: FilingPreview (read) เหมือนเดิม.

## 6. Tests

- recon artefacts render-confirmed (visual gate p3/p6 เหมือน p1/p2 — crops ถึง Ham)
- BuildSheet v2: ladder cases (adjustments +/-, loss partial/expired, zero-case prints zeros)
- preview endpoint: figures == สิ่งที่ filler ใช้จริง (single source — preview กับ Fill
  ต้อง derive จาก object เดียวกัน ห้ามคำนวณสองที่)
- e2e: dashboard แสดง ladder + generate ได้

## Queue หลังจากนี้ (Ham เคาะ 2026-06-11)

1. **นี้** — pnd50 v2 + dashboard (recon p3/p6 ก่อน)
2. หน้าเว็บรวมเอกสาร `/documents` (จาก docs/RD-Forms ที่ commit แล้ว: ตารางฟอร์ม + กำหนดยื่น + เปิด PDF)
3. **Dev DB reset** ("มันหลอนหมดแล้ว" — หลาย version): pg_dump backup ก่อน → drop/recreate
   `accounting_dev` → DbInitializer reseed → mint Reptify key ใต้ company 2 → e2e re-baseline.
   ทำคู่ M15 (วางท่อ dump อัตโนมัติไปเลย)
