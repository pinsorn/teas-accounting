# PLAN — งานต่อจาก army (Ham ตัดสินใจแล้ว 2026-07-25 ~23:1x)

ที่มาการตัดสินใจ: Ham ตอบผ่าน AskUserQuestion · รายละเอียดแต่ละข้อ: `DECISIONS-army-2026-07-25.md`
+ `specs/fix-army-findings-2026-07-22.md` (รหัส O1-O15)

## Ham สั่งทำ — 10 ข้อ
| กลุ่ม | ข้อ | สถานะ |
|---|---|---|
| Payroll | **O9** ช่องวันที่ลาออก · **O8** proration รายวัน · **O10** adjustment ติดลบ | GO ทั้ง 3 |
| SSO | **O12** ช่องเก็บเลขบัญชีนายจ้าง · **O11** สปส.1-10 ส่วนที่ 2 (รายชื่อลูกจ้าง) | GO ทั้ง 2 |
| Batch เล็ก | **O1** badge เตือน FA ยังไม่ลง GL · **O13** DocDate 422 · **O4** หน้าแก้ใบเบิกค่าใช้จ่าย Draft · **O5** ภ.พ.36 export PDF | GO ทั้ง 4 |
| งวดบัญชี | **O14** reopen งวดรายเดือน (permission + audit log) | GO |

## รอบสอง — Ham ตัดสินครบแล้ว (ไม่มีข้อไหนค้างอีก)
- **O6** → **ให้ผมไปหาข้อมูล RD ก่อน** แล้วสรุปมาให้เลือกจากข้อมูลจริง (ปล่อย AGY research แล้ว →
  `swarm-findings/army/O6-research-50twi-pnd53-seq.md`) — ตัดสินหลังได้ผล
- **O8 กฎ proration** → **นับวันตามปฏิทิน**: เงินเดือน × (วันที่เป็นลูกจ้างในเดือนนั้น ÷ จำนวนวันทั้งเดือน)
  · PIT (ภ.ง.ด.1) และประกันสังคมคิดจากค่าจ้างหลัง prorate อัตโนมัติ ← **กฎนี้ยืนยันแล้ว WAVE 2 ship ได้**
- **O2 + O3** → **ผมเทสเอง** (ปล่อย leg แล้ว → `swarm-findings/army/O2-O3-verify.md`)
- **G5** → **แค่เปลี่ยนคำที่แสดง** เติม "(เครดิตไม่ได้ — รวมเป็นต้นทุน)" ต่อท้ายป้าย VAT บนหัวใบ PV
  ของบริษัท non-VAT · ไม่ต้อง migration → ไปรวมใน WAVE 5

## ลำดับที่ผมจะทำ (ถูก→แพง, prerequisite ก่อนของที่พึ่งมัน)
1. **WAVE 1 — prerequisites + ของเล็ก (batch เดียว, warm worker)**: O9 (ช่องวันที่ลาออก) · O12 (ช่องเลขบัญชี SSO) ·
   O13 (validator 422 — recipe มีแล้ว) · O1 (badge FA). ทั้งสี่เล็ก แตะไฟล์ต่างกัน ไม่มีอันไหนเป็น money invariant
2. **WAVE 2 — O8 proration** (money + กฎหมาย → Opus design + Opus review). **ต้องยืนยันกฎกับ Ham ก่อน ship**:
   ผมเสนอ default = นับ**วันตามปฏิทิน** (วันที่ทำงานจริง / จำนวนวันในเดือนนั้น) แล้ว PIT (ภ.ง.ด.1) กับประกันสังคม
   คิดจากค่าจ้างที่จ่ายจริงหลัง prorate โดยอัตโนมัติ — ตรงกับ practice ไทยทั่วไป แต่ **ไม่ ship จนกว่า Ham ยืนยัน**
3. **WAVE 3 — O11 สปส.1-10 ส่วนที่ 2** (ต่อจาก O12) + **O10** adjustment ติดลบ (money → Opus review)
4. **WAVE 4 — O14 reopen รายเดือน** (state machine + permission + audit log → Opus design + review;
   ทดสอบบน co6 ที่ติดสภาพจริงอยู่แล้ว = acceptance test ในตัว)
