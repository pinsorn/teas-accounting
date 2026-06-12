# ภ.ง.ด.50 page 5 — field map (C-D recon, 2026-06-12)

> Method = cont.87b/88/89 verbatim: label-join dump (`_pnd50_fields_p5.txt`) + 0-fill marker raster
> + band crops read. Every row traced to a raster crop. Page margin box numbers: **110-134.1**.
> 94 widgets (93 Text + 1 clear Button506), **no radios**. Same 3-column structure as p4
> (① ยกเว้นภาษี x≈245 · ② ต้องเสียภาษี x≈353 · ③ **รวม** x≈461 ← margin numbers, TEAS fills ③ only).
> **All 93 amount boxes are 13-cell combs (11 บาท + 2 สตางค์)**; col ③ geometry in `pnd50_cells.json`.

## รายการที่ 7 — รายจ่ายในการขายและบริหาร (boxes 110-129.1)

| # | label | box | ①field | ②field | ③field | TEAS source |
|---|---|---|---|---|---|---|
| 1 | รายจ่ายเกี่ยวกับพนักงาน | 110 | Text35.117 | Text35.118 | Text35.119 | **DERIVABLE — payroll**: SalaryExpense 5400 + EmployerSso 5410 |
| 2 | ค่าตอบแทนกรรมการ | 111 | Text35.120 | Text35.121 | Text35.122 | not tracked → 0 (or CoA code if adopted) |
| 3 | ค่าไฟฟ้า ค่าประปา ค่าโทรศัพท์ | 112 | Text35.123 | Text35.124 | Text35.125 | GL conv. — needs dedicated 5xxx code(s) / expense-category mapping (§17.3); else → ข้อ 22 |
| 4 | ค่าพาหนะ รายจ่ายในการเดินทาง ค่าที่พัก | 113 | Text35.126 | Text35.127 | Text35.128 | GL conv. (same) |
| 5 | ค่าระวาง ค่าขนส่ง | 114 | Text35.129 | Text35.130 | Text35.131 | GL conv. (same) |
| 6 | ค่าเช่า | 115 | Text35.132 | Text35.133 | Text35.134 | GL conv. (same) |
| 7 | ค่าซ่อมแซม | 116 | Text35.135 | Text35.136 | Text35.137 | GL conv. (same) |
| 8 | ค่ารับรอง | 117 | Text35.138 | Text35.139 | Text35.140 | GL conv. (same) — pairs with ร.8 ข้อ 2 add-back from `tax.cit_adjustments` |
| 9 | ค่านายหน้า ค่าโฆษณา ค่าส่งเสริมการขาย | 118 | Text35.141 | Text35.142 | Text35.143 | GL conv. (same) |
| 10 | ค่าภาษีธุรกิจเฉพาะ (รวมรายได้ส่วนท้องถิ่น) | 119 | Text35.144 | Text35.145 | Text35.146 | NOT AVAILABLE (no SBT in TEAS) → 0 |
| 11 | ค่าภาษีอากร อื่นๆ | 120 | Text35.147 | Text35.148 | Text35.149 | GL conv. (e.g. IrrecoverableVat 5350) |
| 12 | ต้นทุนทางการเงิน | 121 | Text35.150 | Text35.151 | Text35.152 | GL conv. **5500-5599** (DECIDED 2026-06-12): interest expense reports HERE, never p4 ร.6 ข้อ 3 — flat P&L puts all expenses in ladder row 8 and ร.7 must partition row 8 (ร.6 must foot row 6 == 0) |
| 13 | ค่าทำบัญชี | 121.1 | Text35.153 | Text35.154 | Text35.155 | GL conv. |
| 14 | ค่าสอบบัญชี | 122 | Text35.156 | Text35.157 | Text35.158 | GL conv. |
| 15 | เงินที่บริจาคแก่พรรคการเมือง | 122.1 | Text35.159 | Text35.160 | Text35.161 | NOT AVAILABLE → 0 |
| 16 | รายจ่ายเพื่อการกุศลสาธารณะฯ | 123 | Text35.162 | Text35.163 | Text35.164 | `tax.cit_adjustments` (donation lines already feed the p3 ladder) |
| 17 | รายจ่ายเพื่อการศึกษาหรือเพื่อการกีฬา | 124 | Text35.165 | Text35.166 | Text35.167 | `tax.cit_adjustments` (same) |
| 18 | ค่าธรรมเนียมในการให้คำแนะนำและปรึกษา | 125 | Text35.168 | Text35.169 | Text35.170 | GL conv. |
| 19 | ค่าธรรมเนียม อื่นๆ | 126 | Text35.171 | Text35.172 | Text35.173 | GL conv. |
| 20 | หนี้สูญ | 127 | Text35.174 | Text35.175 | Text35.176 | not tracked → 0 (pairs with ร.8 ข้อ 3) |
| 21 | ค่าสึกหรอและค่าเสื่อมราคาของทรัพย์สิน | 128 | Text35.177 | Text35.178 | Text35.179 | GL conv. — depreciation account if posted manually; no FA module |
| 22 | รายจ่ายอื่นที่นอกเหนือจาก 1. ถึง 21. | 129 | Text35.180 | Text35.181 | Text35.182 | **DERIVABLE — catch-all**: Σ remaining 5xxx expense accounts not mapped above |
| 23 | รายจ่ายอื่นหักได้ 2 เท่า ไม่เกินร้อยละ 10 ของกำไรสุทธิ | 129.1 | Text35.183 | Text35.184 | Text35.185 | `tax.cit_adjustments` (double-deduction lines) |
| 24 | รวม 1. ถึง 23. | — | Text35.186 | Text35.187 | Text35.189 | calc — **must foot vs p3 ladder's รายจ่ายในการขายและบริหาร row** (builder throws on mismatch, same as `BuildLadder`) |

