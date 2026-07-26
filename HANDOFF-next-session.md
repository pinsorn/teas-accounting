# HANDOFF → session ถัดไป (เขียน 2026-07-26 ~11:5x, context เต็ม)

## อ่าน 3 ไฟล์นี้ก่อน แล้วจะเข้าใจทั้งหมด
1. `PLAN-army-followup-2026-07-25.md` — แผนงานที่ Ham อนุมัติ + คำตอบทุกข้อ + log ว่าทำอะไรไปแล้ว
2. `MORNING-BRIEF-2026-07-26.md` — สรุปผลรอบกลางคืน (ของที่ปิดไปแล้ว + เหตุผล)
3. `swarm-findings/army/VERDICT-army-2026-07-25.md` — ที่มาทั้งหมด (army test 11 พื้นที่)

## สถานะ: 13/14 ข้อปิดแล้ว · prod = **v1.23.0** · tree สะอาด · main = `d877286`

### LIVE บน prod แล้ว (v1.23.0, verify สดแล้วทุกตัว)
O7 widget กรองสิทธิ์ · O9 วันที่สิ้นสุดการจ้าง · O12 เลขบัญชี สปส. 10 หลัก · O13 DocDate validator ·
O1 badge สินทรัพย์ไม่ลง GL · **O8 payroll proration** ← ตัวใหญ่สุด
- หลักฐาน O8: `swarm-findings/army/V4-o8-live-verify.md` — JE 176 บน co7 `Dr 5400 = 112,258.07`
  (ก่อนแก้จะเป็น 180,000) · ภ.ง.ด.1 PDF พิมพ์เลข prorated จริง · full-month = 60,000.00 เป๊ะ

### commit แล้ว รอ release ถัดไป
**O4** หน้าแก้ใบเบิก Draft/Rejected · **O2a** chip ใบกำกับบนใบวางบิล · **G5** ป้าย VAT non-VAT
(commit `d877286`, gate 963/0/8 + tsc + next build)

### เหลือ 2 ข้อ — สเปกเขียนพร้อมแล้ว ไม่ต้องออกแบบใหม่
- **O10** deduction/คืนเงินจ่ายเกิน → `specs/payroll-deductions-o10.md`
  - **จุดตาย**: `GlPostingService` comment บอกเองว่า ΣOtherDeductions ≠ 0 ทำให้ JE ไม่ balance และถูก
    reject → **ต้องเพิ่มบัญชีคู่ใน `GlAccountsOptions`** (`backend/src/Accounting.Infrastructure/Ledger/GlAccountsOptions.cs`)
    ก่อนอย่างอื่น · pattern: บรรทัด 28-32 คือชุด payroll (5400/5410/2153/2160/2170)
  - **Ham ตอบเรื่องภาษีแล้ว**: หักจาก **net เท่านั้น** ห้ามแตะฐานภาษี/สปส. · ถ้าภาษีเดือนก่อนเกินจริง
    ให้ไปแก้ ภ.ง.ด.1 **เดือนนั้น** ไม่ net เงียบ ๆ ในเดือนนี้
  - เทสสำคัญ: JE balance เมื่อมี deduction · ภ.ง.ด.1/1ก/สปส. **byte-identical** มี/ไม่มี deduction ·
    deduction > (gross − pit − sso) ต้องถูกปฏิเสธ (net ติดลบไม่ใช่ผลลัพธ์ payroll)
- **O11** สปส.1-10 ส่วนที่ 2 → `specs/sps110-part2-o11.md`
  - template **มี 4 หน้าและหน้า 2 คือส่วนที่ 2 อยู่แล้ว** · `RdField.Page` รองรับแล้ว · เครื่องมือวัดพิกัด
    คือ `TaxFormFillDiagnostic` (`TEAS_DIAG=1`) · เหลือ: พิกัดหน้า 2 + overflow >10 คนเป็นหลายแผ่น
  - **ห้ามอ่าน `Employee.BaseSalary`** — ต้องอ่านจาก payslip snapshot ไม่งั้นพัง proration ของ O8
  - มี escalation point: ถ้า compose หน้า PDF ต้องแก้เกินเล็กน้อย → หยุดถาม ไม่ต้องดัน

