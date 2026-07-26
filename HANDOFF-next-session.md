# HANDOFF → session ถัดไป (เขียน 2026-07-26 เย็น)

## สถานะสั้น ๆ
- **main = `d6cce40`** (push แล้ว) · prod ยังเป็น **v1.23.0**
- **O10 + O14 เสร็จปิดสนิท** · **O2b เขียนเสร็จแต่ยังไม่ commit** · **O11 ตันรอไฟล์จาก Ham** · **O11-alt ยังไม่เริ่ม (สเปกพร้อม)**
- 7-day quota แตะ 94% ตอนหยุด → เลยพัก รอ reset (~2026-07-28 16:00 GMT+7)

## ⚠️ ทำต่อทันทีเมื่อเปิด session ใหม่ — O2b ค้างอยู่กลางทาง
โค้ด O2b **อยู่บน disk ยังไม่ commit**. ไฟล์ที่แก้:
```
M backend/src/Accounting.Application/Sales/BillingNoteDtos.cs
M backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs
M frontend/components/forms/BillingNoteForm.tsx
M frontend/messages/en.json
M frontend/messages/th.json
?? backend/tests/Accounting.Api.Tests/Sales/BillingNoteGenerateLinesO2bTests.cs   ← ไฟล์ใหม่ ต้อง git add ชื่อตรง ๆ
```
**Gate ที่ผ่านแล้ว:** `dotnet build` 0 error · billing-note tests **9/9** · Fable รีวิว diff ครบแล้ว
**Gate ที่ยังค้าง:** full Api suite (รันค้างตอนปิด ผลไม่ทันออก) · `tsc` · `next build`

ขั้นตอนกลับมาทำ:
1. รัน full suite — **ต้อง `cd` ให้ถูกก่อน** (Bash tool จำ cwd ข้ามคำสั่ง; รอบนี้เคยพังเพราะ cwd ค้างที่ `frontend/`) ใช้ absolute path:
   ```
   export TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true"
   export TEAS_REPO_ROOT="Y:/ClaudePlayground/TEAS-Project"
   dotnet test "Y:/ClaudePlayground/TEAS-Project/backend/tests/Accounting.Api.Tests" --nologo
   ```
   baseline ที่ต้องเทียบ: **973 passed / 0 failed / 9 skipped** (จาก `d6cce40`) + เทส O2b ที่เพิ่มมา
2. `tsc` แล้ว `next build` — **รันทีละตัว ห้ามพร้อม dotnet test** (รันชนกันทำ next build ตายด้วย `STATUS_DLL_INIT_FAILED` / worker EPERM มาแล้ว 2 ครั้ง)
3. เขียว → commit + push

## O2b ทำอะไรไปแล้ว (ไม่ต้องออกแบบใหม่)
สเปก `specs/billing-note-generate-lines-o2b.md` — Ham เลือก option (1): ผูกใบกำกับแล้ว generate บรรทัดให้ manual ทับได้
- กฎ generate: `TaxInvoiceIds` ไม่ว่าง **และ** `Lines` ว่าง → สร้าง 1 บรรทัด/ใบกำกับ (document granularity)
- **ยอดก๊อป verbatim จาก header ใบกำกับ** (`SubtotalAmount`/`TaxAmount`/`TotalAmount`) ห้ามส่งผ่าน path คำนวณ tax code — ยอดใบกำกับรวม VAT แล้ว คำนวณซ้ำ = VAT ซ้อน VAT
  - หมายเหตุ: TaxInvoice ใช้ชื่อ `TaxAmount` ไม่ใช่ `VatAmount` (สเปกเขียนชื่อผิด โค้ดถูก)
- `TaxCode`/`TaxCodeId`/`TaxRate` ก๊อปจากบรรทัดของใบกำกับ (code เดียวกันหมด → บรรทัดแรก; ต่างกัน → บรรทัดยอดมากสุด). **เคยเป็น `"TI"` ซึ่งผิด** — "TI" คือ DocType ไม่ใช่ tax code และ `BillingNoteLine.TaxCode` ถูก resolve จริงผ่าน `SalesLineBackstop.LoadTaxCodeFlagsAsync`
- `DescriptionTh` / `UomText` เป็นภาษาไทย (`ใบกำกับภาษี {DocNo} ลงวันที่ dd/MM/yyyy` / `ฉบับ`) — เคยเป็นอังกฤษ ซึ่งพิมพ์ลงเอกสารที่ลูกค้าได้รับ
- FE: ถอด `.min(1)` ออกจาก zod แล้วเช็คเองว่าต้องมีบรรทัด**หรือ**มีใบกำกับผูก + hint i18n ครบ 2 locale

**edge ที่ยังไม่มี guard (บันทึกไว้ ไม่ได้แก้):** ใบกำกับที่ไม่มีบรรทัดเลยจะทำให้ `.First()` บน array ว่าง → 500. ในทางปฏิบัติใบกำกับต้องมีบรรทัดเสมอ ถ้าจะกันก็เป็น guard สั้น ๆ

