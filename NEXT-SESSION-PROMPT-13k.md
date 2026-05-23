# NEXT-SESSION-PROMPT — Sprint 13k (after 13j-FE post-ship polish, cont. 62)

Paste this as the first message of the next session.

---

อ่าน `CLAUDE.md` → `progress.md` (cont. 62 บนสุด) → `plan.md` ก่อนเริ่ม.

## 0. Environment setup (ทำก่อนทุกครั้ง — หายเมื่อ session resume)
1. `subst` drives หายเมื่อ resume — สร้างใหม่:
   - `subst U: <repo>\code` , `subst W: <repo>\code\backend` (ถ้ายังไม่มี)
2. **BE ต้องรันด้วย** `ASPNETCORE_ENVIRONMENT=Development` + `ASPNETCORE_URLS=http://localhost:5080`
   (Production → JWT signing key null → login 500). FE `next dev`/`build` รันจาก **native path ไม่ใช่ U:**.
3. DEV login: `admin / Admin@1234` (company 1, SUPER_ADMIN) · `demo-admin / Demo@1234` (company 2).
4. รายละเอียด env เพิ่มเติม: memory `teas-dev-run`.

## 1. RUN `/graphify` FIRST
Knowledge graph เก่า (ก่อน cont. 61–62). cont. 62 เพิ่ม: `app/(dashboard)/customers/*`,
`components/paper/*`, `components/doc/*`, print-tracking BE (`PrintTrackingService`,
`PrintEndpoints`, migration `AddPrintTracking`), `ReceiptService.SetWhtCertAsync`,
`LineItemsTable` (VAT dropdown). **Regenerate graph แล้ว query แทนการอ่านไฟล์ทั้งต้นไม้**
(CLAUDE.md §17).

## 2. งานหลัก (priority order)

### 2.1 ★ §4.8 audit-log writes (compliance — สูงสุด)
ตอนนี้ `audit.activity_log` ถูกเขียนแค่ ApiKey + print (cont. 62). **ทุก state change ของ
sales doctypes ยังไม่ log** → `ActivityLog` timeline ในหน้า detail ว่างเปล่า (Question-Backend15).
- เพิ่ม activity-log write ในทุก command handler: Quotation (create/send/accept/reject/convert/cancel),
  SalesOrder (create/post/createDo), DeliveryOrder (create/issue/mark-delivered/create-ti),
  TaxInvoice (create/post/void/resend), Receipt (create/post/void), AdjustmentNote CN/DN (create/post/void),
  BillingNote (create/issue/mark-settled/cancel).
- EntityType ต้องตรง endpoint route mapping ที่มีอยู่: `Quotation/SalesOrder/DeliveryOrder/TaxInvoice/
  Receipt/CreditNote/DebitNote/BillingNote` (ดู `ActivityEndpoints.cs` Docs[] + `PrintTrackingService`).
- DTO ที่ FE คาด: `ActivityEntryDto {actor, action, fromStatus, toStatus, at, note}` (มี endpoint
  `GET /{docType}/{id}/activity` พร้อมแล้ว — แค่ต้องมี rows). action ใช้ verb เช่น Created/Posted/
  Accepted/Converted/Delivered/Cancelled/Issued; from/toStatus ใส่ใน metadata หรือ field.
- ⚠️ แตะ posting/immutability paths → CLAUDE.md §9 careful. มี test ครอบ.

### 2.2 รายงาน "ใบเสร็จที่ขาดใบทวิ 50" (ภ.ง.ด.)
- query receipts ที่ `wht_amount > 0 && customer_wht_cert_no IS NULL` (status posted).
- BE report endpoint + FE หน้า (วางใต้ Reports หรือ Tax filings — ยืนยันกับ Ham).
- คอลัมน์เสนอ: docNo, ลูกค้า, วันที่, ยอด WHT, งวด. filter งวดเดือน.
- เชื่อมหน้า receipt detail (`ReceiptWhtCertSection` มีปุ่มใส่ทีหลังแล้ว).

### 2.3 งานรอง
- WHT type select (receipt) → `FloatingListbox` (custom dropdown, เหมือน CustomerSelector) — ตอนนี้
  เป็น DaisyUI `<select>` (เข้าธีมแล้วแต่ native dropdown).
- Logo จริง: `public/teas-logo.png` = `teas-mascot.png` (ไฟล์เดียวกันจาก TEAS.zip). รอ Ham ส่งโลโก้จริง.
- Purchase module + Settings restyle (out-of-scope 13j-FE per Answer-29) — Ham กำหนด sprint.

### 2.4 13j-PDF (แยก, รอ Sana)
QuestPDF mirror ของ `PaperDocument` (`components/paper/types.ts` PaperDocumentProps §C4 LOCKED +
`lib/paper.css` geometry 1:1). Sana เขียน `docs/paper-document-spec.md` ก่อน. ตอนนี้พิมพ์ใช้
browser print (Save as PDF) ตรง UI ใหม่.

## 3. Verify gate ทุก PR
FE `tsc --noEmit` 0 · `next build` 0 (native path) · `dotnet build` 0/0 · `dotnet test` (Domain ≥89) ·
hex-grep components/app = 0 · mirror Y:\AccountApp · prepend progress.md cont. NN · tick plan.md ·
**run /graphify** ถ้าเพิ่ม/ย้ายไฟล์เยอะ.

---
Handoff จาก cont. 62 (2026-05-22). Thanks 🙏