### O2b — Ham ตอบแล้ว ยังไม่ได้ทำ
ผูกใบกำกับแล้ว **generate บรรทัดให้เลย** (manual แก้ทับได้) · เพิ่มใน `specs/fix-army-findings-2026-07-22.md`
หัวข้อ O2b แล้ว แต่ยังไม่มีสเปกลงรายละเอียด

## กฎที่เพิ่งเรียนคืนนี้ — อ่านก่อนสั่งงาน (fold ไว้แล้วทั้งหมด)
- **Fable รัน full suite เอง ไม่ให้ worker babysit** (CLAUDE.md) — worker ตัวหนึ่งเสีย 220k tokens
  กับ suite ครั้งเดียวเพราะจบเทิร์นไปรอ monitor
- **`--filter` run ไม่ใช่ gate** — targeted 81/81 เขียวขณะ suite เต็มพัง 35 ตัว
- **guard เรื่องรูป request วางที่ DTO validator ไม่ใช่ service seam** — วางผิดที่พังเทส 35 ตัวที่ไม่มีบั๊ก
- **สเปกงานเงินต้องเขียน invariant ไม่ใช่ observable** — "VAT totals = 0" ทำให้ได้โค้ดที่จ่ายเงินขาด
- **เทสห้ามใช้ `DateTime.UtcNow` เทียบกับกฎที่ pin Bangkok** — เขียวก่อนเที่ยงคืน แดง 00:00–07:00 ICT
  (troubles-wiki มี entry แล้ว · ไฟล์ `McpServerSmokeTests` ยังเหลือ `UtcNow` อีก ~22 จุดที่ยังไม่ระเบิด)
- commit message **ห้ามมี backtick** — bash ตีความเป็น command substitution ใช้ `git commit -F <file>`
- 7-day quota 88% → **Claude worker ห้ามใหม่ที่ ≥85%** ใช้ **Codex** (pool แยก) ตาม quota arbitrage

## สภาพ prod / สนามเทส
- **co5** = สนาม VAT (บริษัท ทดสอบ VAT DUMMY) · users `UxSwarm-2026-<A1..B1>` ดู
  `specs/uxswarm-multirole-co5.md`
- **co6** = non-VAT **แช่แข็ง** — ปิดปี + ปิดครบ 12 เดือน → สร้าง PV ไม่ได้จนถึง 2027 (นี่คือ O14 ที่ยัง
  ไม่ได้ทำ · สเปก `specs/period-monthly-reopen-o14.md` เขียนพร้อมแล้ว แต่ Ham ยังไม่ได้จัดลำดับ)
- **co7** = non-VAT ใช้งานได้ (id=7) · users `nvadmin02`/`nvchief02` pw `UxSwarm-2026-NV4`/`NV5` ·
  มีพนักงาน 3 คน + payroll run 10 POSTED (ข้อมูลเทส O8)
- MCP key ของ co5 อยู่ที่ `~/.claude/teas-secrets/co5-mcp-key.txt` (ห้าม commit)
- deploy: `TEAS_TEST_PG`/ssh key/ขั้นตอน publish อยู่ใน memory `teas-prod-deploy-plink` ·
  **build จาก git worktree แยกได้** ถ้า tree มีของค้าง (ทำแบบนี้ตอน v1.23.0)

## คำสั่งที่ใช้บ่อย
```
# full Api suite (Fable รันเอง)
TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true" \
TEAS_REPO_ROOT="Y:/ClaudePlayground/TEAS-Project" \
dotnet test backend/tests/Accounting.Api.Tests --nologo > Z:/temp/claude/gate.log 2>&1
# ถ้า build copy DLL ไม่ได้ = testhost ค้าง → kill dotnet/testhost ก่อน
```