NB: there is **no `Text35.188`** — the ③ box of row 24 is `Text35.189` (raster-confirmed marker #73).

## รายการที่ 8 — รายจ่ายที่ไม่ให้ถือเป็นรายจ่ายตามประมวลรัษฎากร (ม.65ตรี, boxes 130-134.1)

Source for the whole section = `tax.cit_adjustments` (LegalRefCode-classified add-backs that
already feed the p3 ladder). Σ ร.8 must reconcile with the ladder's add-back total.

| # | label | box | ①field | ②field | ③field | TEAS source |
|---|---|---|---|---|---|---|
| 1 | ภาษีเงินได้บริษัทหรือห้างหุ้นส่วนนิติบุคคล | 130 | Text35.190 | Text35.191 | Text35.192 | cit_adjustments (ม.65ตรี(6)) |
| 2 | ค่ารับรอง | 131 | Text35.193 | Text35.194 | Text35.195 | cit_adjustments (ม.65ตรี(4)) |
| 3 | หนี้สูญ | 132 | Text35.196 | Text35.197 | Text35.198 | cit_adjustments |
| 4 | เงินสำรอง | 133 | Text35.199 | Text35.200 | **Text35.2011** | cit_adjustments (ม.65ตรี(1)) |
| 5 | รายจ่ายตามรายการที่ 7 ข้อ 23. | 134 | Text35.201 | Text35.202 | Text35.203 | cit_adjustments (add-back of the double-deduct base) |
| 6 | รายจ่ายที่ไม่ให้ถือเป็นรายจ่ายฯ อื่นๆ | 134.1 | Text35.204 | Text35.205 | Text35.206 | cit_adjustments (remainder) |
| 7 | รวม 1. ถึง 6. | — | Text35.207 | Text35.208 | Text35.209 | calc |

⚠️ **Name trap (raster-confirmed)**: row 4 col ③ is `Text35.2011` (NOT `Text35.201` —
`Text35.201` is row 5 col ①). Marker #85 landed in box 133 col ③; #88 in box 134 col ①.

## Feasibility tally (col ③ data lines, totals excluded)

- ร.7: 23 data lines → 2 unconditionally derivable (1 payroll, 22 catch-all) · 3 from
  cit_adjustments (16, 17, 23) · 14 conditional on CoA/expense-category mapping ·
  4 zero/NA (10, 15, 20, 2).
- ร.8: 6 data lines → all 6 derivable from `tax.cit_adjustments` (zero rows render 0).