## เสร็จแล้ววันนี้ (อยู่บน main แล้ว)
| commit | ของ |
|---|---|
| `e62102f` | **O10-A** deduction backend — account 2180, `Cr 2180`, API draft-only, guard 2 ชั้น, seed 2 ทาง |
| `93d5ee4` | **O10-B** reason column + migration + FE + สลิป → **O10 ปิดครบ** |
| `4d71841` | **O11-D0** dump พิกัด template + พบว่า ส่วนที่ 2 ไม่มีในไฟล์ |
| `d6cce40` | **O14** reopen งวดรายเดือน (backend+FE) → co6 ที่แช่แข็งถึง 2027 แก้ได้แล้ว |

## ยังไม่ได้ deploy — 4 commit ค้างหลัง tag `v1.23.0`
`d877286` (O4/O2a/G5) · `e62102f` · `93d5ee4` · `d6cce40`
**release นี้มีทั้ง SqlScripts seed 630 และ EF migration `20260726060403` → backup prod DB บังคับ**

## เหลือทำ
1. **O2b** — จบ gate + commit (ดูข้างบน)
2. **O11-alt** — สเปกพร้อม `specs/sso-schedule-onscreen-o11alt.md` ยังไม่เริ่ม
   Ham สั่ง: O11 ไม่ต้องกรอก PDF แล้ว ให้**แสดงข้อมูลบนจอให้ user ไปกรอกเอง**
   ของดี: `SsoMonthlyModel.Lines` มีข้อมูลครบและถูกอยู่แล้ว (กรองเฉพาะผู้ประกันตน, ค่าจ้าง = ยอดจ่ายจริง prorated ตาม O8) → **ไม่ต้องคำนวณอะไรใหม่** แค่ project + endpoint + ตาราง + print CSS
   และมี `BuildMonthlyFileAsync` = ไฟล์ upload e-service อยู่แล้ว ให้โผล่เป็นปุ่มด้วย
3. **O11 ตัวจริง** — ⛔ รอ Ham เอาไฟล์ ส่วนที่ 2 PDF มาวางที่ `backend/src/Accounting.Infrastructure/Pdf/Templates/`
   `sps110_main.pdf` 4 หน้าไม่มี ส่วนที่ 2 เลย: p1 = `สปส.1-10 ส่วนที่ 1` · p2 = คำชี้แจง · **p3/p4 = `สปส.1-10/1` คนละแบบฟอร์ม** (ยื่นรวมสาขา แถวเป็นรายสาขา)
   ของที่กู้ไว้ใช้ได้ตอนได้ไฟล์: สูตรแปลงพิกัด **`yTop_json = 595.3 − Top_dump`, `x_json = Left_dump`** (A4 แนวนอน, verify กับ `wageMonth` ที่ JSON pin ไว้ 202.6 ↔ dump 392.4) และ `TaxFormFillDiagnostic.Dump_sps110_positioned_words` (`TEAS_DIAG=1`) ชี้ไปที่ไฟล์ใหม่ได้เลย
4. **deploy** 4 commit ที่ค้าง (+ O2b ถ้า commit ทัน)

## บทเรียนรอบนี้ที่ยังไม่ได้ fold ที่ไหน
- **Codex runtime ป่วยหนักทั้งวัน** — job ตายกลางทาง 5+ ครั้ง, และ 2 ครั้งไม่ได้เริ่มเลยเพราะ **job ที่ตายแล้วยังค้าง `status: running` + `pid` ที่ตายไปแล้วใน `~/.claude/plugins/data/codex-openai-codex/state/<proj>/jobs/<id>.json`** — `codex cancel` ล้างไม่ได้ (lookup คนละ index) ต้อง patch ไฟล์เอง แล้วค่อยยิงใหม่
- **poll ต้องเช็ค pid ว่ายังมีชีวิต ไม่ใช่เช็คแค่ status** — และ **log flush ช้ากว่าไฟล์จริงมาก** (เคยเห็น log ค้างที่ `dotnet build` 1.5 ชม. ทั้งที่ไฟล์ B4/B5 ลงไปแล้ว) → ตัดสินจาก `git status` + pid ไม่ใช่จาก log
- **งานที่เหลือแค่ gate ไม่ต้อง dispatch ซ้ำ** — Fable รันคำสั่งเองเร็วกว่ารอ Codex รอบ 4
- **`PdfText` ตัดวรรณยุกต์ไทย** (`อื่น` → `อื น`) ห้าม assert สตริงไทยที่มีวรรณยุกต์ (อยู่ใน troubles-wiki แล้ว)
- **random-id collision โตตาม teas_test** — เจอ fail แล้ว re-run เดี่ยวก่อนโทษ diff (อยู่ใน troubles-wiki แล้ว)
