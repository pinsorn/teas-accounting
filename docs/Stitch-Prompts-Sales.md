# Stitch Prompts — TEAS Sales pages

**Stitch project:** [Thai E-Tax Document System](https://stitch.withgoogle.com/projects/10440553618447039589)

**Done:**
- ☑ Tax Invoice (ใบกำกับภาษี) detail — generated 2026-05-21 by Sana

**To paste manually in Stitch chat input** (Sana's Chrome MCP couldn't hit Stitch shadow DOM):
1. Quotation detail
2. Sales Order detail
3. Delivery Order detail
4. Receipt detail
5. Credit Note detail
6. Debit Note detail
7. Billing Note detail

Each prompt paste → wait Stitch generate → repeat. Keep same project (continues design system + sidebar consistency).

---

## 1. Quotation (ใบเสนอราคา) detail

```
Now design the matching Quotation (ใบเสนอราคา) detail page with the same
design system + sidebar. Document number 05-2026-QT-ECOM-0001, status
"ตอบรับแล้ว Accepted" (green check badge). Header: company logo + seller info
left, "ใบเสนอราคา / QUOTATION" title right + doc no + issue date 20/05/2026
+ valid-until date 19/06/2026 (ยืนราคาถึง). Customer block: บริษัท แอคมี
จำกัด Tax ID 0-1055-56123-45-3 branch 00000. Line items table: ตู้เลี้ยงปลา
ขนาดกลาง qty 1 unit หน่วย, B3,500.00, discount 0%, B3,745.00 (with VAT).
Summary: มูลค่าก่อนภาษี B3,500 / ส่วนลด B0 / ภาษีมูลค่าเพิ่ม B245 / รวมทั้งสิ้น
B3,745. Notes section: ลูกค้านิติบุคคลหัก ณ ที่จ่าย 3% เฉพาะส่วนบริการ. Top
right action bar: ดาวน์โหลด PDF / Resend Email / Print. Action buttons under
status: "แปลงเป็นใบสั่งขาย Convert to Sales Order" + "ยกเลิกใบเสนอราคา Cancel".
Cross-ref panel bottom: linked SO 05-2026-SO-ECOM-0001 (clickable chip).
NO watermark (Quotation is pre-fiscal). Same Noto Sans Thai + TH Sarabun
New font system.
```

---

## 2. Sales Order (ใบสั่งขาย) detail

```
Now design Sales Order (ใบสั่งขาย) detail. Document 05-2026-SO-ECOM-0001
status "ยืนยันแล้ว Posted" (lock icon, green). Header: same layout, title
"ใบสั่งขาย / SALES ORDER". Customer + expected delivery date (กำหนดส่ง)
21/05/2026. Line items same shape as Quotation. Summary same. Action
buttons: "สร้างใบส่งของ Create Delivery Order" + "ยกเลิก Cancel". Cross-ref
chips bottom: linked Quotation 05-2026-QT-ECOM-0001 + linked DO 05-2026-DO-
ECOM-0001 (back/forward chain). Same action bar Download PDF / Print.
```

---

## 3. Delivery Order (ใบส่งของ) detail

```
Now design Delivery Order (ใบส่งของ) detail. Document 05-2026-DO-ECOM-0001
status "ส่งมอบแล้ว Delivered" (green truck icon). 4-state machine (Draft →
Issued → Delivered → Cancelled). Title "ใบส่งของ / DELIVERY ORDER". Buyer
info same as TI customer. Special fields: ที่อยู่จัดส่ง (Ship-to address)
+ ชื่อผู้รับ (Recipient name) + ลายเซ็นผู้รับ (Recipient signature box,
empty in template). Line items: same product table but without VAT column
(DO is non-fiscal). Show only qty/unit/description. NO summary/VAT —
just total line count. Note field at bottom. Cross-ref chips: linked SO
05-2026-SO-ECOM-0001 + auto-fired TI 05-2026-TI-ECOM-0001 (Pattern X — TI
created automatically on Mark Delivered). Action bar: Print + Download PDF.
NO watermark.
```

---

## 4. Receipt (ใบเสร็จรับเงิน) detail

```
Now design Receipt (ใบเสร็จรับเงิน) detail. Document 05-2026-RC-ECOM-0001
status "บันทึกแล้ว Posted" (lock icon, green). Title "ใบเสร็จรับเงิน /
RECEIPT". Customer block same. Date locked "ล็อคเป็นวันนี้" Asia/Bangkok
21/05/2026. Body: payment method radio (เงินสด/โอนเงิน/เช็ค/บัตรเครดิต)
selected as โอนเงิน; bank info row ธนาคารไทยพาณิชย์ เลขที่ 123-4-56789-0.
Applied tax invoices table: TI doc_no / total / applied amount /
outstanding. Row: 05-2026-TI-ECOM-0001 / B3,745 / B3,745 / B0. WHT
section: toggle "ลูกค้าหัก ภาษี ณ ที่จ่าย" on; WHT type บริการ 3%, rate 3%,
base auto from SERVICE lines only, WHT amount B0, net received B3,745.
50ทวิ reference TXC-12345. Summary right: ยอดเงินรับ B3,745 / ภาษีหัก ณ
ที่จ่าย -B0 / รวมสุทธิ B3,745. Same ต้นฉบับ Original watermark. Bottom
cross-ref chip: ชำระสำหรับ 05-2026-TI-ECOM-0001 (link back). Top right
action bar: Download PDF / Download XML / Resend Email / Print.
```

---

## 5. Credit Note (ใบลดหนี้) detail

```
Now design Credit Note (ใบลดหนี้ ม.86/10) detail. Document
05-2026-CN-ECOM-0001 status "บันทึกแล้ว Posted" (lock icon, green). Title
"ใบลดหนี้ / CREDIT NOTE" + subtitle "ตามมาตรา ม.86/10 แห่งประมวลรัษฎากร".
Header same. Reference original Posted TI prominently: "อ้างอิงใบกำกับภาษี
05-2026-TI-ECOM-0001" (clickable chip). Reason code dropdown shown as
selected value: "การคืนสินค้า (TYPO / AMOUNT_ERROR / CUSTOMER_INFO /
RETURN / CANCEL — pick RETURN)". Reason text required field: "ลูกค้าคืน
สินค้าเนื่องจากชำรุดระหว่างขนส่ง — เปลี่ยนทดแทนใหม่ตามใบกำกับภาษี #002".
Adjustment amount: B3,500 (full reversal), tax rate 7%, VAT amount B245.
Summary: มูลค่าที่ปรับ B3,500 / ภาษีมูลค่าเพิ่ม B245 / รวมที่ลด B3,745.
Date locked today. ต้นฉบับ Original watermark diagonal. Cross-ref bottom:
อ้างอิง TI 05-2026-TI-ECOM-0001 + linked reissue TI 05-2026-TI-ECOM-0002
(if applicable). Action bar same: Download PDF / XML / Resend / Print.
```

---

## 6. Debit Note (ใบเพิ่มหนี้) detail

```
Now design Debit Note (ใบเพิ่มหนี้ ม.86/9) detail. Document
05-2026-DN-ECOM-0001 status "บันทึกแล้ว Posted". Title "ใบเพิ่มหนี้ / DEBIT
NOTE" + subtitle "ตามมาตรา ม.86/9 แห่งประมวลรัษฎากร". Reference original
Posted TI prominently: 05-2026-TI-ECOM-0001 (clickable chip). Reason code:
PRICE_INCREASE / QTY_INCREASE / NEW_ITEM. Reason text: "ปรับราคาเพิ่ม
เนื่องจากค่าจัดส่งเพิ่มเติม". Adjustment amount: B500 (increase), VAT B35,
total +B535. Summary: มูลค่าที่ปรับเพิ่ม B500 / ภาษีมูลค่าเพิ่ม B35 /
รวมที่เพิ่ม B535. Same ต้นฉบับ Original watermark. Same action bar
Download PDF / XML / Resend / Print. Date locked today.
```

---

## 7. Billing Note (ใบแจ้งหนี้) detail

```
Now design Billing Note (ใบแจ้งหนี้ / ใบวางบิล) detail. Document
05-2026-BL-ECOM-0001 status "ออกแล้ว Issued" (blue send icon). 4-state
machine (Draft → Issued → Settled → Cancelled). Title "ใบแจ้งหนี้ /
BILLING NOTE / ใบวางบิล". Header same layout. Special fields: วันที่ครบกำหนด
(Due date) 19/06/2026 (highlighted in orange if overdue). Customer
block. Multi-TI table: "รายการใบกำกับภาษีที่อ้างอิง" — multiple TIs grouped
into one bill: TI doc_no / TI date / TI total / amount due. Rows:
05-2026-TI-ECOM-0001 / 20/05/2026 / B3,745 / B3,745. Sum row: รวมยอดเรียก
เก็บ B3,745. Notes section (terms): "กรุณาชำระภายในกำหนดเพื่อหลีกเลี่ยงค่า
ปรับ. โอนเข้าบัญชี ธนาคารไทยพาณิชย์ 123-4-56789-0". Action buttons under
status: "ยืนยันชำระครบแล้ว Mark Settled" + "ยกเลิก Cancel". Cross-ref chips
bottom: linked TIs (multiple). NO compliance watermark (BN is
pre-fiscal — collection trigger only). Same action bar Download PDF /
Print / Resend Email. NO XML download (BN is not e-Tax).
```

---

## Process notes

- Stitch may hallucinate numbers (Sana saw Subtotal 5,500 instead of 3,500 in TI). User reviews + corrects as visual reference only.
- Each prompt = ~30 seconds Stitch generate time.
- Use Stitch's left-panel "Agent log" to check what design system tokens were applied.
- Export → download Stitch designs as PNG for Sprint 13j R1 reference + share with Claude Code as PDF layout target.

## Sprint 13j R1 input

These 8 Stitch designs (1 done + 7 to generate) = visual brief for Sprint 13j R1 (Print/PDF revamp shared QuestPDF template). Compare Stitch's compliance-style layout vs current TEAS Sprint 13e era PDF → Claude Code implements QuestPDF 3-section template matching Stitch's structure.

Watermarks: ต้นฉบับ + สำเนา = need to add 2nd page variant per Ham's spec. Stitch may suggest these on prompt.
