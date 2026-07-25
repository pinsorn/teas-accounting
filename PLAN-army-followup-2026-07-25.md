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

## ยังไม่ได้ถาม / ยังไม่ตัดสิน
- **O6** 50ทวิ ช่อง "ลำดับที่ ... ในแบบ ภ.ง.ด.53" ว่างเสมอ — ต้องใช้ความรู้บัญชีของ Ham ว่า practice จริงทำอย่างไร
  (เติมตอน finalize โดยออกสำเนาใหม่ / พิมพ์ "-" / ปล่อยว่าง) ถามเมื่อ Ham ว่าง
- **O2** ใบวางบิลรวมใบกำกับ + **O3** ปุ่มดาวน์โหลด PDF — Ham กดดูเอง 2 นาที
- **G5** ป้าย "VAT" บนหัว PV ของบริษัท non-VAT (ตัวเลขถูก) — ต้อง migration ถ้าจะแยกถัง

## ลำดับที่ผมจะทำ (ถูก→แพง, prerequisite ก่อนของที่พึ่งมัน)
1. **WAVE 1 — prerequisites + ของเล็ก (batch เดียว, warm worker)**: O9 (ช่องวันที่ลาออก) · O12 (ช่องเลขบัญชี SSO) ·
   O13 (validator 422 — recipe มีแล้ว) · O1 (badge FA). ทั้งสี่เล็ก แตะไฟล์ต่างกัน ไม่มีอันไหนเป็น money invariant
2. **WAVE 2 — O8 proration** (money + กฎหมาย → Opus design + Opus review). **ต้องยืนยันกฎกับ Ham ก่อน ship**:
   ผมเสนอ default = นับ**วันตามปฏิทิน** (วันที่ทำงานจริง / จำนวนวันในเดือนนั้น) แล้ว PIT (ภ.ง.ด.1) กับประกันสังคม
   คิดจากค่าจ้างที่จ่ายจริงหลัง prorate โดยอัตโนมัติ — ตรงกับ practice ไทยทั่วไป แต่ **ไม่ ship จนกว่า Ham ยืนยัน**
3. **WAVE 3 — O11 สปส.1-10 ส่วนที่ 2** (ต่อจาก O12) + **O10** adjustment ติดลบ (money → Opus review)
4. **WAVE 4 — O14 reopen รายเดือน** (state machine + permission + audit log → Opus design + review;
   ทดสอบบน co6 ที่ติดสภาพจริงอยู่แล้ว = acceptance test ในตัว)
5. **WAVE 5 — O4** หน้าแก้ใบเบิก + **O5** ภ.พ.36 PDF (FE/report ธรรมดา)

## ข้อจำกัดที่ต้องเคารพ
- 7-day quota 85% ตอนวางแผน → ทำเป็น wave ไม่รวดเดียว, checkpoint ทุก wave, Codex/AGY ช่วยได้ (pool แยก)
- Payroll/SSO = money + กฎหมาย → Opus design + Opus review เสมอ, ห้าม Fable เขียนโค้ดเอง
- co6 งวดปิดหมด (ใช้เทส O14 ได้ดี) · co7 งวดเปิด (ใช้เทส payroll/SSO) · co5 = สนาม VAT
- ทุก wave: full suite เป็น gate (ไม่ใช่ --filter), Fable อ่าน diff ก่อน commit, deploy + verify สด
