# Stitch Prompts — TEAS Sales LIST pages (main menu)

**Stitch project:** [Thai E-Tax Document System](https://stitch.withgoogle.com/projects/10440553618447039589)

Companion file to `docs/Stitch-Prompts-Sales.md` (detail pages). This file covers the **8 list pages** = main menu entry per sales sub-module.

**Paste each prompt sequentially in Stitch chat. Same project — keeps sidebar + design system + Thai typography consistent.**

Shared list-page requirements (all 8):
- Same left sidebar with Thai labels (`แดชบอร์ด / ใบเสนอราคา / ใบสั่งขาย / ใบส่งของ / ใบกำกับภาษี / ใบแจ้งหนี้ / ใบเสร็จรับเงิน / ใบลดหนี้ / ใบเพิ่มหนี้`) — selected item per page.
- Top: page title (Thai + English subtitle) + "+ สร้าง..." button right.
- Filter bar row: `สถานะ` dropdown + `หน่วยธุรกิจ` BU dropdown + `ลูกค้า` async combobox + `วันที่ตั้งแต่ / ถึง` date range. URL-persisted (`?status=&bu=&customerId=&dateFrom=&dateTo=`).
- Data table headers Thai. Each row has StatusBadge (Thai label + icon + color). Click row → detail.
- Action column on the right per row: contextual link — Draft state shows "แก้ไข", non-Draft "เปิด".
- Empty state below table: "ไม่มีข้อมูล" centered + small icon.
- Bottom: pagination (cursor-based per Plan §20.8 — show "ก่อนหน้า / ถัดไป" buttons + page indicator).
- Font: Noto Sans Thai for UI. Numbers tabular-nums.

---

## 1. /quotations — ใบเสนอราคา list

```
Now design the Quotation (ใบเสนอราคา) list page. Sidebar "ใบเสนอราคา"
selected. Title "ใบเสนอราคา / QUOTATIONS" + "+ สร้างใบเสนอราคา" button top
right. Filter bar: สถานะ dropdown (ทั้งหมด/ร่าง Draft/ส่งแล้ว Sent/ตอบรับแล้ว
Accepted/ปฏิเสธ Rejected/หมดอายุ Expired/แปลงแล้ว Converted/ยกเลิก Cancelled) +
หน่วยธุรกิจ BU dropdown + ลูกค้า async combobox + วันที่ตั้งแต่ + วันที่ถึง.
URL-persisted ?status=Sent&bu=ECOM&customerId=5&dateFrom=2026-05-01&dateTo=
2026-05-31. Data table columns: เลขที่ (font-mono) / สถานะ (StatusBadge with
Thai label + Send/CheckCircle2/Ban/XCircle/ArrowRightCircle icon) / ลูกค้า /
วันที่ (Thai BE format "20 พ.ค. 2569") / ยืนราคาถึง (valid-until date,
highlight orange if expired) / รวม (tabular-nums, B-prefix) / action link
("แก้ไข" only when Draft, else "เปิด"). Sample 5 rows: 05-2026-QT-ECOM-0001
Accepted บริษัท แอคมี จำกัด 20 พ.ค. 2569 19 มิ.ย. 2569 B3,745.00 / 05-2026-
QT-ECOM-0002 Sent ลูกค้าทดสอบ จำกัด 21 พ.ค. 2569 20 มิ.ย. 2569 B3,210.00 /
two Draft + one Cancelled. Pagination bottom: "ก่อนหน้า / 1 / ถัดไป". Empty
state placeholder if no data: "ยังไม่มีใบเสนอราคา — เริ่มต้นสร้างใบแรก".
Compact responsive 3-row table density. Same Noto Sans Thai font.
```

---

## 2. /sales-orders — ใบสั่งขาย list

```
Now design Sales Order (ใบสั่งขาย) list page. Sidebar "ใบสั่งขาย" selected.
Title "ใบสั่งขาย / SALES ORDERS" + "+ สร้างใบสั่งขาย" button. Filter bar:
status dropdown (ทั้งหมด/ร่าง Draft/ยืนยันแล้ว Posted/ปิด Closed/ยกเลิก
Cancelled) + BU + customer + date range, same URL persistence pattern.
Table columns: เลขที่ / สถานะ / ลูกค้า / วันที่ / กำหนดส่ง (expected delivery
date) / รวม / action link. Sample 3 rows: 05-2026-SO-ECOM-0001 Posted
บริษัท แอคมี จำกัด 20 พ.ค. 2569 28 พ.ค. 2569 B3,745.00 ; one Draft + one
Closed (fulfilled green). Pagination + empty state same pattern.
```

---

## 3. /delivery-orders — ใบส่งของ list

```
Now design Delivery Order (ใบส่งของ) list. Sidebar "ใบส่งของ" selected. Title
"ใบส่งของ / DELIVERY ORDERS" + "+ สร้างใบส่งของ" button. 4-state machine
filter: ทั้งหมด/ร่าง Draft/ออกแล้ว Issued/ส่งมอบแล้ว Delivered/ยกเลิก
Cancelled + BU + customer + date range. Table columns: เลขที่ / สถานะ
(Issued=blue Send icon, Delivered=green Truck icon, Cancelled=red XCircle) /
ลูกค้า / วันที่ / ผู้รับ (recipient name) / TI ที่เชื่อม (clickable chip with
linked TI doc_no, blank for Draft/Issued) / action link. Sample 3 rows:
05-2026-DO-ECOM-0001 Delivered บริษัท แอคมี จำกัด 20 พ.ค. 2569 คุณสมชาย
(linked TI chip 05-2026-TI-ECOM-0001) ; one Issued (no TI yet) + one
Cancelled. No "รวม" column (DO is non-fiscal). Pagination + empty state.
```

---

## 4. /tax-invoices — ใบกำกับภาษี list (priority page — most-used)

```
Now design Tax Invoice (ใบกำกับภาษี) list page. Sidebar "ใบกำกับภาษี"
selected. Title "ใบกำกับภาษี / TAX INVOICES" + "+ สร้างใบกำกับภาษี" button
top right. Filter bar row: สถานะ dropdown (ทั้งหมด/ร่าง Draft/บันทึกแล้ว
Posted/ยกเลิก Voided) + หน่วยธุรกิจ BU dropdown + ลูกค้า async combobox (with
search) + วันที่ตั้งแต่ + ถึง + advanced filter expander row showing
"e-Tax สถานะ" (ทั้งหมด/ส่งแล้ว Sent/รอส่ง Pending/ส่งล้มเหลว Failed) +
"การชำระเงิน" (ทั้งหมด/PAID/PARTIAL/UNPAID). URL-persisted.

Data table columns: เลขที่ (font-mono, e.g. 05-2026-TI-ECOM-0001) / สถานะ
(StatusBadge Posted lock icon green) / ลูกค้า + Tax ID below in small text /
วันที่ออก (Thai BE) / มูลค่ารวม (tabular-nums) / VAT / e-Tax (small chip:
green check "ส่ง RD แล้ว" or yellow clock "รอ" or red X "ล้มเหลว") /
การชำระเงิน chip (green "ชำระครบ" / orange "ชำระบางส่วน" / red "ค้างชำระ") /
action link "เปิด".

Sample 4 rows: 05-2026-TI-ECOM-0001 Posted (lock) "บริษัท แอคมี จำกัด /
0-1055-56123-45-3" 20 พ.ค. 2569 B3,745.00 B245.00 ส่ง RD แล้ว ชำระครบ ;
05-2026-TI-LAB-0001 Posted "บริษัท แล็บ จำกัด" 18 พ.ค. 2569 B12,500
B875 ส่ง RD แล้ว ชำระบางส่วน ; one Voided strikethrough ; one Draft no
e-Tax chip yet.

Below table small summary stat strip: "เดือนนี้ 12 ใบ • ยอดรวม B89,250 •
VAT B6,247 • รอชำระ B15,500" (4 inline mini stats).

Pagination "ก่อนหน้า / 1 2 3 / ถัดไป" + cursor-based. Empty state "ยังไม่มี
ใบกำกับภาษี — สร้างใบแรกของคุณ".
```

---

## 5. /receipts — ใบเสร็จรับเงิน list

```
Now design Receipt (ใบเสร็จรับเงิน) list page. Sidebar "ใบเสร็จรับเงิน"
selected. Title "ใบเสร็จรับเงิน / RECEIPTS" + "+ สร้างใบเสร็จ" button. Filter
bar: สถานะ (ทั้งหมด/ร่าง/บันทึกแล้ว Posted/ยกเลิก) + BU + ลูกค้า + วันที่
ตั้งแต่/ถึง + payment method filter (ทั้งหมด/เงินสด/โอนเงิน/เช็ค/บัตรเครดิต).
URL-persisted.

Table columns: เลขที่ (e.g. 05-2026-RC-ECOM-0001) / สถานะ / ลูกค้า /
วันที่ / วิธีชำระ (chip: เงินสด/โอนเงิน/เช็ค/บัตรเครดิต with icon) /
ยอดเงิน (tabular-nums) / WHT (orange chip "หัก B105" if applied, else "-") /
TI ที่ชำระ (small chip linking applied TI doc_no, or "หลายใบ" if multiple) /
action link "เปิด".

Sample 3 rows: 05-2026-RC-ECOM-0001 Posted "บริษัท แอคมี จำกัด" 21 พ.ค.
2569 โอนเงิน B3,745.00 - 05-2026-TI-ECOM-0001 ; 05-2026-RC-LAB-0001 Posted
"บริษัท แล็บ" 20 พ.ค. 2569 เงินสด B12,500 หัก B375 (3% บริการ)
05-2026-TI-LAB-0001 ; one Draft.

Bottom mini stats: "เดือนนี้ 18 ใบ • รวมรับ B125,400 • WHT รับ B3,762".
Pagination + empty state.
```

---

## 6. /credit-notes — ใบลดหนี้ list

```
Now design Credit Note (ใบลดหนี้ ม.86/10) list page. Sidebar "ใบลดหนี้"
selected. Title "ใบลดหนี้ / CREDIT NOTES" + subtitle "ตามมาตรา ม.86/10
แห่งประมวลรัษฎากร" + "+ สร้างใบลดหนี้" button. Filter bar: สถานะ (ทั้งหมด/
ร่าง/บันทึกแล้ว Posted/Applied) + BU + customer + date range + เหตุผล
dropdown (ทั้งหมด/TYPO/AMOUNT_ERROR/CUSTOMER_INFO/RETURN/CANCEL). URL
persistence.

Table columns: เลขที่ (e.g. 05-2026-CN-ECOM-0001) / สถานะ / ลูกค้า / วันที่ /
เหตุผล (chip color-coded — return=red, typo=orange, customer_info=blue,
amount_error=yellow, cancel=gray) / มูลค่าที่ลด (negative tabular-nums,
red color) / TI ต้นฉบับ (chip linking original TI doc_no) / action link.

Sample 2 rows: 05-2026-CN-ECOM-0001 Posted "บริษัท แอคมี จำกัด" 21 พ.ค.
2569 RETURN -B3,745 05-2026-TI-ECOM-0001 ; one Draft.

Bottom mini stats: "เดือนนี้ 3 ใบ • รวมลดหนี้ -B8,250 • Output VAT คืน
-B577". Pagination + empty state.
```

---

## 7. /debit-notes — ใบเพิ่มหนี้ list

```
Now design Debit Note (ใบเพิ่มหนี้ ม.86/9) list. Sidebar "ใบเพิ่มหนี้"
selected. Title "ใบเพิ่มหนี้ / DEBIT NOTES" + subtitle "ตามมาตรา ม.86/9
แห่งประมวลรัษฎากร" + "+ สร้างใบเพิ่มหนี้" button. Filter bar: สถานะ + BU +
customer + date range + เหตุผล dropdown (PRICE_INCREASE/QTY_INCREASE/
NEW_ITEM).

Columns: เลขที่ (05-2026-DN-ECOM-0001) / สถานะ / ลูกค้า / วันที่ / เหตุผล
(chip — PRICE_INCREASE green, QTY_INCREASE blue, NEW_ITEM purple) /
มูลค่าที่เพิ่ม (positive tabular-nums, green color) / TI ต้นฉบับ chip /
action link.

Sample row: 05-2026-DN-ECOM-0001 Posted "บริษัท แอคมี" 21 พ.ค. 2569
PRICE_INCREASE +B500 05-2026-TI-ECOM-0001. Bottom stats + pagination +
empty state.
```

---

## 8. /billing-notes — ใบแจ้งหนี้ list

```
Now design Billing Note (ใบแจ้งหนี้ / ใบวางบิล) list page. Sidebar
"ใบแจ้งหนี้" selected. Title "ใบแจ้งหนี้ / BILLING NOTES" + "+ สร้างใบแจ้งหนี้"
button. 4-state filter: ทั้งหมด/ร่าง Draft/ออกแล้ว Issued/ชำระครบแล้ว Settled/
ยกเลิก Cancelled + BU + customer + date range + due-date filter dropdown
(ทั้งหมด/ครบกำหนดแล้ว Overdue/ใน 7 วัน/ใน 30 วัน).

Columns: เลขที่ (05-2026-BL-ECOM-0001) / สถานะ (StatusBadge Settled=green
CheckCheck, Issued=blue Send, Cancelled=red XCircle) / ลูกค้า / วันที่ออก /
วันครบกำหนด (highlight orange-red if overdue, badge "เลยกำหนด N วัน" inline)
/ จำนวน TI (small chip "3 ใบ" showing count of grouped tax_invoices) / รวม
ยอดเรียกเก็บ (tabular-nums) / ยอดค้างชำระ (computed = total - settled,
red if > 0) / action link.

Sample 3 rows: 05-2026-BL-ECOM-0001 Issued "บริษัท แอคมี จำกัด" 21 พ.ค.
2569 ครบ 19 มิ.ย. 2569 1 ใบ B1,605 B1,605 (outstanding) ; one Settled
green checkmark "ชำระครบ" B0 outstanding ; one Cancelled.

Bottom mini stats: "ค้างชำระทั้งหมด 5 ใบ • รวม B85,000 • เลยกำหนด 2 ใบ
(B12,500)". Pagination + empty state.
```

---

## Process notes

- Each prompt = ~30 seconds Stitch generate.
- Use Stitch's right-side "Export" → PNG for Sprint 13j R1 visual brief.
- Combine detail (8) + list (8) = 16 sales screens total. Then prompt for shared sidebar component variation if needed.
- After sales pages complete → consider Purchase pages (vendor invoices, payment vouchers, purchase orders) + Reports module + Settings as future Stitch project.

## Sprint 13j R1 input

These 8 list designs + 8 detail designs = full visual brief for Sprint 13j R1 (Print/PDF revamp + List page styling refinement). Claude Code uses Stitch designs as the **target layout** for list refactors + QuestPDF template.
