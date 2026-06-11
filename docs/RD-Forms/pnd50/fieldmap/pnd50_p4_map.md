# ภ.ง.ด.50 page 4 — field map (C-D recon, 2026-06-12)

> Method = cont.87b/88/89 verbatim: label-join dump (`_pnd50_fields_p4.txt`) + 0-fill marker raster
> (`#N` = dump line N) + band crops read at zoom 2.8-4.0. Every row below was traced to a raster
> crop. Page margin box numbers: **86-109**. 88 widgets (87 Text + 1 clear Button505), **no radios**.

## Column structure (render-confirmed from the section headers)

All three sections share the same 3 columns — identical to the p3 ladder:

| col | header | x≈ | TEAS use |
|---|---|---|---|
| ① | กิจการที่ได้รับยกเว้นภาษีเงินได้ | 251-253 | not filled (no BOI-exempt split) |
| ② | กิจการที่ต้องเสียภาษีเงินได้ | 359-361 | not filled |
| ③ | **รวม** | 467-469 | **the only column TEAS fills** (same rubric as p3: กรณีทั่วไป/ลดอัตรา → ③ only) |

**Every amount box on this page is a 13-cell comb (11 บาท + 2 สตางค์)** — geometry for all col ③
fields is in `pnd50_cells.json` (added 2026-06-12, additive regen; col ①/② intentionally omitted).
Printed margin numbers sit on the col ③ boxes only.

## รายการที่ 4 — ต้นทุนผลิต/ต้นทุนการให้บริการ (boxes 86-99)

| # | label | box | ①field | ②field | ③field | TEAS source |
|---|---|---|---|---|---|---|
| 1 | วัตถุดิบ และวัสดุคงเหลือ ณ วันเริ่มรอบระยะเวลาบัญชี | 86 | Text35.30 | Text35.31 | Text35.32 | **zero-by-design** (no inventory tracking) |
| 2 | ซื้อวัตถุดิบ และวัสดุ | 87 | Text35.33 | Text35.34 | Text35.35 | zero-by-design (no inventory) |
| 3 | ค่าใช้จ่ายอื่นๆ ในการซื้อวัตถุดิบ และวัสดุ | 88 | Text35.36 | Text35.37 | Text35.38 | zero-by-design |
| 4 | รวม 1. ถึง 3. | — | Text35.39 | Text35.40 | Text35.41 | calc (Σ 1-3) |
| 5 | หัก วัตถุดิบ/วัสดุคงเหลือ ณ วันสุดท้ายฯ | 89 | Text35.42 | Text35.43 | Text35.44 | zero-by-design |
| 6 | ต้นทุนวัตถุดิบและวัสดุใช้ไป (4. - 5.) | 90 | Text35.45 | Text35.46 | Text35.47 | calc |
| 7 | งานระหว่างทำ/สินค้าระหว่างผลิตคงเหลือ ณ วันเริ่มรอบฯ | 91 | Text35.48 | Text35.49 | Text35.50 | zero-by-design |
| 8 | เงินเดือน และค่าจ้างแรงงาน | 92 | Text35.51 | Text35.52 | Text35.53 | NOT AVAILABLE as *production* labour — TEAS payroll (5400/5410) is flat SG&A and is reported in p5 ร.7 ข้อ 1; filling both = double count. Keep 0 here. |
| 9 | ค่าแห่งกู๊ดวิลล์ ค่าแห่งลิขสิทธิ์ หรือสิทธิอย่างอื่น | 93 | Text35.54 | Text35.55 | Text35.56 | NOT AVAILABLE → 0 |
| 10 | ค่าเชื้อเพลิงหรือพลังงาน | 94 | Text35.57 | Text35.58 | Text35.59 | NOT AVAILABLE → 0 |
| 11 | ค่าภาชนะบรรจุ ค่าหีบห่อ | 95 | Text35.60 | Text35.61 | Text35.62 | NOT AVAILABLE → 0 |
| 12 | ค่าสึกหรอและค่าเสื่อมราคา | 96 | Text35.63 | Text35.64 | Text35.65 | NOT AVAILABLE (no FA module; depreciation, if any, lives in SG&A accounts → p5) → 0 |
| 13 | ค่าใช้จ่ายในการผลิต/การให้บริการ อื่นๆ | 97 | Text35.66 | Text35.67 | Text35.68 | GL conv. — only if a 51xx cost-of-service convention is adopted; else 0 |
| 14 | รวม 8. ถึง 13. | — | Text35.69 | Text35.70 | Text35.71 | calc |
| 15 | รวม (6. + 7. + 14.) | — | Text35.72 | Text35.73 | Text35.74 | calc |
| 16 | หัก งานระหว่างทำ/สินค้าระหว่างผลิตคงเหลือ ณ วันสุดท้ายฯ | 98 | Text35.75 | Text35.76 | Text35.77 | zero-by-design |
| 17 | ต้นทุนผลิต/ต้นทุนการให้บริการ (15. - 16.) | 99 | Text35.78 | Text35.79 | Text35.80 | calc — **must foot vs p3 รายการที่ 3 ต้นทุนขาย** if filled |