5. **WAVE 5 — O4** หน้าแก้ใบเบิก + **O5** ภ.พ.36 PDF + **G5** ป้าย VAT บนหัว PV (FE/report ธรรมดา)
6. ~~**O6**~~ **ปิดแล้ว 2026-07-25 — ไม่ต้องแก้โค้ด**: ช่องนั้นไม่ใช่ข้อบังคับตอนออก cert และ cert ที่เว้นว่าง
   **ใช้เครดิตภาษีได้ตามกฎหมาย** (ม.60) · ยืนยัน citation: rd.go.th ม.50 ทวิ + หนังสือตอบข้อหารือ
   กค 0702/3793 · เหตุผลเชิงโครงสร้างก็บังคับอยู่แล้ว: ม.50 ทวิ ให้ออก cert **ตอนจ่าย** ซึ่งเกิดก่อน
   ภ.ง.ด.53 มีอยู่ และ cert แก้ไม่ได้ → เว้นว่างเป็นทางเดียวที่ไม่ผิดกฎข้อใดข้อหนึ่ง
   (ปฏิเสธข้อเสนอให้พิมพ์ voucher ID ของเราลงช่องนั้น — ช่องระบุชัดว่า "ลำดับที่ในแบบ ภ.ง.ด.53"
   ใส่เลขอื่นจะทำให้เข้าใจผิด) → **เหลือ 13 ข้อที่ต้องลงมือ**

