# Sprint 13e — Sales forms fix + build

**Owner**: Claude Code
**Spec author**: Sana (live-tested chapter 3 forms via Chrome MCP, 2026-05-19)
**Sequencing**: Run AFTER Sprint 13d completes (touches overlapping FE files —
shared components from 13d like AlertDialog + PermissionGate will be used here)
**ROI**: 3-5 days

---

## Background

Sprint 13b chapter 3 live capture พบ sales document forms อยู่ในสภาพ
ไม่ consistent. ทดสอบจริงผ่าน Chrome MCP ทุกหน้า /quotations/new,
/sales-orders/new, /delivery-orders/new, /tax-invoices/new,
/receipts/new, /credit-notes/new, /debit-notes/new — เจอ 3 routes พัง,
1 form เป็น MVP skeleton, และ shared UX bug ใน RC/CN/DN.

นี่คือ blocker ของ chapter 3 manual — เขียน end-to-end sales cycle
walkthrough ไม่ได้ตอนนี้.

---

## Live audit results (verified 2026-05-19)

| Route | Status | Network evidence |
|---|---|---|
| `/tax-invoices/new` | ✅ Production-ready | TI form fully built, 200 OK |
| `/credit-notes/new` | ✅ Production-ready | CN form built, มี ม.86/10 ref |
| `/debit-notes/new` | ✅ Production-ready | DN form built, มี ม.86/9 ref |
| `/receipts/new` | 🟡 Works, UX bug | RC form built, taxInvoiceId raw number |
| `/quotations/new` | 🚨 MVP stub | 1 line only, no products, no VAT, no BU |
| `/sales-orders/new` | 🚨 BROKEN | GET `/api/proxy/sales-orders/NaN` → 404, RSC → 503 |
| `/delivery-orders/new` | 🚨 BROKEN | GET `/api/proxy/delivery-orders/NaN` → 404 |

Routing bug root cause: chunks for `[id]/page.js` loaded สำหรับ URL `/new`
— ยืนยันว่าไฟล์ `new/page.tsx` หายไป. Compare กับ DN ที่ทำงาน:
```
✅ /_next/.../debit-notes/new/page.js     (loaded for /debit-notes/new)
🚨 /_next/.../sales-orders/[id]/page.js   (loaded for /sales-orders/new)
🚨 /_next/.../delivery-orders/[id]/page.js (loaded for /delivery-orders/new)
```

---

## P1 — Emergency: Fix SO + DO routing (P0 priority)

### Problem
`/sales-orders/new` และ `/delivery-orders/new` ถูก match กับ dynamic
`[id]/page.tsx` route → "new" parse เป็น `id` → `parseInt("new") = NaN`
→ fetch `/api/proxy/{resource}/NaN` → 404 → page stuck "กำลังโหลด..."

User เห็นแค่ loading spinner ตลอด — ไม่มี error, ไม่มี form. โหลด
หนึ่งล้านปีก็ไม่ขึ้นอะไร.

### Fix
สร้าง 2 files (ใช้ pattern เดียวกับ `tax-invoices/new/page.tsx`):

```
frontend/app/(dashboard)/sales-orders/new/page.tsx
frontend/app/(dashboard)/delivery-orders/new/page.tsx
```

ทั้ง 2 ไฟล์เรียก component form ใหม่ที่จะ build ใน P4. ในระยะ
emergency: stub กับ heading "สร้างใบสั่งขาย" / "สร้างใบส่งของ" + ข้อความ
"กำลังพัฒนา — ใช้ POST /api/v1/sales-orders โดยตรงสำหรับ external
integration" + link กลับ list page.

ถ้า P4 ทำพร้อมกัน → ข้าม stub, ใส่ form จริงเลย.

### Acceptance
- GET `/sales-orders/new` → renders ทันที (ไม่ 404)
- GET `/delivery-orders/new` → renders ทันที
- Network: chunks load `new/page.js` (ไม่ใช่ `[id]/page.js`)

---

## P2 — Quotation form rebuild

### Problem
ปัจจุบัน `/quotations/new`:
- **1 line item อย่างเดียว** — `รายละเอียด` เป็น textbox อิสระ ไม่ผูก products
- **ไม่มี VAT preview** — ไม่มี subtotal/tax/total breakdown
- **ไม่มี BU selector** — ขัดกับ enforce-BU toggle ใน /settings/business-units
- **1 ปุ่ม "บันทึก" เดียว** — ไม่มี Draft/Issue workflow

ใช้สำหรับ business จริงไม่ได้ — Q ปกติเป็น multi-line + product picker
+ VAT preview + terms & notes.