⚠️ For TEAS (trading/service, no inventory, flat P&L): the whole รายการที่ 4 schedule is
zeros/attest-blank unless Ham adopts a production-cost account convention. Inventory-dependent
lines (1, 2, 3, 5, 7, 16) are **zeros-by-design** permanently.

## รายการที่ 5 — รายได้อื่น (boxes 100-105)

| # | label | box | ①field | ②field | ③field | TEAS source |
|---|---|---|---|---|---|---|
| 1 | กำไรจากการจำหน่ายทรัพย์สิน | 100 | Text35.81 | Text35.82 | Text35.83 | NOT AVAILABLE (no FA module) → 0 |
| 2 | กำไรจากอัตราแลกเปลี่ยนเงินตรา | 101 | Text35.84 | Text35.85 | Text35.86 | zero (THB-only posture) |
| 3 | ดอกเบี้ยรับ | 102 | Text35.87 | Text35.88 | Text35.89 | GL conv. — needs a dedicated interest-income 4xxx account; else falls into line 6 |
| 4 | เงินปันผลหรือส่วนแบ่งกำไร | 103 | Text35.90 | Text35.91 | Text35.92 | GL conv. — same (dedicated account or line 6) |
| 5 | เงินชดเชยค่าภาษีอากร | 104 | Text35.93 | Text35.94 | Text35.95 | NOT AVAILABLE → 0 |
| 6 | รายได้อื่นที่นอกเหนือจาก 1. ถึง 5. | 105 | Text35.96 | Text35.97 | Text35.98 | **DERIVABLE**: Σ revenue accounts ≠ Sales(4000)/SalesReturn(4100) |
| 7 | รวม 1. ถึง 6. | — | Text35.99 | Text35.100 | Text35.101 | calc — must foot vs the p3 ladder's รายได้อื่น row |

## รายการที่ 6 — รายจ่ายอื่น (boxes 106-109)

| # | label | box | ①field | ②field | ③field | TEAS source |
|---|---|---|---|---|---|---|
| 1 | ขาดทุนจากการจำหน่ายทรัพย์สิน | 106 | Text35.102 | Text35.103 | Text35.104 | NOT AVAILABLE → 0 |
| 2 | ขาดทุนจากอัตราแลกเปลี่ยนเงินตรา | 107 | Text35.105 | Text35.106 | Text35.107 | zero (THB-only) |
| 3 | ต้นทุนทางการเงิน | 108 | Text35.108 | Text35.109 | Text35.110 | GL conv. — dedicated interest-expense account; ⚠️ same label exists at p5 ร.7 ข้อ 12 — RD instructions decide which side; fill ONE, never both |
| 4 | รายจ่ายอื่นที่นอกเหนือจาก 1. ถึง 3. | 109 | Text35.111 | Text35.112 | Text35.113 | flat P&L cannot split non-operating from SG&A → default 0 (everything reports in ร.7 ข้อ 22) |
| 5 | รวม 1. ถึง 4. | — | Text35.114 | Text35.115 | Text35.116 | calc |

## Feasibility tally (col ③ data lines, totals excluded)

- ร.4: 12 data lines → 0 derivable · 6 zeros-by-design (inventory) · 6 NOT AVAILABLE/0.
- ร.5: 6 data lines → 1 derivable (ข้อ 6 catch-all) · 2 conditional on CoA codes (3, 4) · 3 zero/NA.
- ร.6: 4 data lines → 1 conditional (ข้อ 3) · 3 zero/NA.