## Ham ไปนอน 2026-07-25 ~23:2x — "ฝากทำต่อและเทสหน่อยนะ"
ผมเดินแผนเองต่อ: WAVE 1 → review+commit → WAVE 2 (O8 design) → release+deploy รวมทีเดียว
(ไม่ deploy O7 แยก เพราะ release PR #99 จะรวม WAVE 1 เข้าไปเอง = deploy รอบเดียวเสี่ยงน้อยกว่า)

## ข้อจำกัดที่ต้องเคารพ
- 7-day quota 85% ตอนวางแผน → ทำเป็น wave ไม่รวดเดียว, checkpoint ทุก wave, Codex/AGY ช่วยได้ (pool แยก)
- Payroll/SSO = money + กฎหมาย → Opus design + Opus review เสมอ, ห้าม Fable เขียนโค้ดเอง
- co6 งวดปิดหมด (ใช้เทส O14 ได้ดี) · co7 งวดเปิด (ใช้เทส payroll/SSO) · co5 = สนาม VAT
- ทุก wave: full suite เป็น gate (ไม่ใช่ --filter), Fable อ่าน diff ก่อน commit, deploy + verify สด

## Night-shift log + quota pivot (2026-07-26 ~00:1x)
- คืบคืนนี้: **WAVE 1 ปิด** (`3877df7`, suite 955/0/8) · **O6 ปิดไม่ต้องแก้โค้ด** (`c465583`) ·
  **O3 ปิดเป็น automation artifact + O2 แยกเป็น O2a/O2b** (`0babb6f`) · **WAVE 2 design ผ่าน
  Fable review** (`f0346e4`) และ implement กำลังวิ่ง
- **Quota pivot: 7-day = 87%** (เกินเส้น 85% ของ CLAUDE.md → ห้าม dispatch Claude worker ใหม่)
  - Wave 2 implement คือ Claude worker **ตัวสุดท้าย**ของคืนนี้
  - Tier-2 review ของ O8 (money) จะ**ไม่**ใช้ opus-reviewer (Claude pool) → ใช้ **Codex** (pool แยก)
    ตาม quota-arbitrage rule: footgun work ส่ง Codex ไม่ใช่ลดชั้นเป็น Claude ที่ถูกกว่า
    + Fable อ่าน diff เองด้วย (ถูกกว่าการ spawn subagent มาก)
  - **WAVE 3/4/5 ไม่เริ่มคืนนี้** — รอ 7-day ฟื้น (reset 1785229200) หรือ Ham สั่ง
- ลำดับที่เหลือหลัง Wave 2: Fable diff review → Codex money review → commit → release **ครั้งเดียว**
  (PR #99 สะสม O7 + Wave 1 + O8 ไว้แล้ว) → deploy (FE + API เพราะ O8/O13 แตะ backend) → verify สด
  (proration บน co7 ที่งวดเปิด: จ้างกลางเดือน/ออกกลางเดือน เทียบ hand-calc 32,903.23 / 19,354.84)

## 2026-07-26 ~05:0x — WAVE 5 ปล่อยผ่าน Codex (quota arbitrage)
- 5h reset แล้ว (6%) แต่ **7-day ยัง 88%** → เกินเส้น 85% ที่ห้าม dispatch Claude worker ใหม่
  → ตามกฎ quota-arbitrage ของ CLAUDE.md: **implementation ไป Codex (pool แยก) ไม่ใช่หยุดงาน**
  (ผมหยุดผิดไปหนึ่งรอบก่อนจะนึกถึงข้อนี้ — จดไว้)
- Codex ทำ 3 ข้อ FE ล้วน ไม่มี money invariant ไม่ต้องตัดสินใจเชิง product: **O2a** (โชว์ chip
  ใบกำกับที่ผูกบนหน้า detail ของใบวางบิล — ข้อมูลมีอยู่แล้ว API ส่งมาแล้ว) · **G5** (ป้าย VAT บนหัว PV
  ของบริษัท non-VAT เติมคำว่าเครดิตไม่ได้/รวมเป็นต้นทุน — ป้ายเท่านั้น ตัวเลขไม่แตะ) · **O4** (หน้าแก้
  ใบเบิกค่าใช้จ่ายที่ยัง Draft/Rejected — hook `useUpdateExpenseClaim` มีอยู่แล้วแต่ไม่มีใครเรียก)
- **O5 ให้ Codex สืบก่อนอย่างเดียว ห้ามสร้าง**: ภ.พ.36 มี template asset ฝังในโปรเจกต์เหมือน
  ภ.ง.ด.54/สปส.1-10 หรือเปล่า → ถ้ามีก็เป็นงานต่อสาย ถ้าไม่มีต้องทำ template + box mapping (งานใหญ่กว่ามาก)
  ค่อยตัดสินจากคำตอบ
- Gate ของรอบนี้: tsc + next build (Codex รันเอง) · **dotnet suite ผมรันเอง** ตามกฎใหม่ที่ fold ไว้

## 2026-07-26 ~06:0x — สเปกครบทุก wave ที่เหลือ (Fable เขียนเอง ไม่กิน worker quota)
- `specs/period-monthly-reopen-o14.md` (**O14**) — key decision D3: ห้าม reopen เดือนที่อยู่ในปีที่ปิดแล้ว
  (P&L ถูกโยนเข้ากำไรสะสมไปแล้ว → post ใหม่จะทำให้ closing entry กับงบดุลขัดกันเงียบ ๆ) · ใช้ permission
  เดิม `gl.period.close` · ไม่แตะ schema (audit ลง activity log) · concurrency ลอก
  `YearCloseService.ReopenAsync` ที่ claim ด้วย affected-rows
- `specs/sps110-part2-o11.md` (**O11**) — ค้นพบว่า template มี 4 หน้าและหน้า 2 คือส่วนที่ 2 อยู่แล้ว,
  `RdField.Page` รองรับอยู่แล้ว, มี `TaxFormFillDiagnostic` (TEAS_DIAG=1) เป็นเครื่องมือวัดพิกัดอยู่แล้ว
  → งานเหลือคือพิกัดหน้า 2 + overflow >10 คนเป็นหลายแผ่น · ห้ามอ่าน `Employee.BaseSalary` (จะพัง O8)
- `specs/payroll-deductions-o10.md` (**O10**) — จุดตายคือ **GL**: comment ในโค้ดบอกเองว่า ΣOther ที่ไม่เป็น
  ศูนย์ทำให้ JE ไม่ balance และถูก reject → ต้องมีบัญชีคู่ใน `GlAccountsOptions` ก่อน · deduction ลดแค่
  **net** ห้ามแตะฐานภาษี/สปส. · **OPEN QUESTION ถึง Ham**: การคืนเงินจ่ายเกินของเดือนก่อนควรไปแก้ ภ.ง.ด.1
  ของเดือนนั้น ไม่ใช่ netting เงียบ ๆ ในเดือนนี้ (ผมแนะนำแบบนี้ แต่ไม่ตัดสินแทน)
- สถานะ: **ทุก wave ที่เหลือมีสเปกพร้อม implement แล้ว** รอ pool ว่าง (7-day 88%) หรือ Ham สั่ง ·
  Codex ยังทำ Wave 5 อยู่