### Fix — match TI form pattern พร้อม Q-specific fields

**Fields** (เหมือน TI ตาม `/tax-invoices/new` ที่ build ไว้แล้ว):
- ลูกค้า * (customer search combobox)
- วันที่ * (date — default today, แต่ Q แก้ได้ ไม่ lock เหมือน TI)
- **ยืนราคาถึง * (date — default = +30 วัน)** [Q-specific]
- หน่วยธุรกิจ * (BU dropdown — required ถ้า enforce-BU on, optional otherwise)
- รายการ (multi-line table):
  - รายละเอียด (textbox + product picker via dropdown OR autocomplete —
    เลือก product → pre-fill รายละเอียด + ราคา + อัตราภาษี)
  - จำนวน (number)
  - หน่วยนับ (text — pre-fill จาก product's uom)
  - ราคา/หน่วย (number — pre-fill จาก product's defaultUnitPrice)
  - อัตราภาษี (number — 0 ถ้า product type = EXEMPT_*, 0.07 ถ้าอื่น)
  - รายการรวม (computed: qty × price)
  - [remove line button]
- "+ เพิ่มรายการ" button (above table)
- **หมายเหตุ (textarea — optional)** [Q-specific: payment terms, conditions]
- Subtotal / VAT / **Discount (Q-specific)** / Total breakdown

**Actions** (2 buttons เหมือน TI):
- "บันทึกร่าง" (Save as Draft — status=Draft, แก้ไขต่อได้, สร้างเลขเอกสาร
  แล้วแต่ไม่ post)
- **"ออกใบเสนอราคา" (Issue — status=Issued, ส่งให้ลูกค้าได้, ยังแก้ไขได้
  จนกว่า status จะเป็น Accepted/Rejected)**

**Status machine** [Q-specific]:
```
Draft → Issued → Accepted → (กลายเป็น SO via convert action)
                ↓
              Rejected (end state)
```

### Files
- `frontend/app/(dashboard)/quotations/new/page.tsx` (rebuild)
- `frontend/components/forms/QuotationForm.tsx` (new — shared กับ
  /quotations/[id]/edit)
- `frontend/components/forms/ProductPicker.tsx` (new — autocomplete
  search products by name/SKU, ใช้ใน Q + SO + DO + TI)
- `frontend/components/forms/LineItemsTable.tsx` (new — shared
  multi-line table, refactor TI form ก็ใช้ได้)
- `backend/.../Api/Controllers/QuotationsController.cs` — เพิ่ม status
  transitions (Issue, Accept, Reject, ConvertToSO)
- `backend/.../Models/Quotation.cs` — เพิ่ม validUntil, notes, discount,
  status enum

### Acceptance
- สร้าง Q พร้อม 3 line items (1 GOOD, 1 SERVICE, 1 EXEMPT_GOOD) →
  VAT ถูกคำนวณตามประเภท (GOOD+SERVICE × 7%, EXEMPT_GOOD = 0)
- BU dropdown required เมื่อ enforce-BU toggle on
- "บันทึกร่าง" → status=Draft, list page แสดง
- "ออกใบเสนอราคา" → status=Issued, สามารถ "ConvertToSO" จาก detail page
- E2E test: full Q → SO conversion flow

---

## P3 — Shared TaxInvoicePicker component

### Problem
RC, CN, DN ทั้ง 3 forms ต้องอ้างอิง TI เดิม. ปัจจุบันทุกหน้าใช้ raw
number input:
```html
<label>อ้างอิงใบกำกับภาษีเดิม *</label>
<input type="number" value="0" />
```
User ต้อง:
1. เปิดหน้า /tax-invoices ในแท็บใหม่
2. หา TI ที่ต้องการ
3. Copy `taxInvoiceId` (internal numeric ID — ไม่ใช่เลขเอกสารที่ visible)
4. Paste กลับ form

UX แย่มาก + error-prone (พิมพ์เลขผิด → อ้าง TI ผิด → audit หาเรื่อง).

### Fix — `<TaxInvoicePicker>` component
- Combobox/autocomplete: ค้นจาก **เลขเอกสาร** (เช่น "05-2026-TI-ECOM-0001"),
  **ลูกค้า**, หรือ **ยอดรวม**
- แสดง preview: เลขเอกสาร · ลูกค้า · วันที่ · ยอดรวม
- Filter ตาม:
  - context (CN/DN: filter เฉพาะ TI ที่ status=Posted, ไม่ใช่ Draft)
  - customer (ถ้า user เลือกลูกค้าก่อน → filter เฉพาะ TI ของลูกค้านั้น)
  - amount remaining (RC: filter เฉพาะ TI ที่ยัง unpaid)
- รับค่า value=taxInvoiceId, callback onChange(taxInvoice)
- Disabled state: เมื่อยังไม่เลือกลูกค้า (ถ้าใช้ใน RC)

### Files
- `frontend/components/forms/TaxInvoicePicker.tsx` (new)
- Update: `/receipts/new`, `/credit-notes/new`, `/debit-notes/new`
- BE: `GET /api/v1/tax-invoices/search?customer={id}&status=posted&unpaid=true`
  — ถ้ายังไม่มี (น่าจะมีอยู่แล้วใน list endpoint แต่ต้องรองรับ filters)

### Acceptance
- /receipts/new: เลือกลูกค้า → TI picker เปิดได้ → ค้น "0001" → เลือก →
  taxInvoiceId กรอกอัตโนมัติ + จำนวนเงินตั้งต้นเป็น TI total
- /credit-notes/new: TI picker filter เฉพาะ status=posted
- E2E test: search + select flow

---

## P4 — Build SO + DO forms (after P1 routing fix)

### SO (Sales Order) form

**Purpose**: Internal commitment เมื่อลูกค้า accept Q. SO เป็น
"confirmed Q" — ใช้สำหรับ planning fulfillment + linking ไป DO/TI.

**Fields** (เหมือน Q ตาม P2):
- ลูกค้า * + วันที่ * + กำหนดส่ง * + หน่วยธุรกิจ
- **อ้างอิงใบเสนอราคา (optional — ผูกกับ Q ถ้า convert มา)**
- รายการ multi-line (same product picker, VAT calc)
- หมายเหตุ
- Subtotal / VAT / Total

**Actions**: บันทึกร่าง / **ยืนยันสั่งขาย (Confirmed)**

**Status machine**:
```
Draft → Confirmed → Fulfilled (เมื่อมี DO/TI อ้างอิงครบ — auto)
                  ↓
                Cancelled
```

### DO (Delivery Order) form

**Purpose**: เอกสารส่งของ. ไม่ใช่เอกสารทางการเงิน — ไม่มี VAT/total.

**Fields** (เรียบง่ายกว่า SO/Q):
- ลูกค้า * + วันที่ส่ง * + หน่วยธุรกิจ
- **อ้างอิงใบสั่งขาย หรือใบกำกับภาษี (optional)** — เผื่อ DO เป็น
  standalone กรณีไม่ผ่าน SO workflow
- รายการ multi-line: รายละเอียด + จำนวน + หน่วยนับ (ไม่มีราคา!)
- **ที่อยู่จัดส่ง * (textarea)** — pre-fill จาก customer's default
  address แต่แก้ได้
- **ผู้รับสินค้า + ลายเซ็น (textarea + future: e-signature)**
- หมายเหตุ

**Actions**: บันทึกร่าง / **ออกใบส่งของ (Issued — พิมพ์ได้)**

**Status machine**:
```
Draft → Issued → Delivered (เมื่อ user mark received)
                ↓
              Cancelled
```

### Files
- `frontend/app/(dashboard)/sales-orders/new/page.tsx` (build, replace P1 stub)
- `frontend/app/(dashboard)/sales-orders/[id]/page.tsx` (existing — review)
- `frontend/components/forms/SalesOrderForm.tsx` (new)
- `frontend/app/(dashboard)/delivery-orders/new/page.tsx` (build, replace P1 stub)
- `frontend/components/forms/DeliveryOrderForm.tsx` (new — simpler than SO)
- BE: SO + DO controllers (อาจมีอยู่แล้ว — verify endpoint shapes)
- BE: Status transition endpoints

### Acceptance
- /sales-orders/new → form ขึ้นมา 200ms, save ได้, status workflow
  ทำงานครบ
- /delivery-orders/new → form ขึ้นมา, save ได้, ไม่มีต้องการ price
- E2E test: Q → SO → DO chain (convert จาก Q → SO, แล้ว SO → DO)

---

## P5 — Cross-cutting: Status badges + state machine UI

หลัง P2-P4 ทุก document type จะมี multi-status workflow:
- Q: Draft / Issued / Accepted / Rejected / Converted
- SO: Draft / Confirmed / Fulfilled / Cancelled
- DO: Draft / Issued / Delivered / Cancelled
- TI: Draft / Posted / Voided (existing — keep as-is)
- RC, CN, DN: Draft / Posted (existing)

### Fix
สร้าง shared `<DocumentStatusBadge>` component — display status พร้อม:
- สี (green=success, blue=info, gray=draft, red=error/voided)
- icon (✓ posted, ✏️ draft, ↺ converted, ✗ cancelled)
- tooltip ภาษาไทย

ใน list table + detail page top-right แสดง badge.

### Files
- `frontend/components/ui/DocumentStatusBadge.tsx` (new)
- Update list pages: /quotations, /sales-orders, /delivery-orders,
  /tax-invoices, /receipts, /credit-notes, /debit-notes — column สถานะ
  ใช้ badge
- Update detail pages: header

---

## Out of scope (Phase 2)

- e-signature on DO (digital signature workflow)
- Quote PDF export / email to customer
- Quote → Tax Invoice direct conversion (skip SO/DO)
- Partial fulfillment (1 SO → multiple DO)
- Reverse workflow (TI → DO if delivery happened after billing)
- Q version history (re-issue revised Q)

---

## Sana owns (update AFTER merge)
- `docs/accounting-system-plan.md` — เพิ่ม §X "Sales document workflow"
  พร้อม status state machines
- `docs/api/openapi.yaml` — Q + SO + DO status transition endpoints
- `docs/manual/chapters/03-การขาย.md` (new — Sana จะเขียน walkthrough
  03.01-03.07 หลัง merge)
- `frontend/manual/walkthroughs/03.*` (Sana จะเขียน)

---

## Test plan

### Unit/Integration (BE)
- Q status transitions (each valid → next, invalid → rejected)
- SO/DO status transitions
- Q → SO conversion (preserves line items, BU, customer)
- VAT calculation: GOOD vs SERVICE vs EXEMPT_GOOD vs EXEMPT_SERVICE

### E2E (Playwright)
- `chapter3_q_to_so_to_do_chain.spec.ts` — full sales cycle
- `chapter3_ti_picker_search.spec.ts` — picker in RC/CN/DN
- `chapter3_so_routing_fix.spec.ts` — /sales-orders/new renders
  (regression test for SO bug)
- `chapter3_do_routing_fix.spec.ts` — /delivery-orders/new renders

### Sana acceptance (after merge)
ผมจะ re-run chapter 3 walkthroughs ผ่าน Chrome MCP — เขียน 7 walkthroughs:
- 03.01 ใบเสนอราคา (Q create + issue)
- 03.02 ใบสั่งขาย (SO from Q convert)
- 03.03 ใบส่งของ (DO from SO)
- 03.04 ใบกำกับภาษี (TI standalone หรือจาก SO)
- 03.05 ใบเสร็จรับเงิน (RC ที่อ้าง TI ผ่าน picker)
- 03.06 ใบลดหนี้ (CN ม.86/10)
- 03.07 ใบเพิ่มหนี้ (DN ม.86/9)

---

## Sequencing within Sprint 13e

Recommended order (1 dev):
1. **Day 1**: P1 (emergency routing fix — 1-2 hours) + P3 (TaxInvoicePicker)
2. **Day 2-3**: P2 (Q rebuild — biggest piece)
3. **Day 3-4**: P4 (SO + DO forms — reuse Q form components)
4. **Day 5**: P5 (status badges) + E2E tests + Sana acceptance

If 2 devs parallel:
- Dev A: P1 → P2 → P5
- Dev B: P3 → P4

---

## Dependencies on Sprint 13d

- `<PermissionGate>` from 13d-P3 — used in this sprint สำหรับ hide
  Issue/Post buttons เมื่อ user ไม่มี scope
- `<AlertDialog>` from 13d-P1 — used for "ยกเลิก document" confirms
- Error envelope v1 from 13d-P5 — sales form fields ใช้ field-level
  validation errors

→ **Don't start 13e P2/P4 until 13d-P1/P3/P5 merged** (P1+P3 ของ
sprint นี้ทำได้ก่อน — ไม่กระทบ component สำคัญ).

---

## File ownership reminder

Same as Sprint 13d:
- Claude Code edits: source, tests, migrations
- Sana owns (provide proposed text in Report-Backend22.md):
  - `CLAUDE.md`, `docs/accounting-system-plan.md`,
  - `docs/runtime-gotchas.md`, `docs/api/openapi.yaml`
  - `frontend/manual/**`, `docs/manual/**`

---

## Reporting back

`Report-Backend22.md` — รวมทุก P, breaking changes (โดยเฉพาะถ้า Q form
แตกต่างจาก stub เดิมจน existing Q records ใน DB ใช้ไม่ได้ —
migration needed), test results.

Sana จะ re-test ทั้ง chapter 1-3 หลัง 13d + 13e merged (ตาม instruction
จาก Ham — "เดี๋ยวจะบอกตอน Claude Code เสร็จ").
