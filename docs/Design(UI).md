# UI Design Specification
## Thailand Enterprise Accounting System

**Version:** 1.0  
**Parent Doc:** `accounting-system-plan.md` v1.4  
**Target Users:** Accountant, AR/AP Clerk, Sales Staff, Approver, Admin  

---

## สารบัญ

1. Design Principles
2. Design System & Tokens
3. Layout & Navigation
4. Authentication Screens
5. Dashboard
6. Master Data Screens
7. Sales Module Screens
8. Purchase Module Screens
9. Tax & Reporting Screens
10. ภ.พ.30 Submission Workflow (Auto & Manual Modes)
11. API Key Management
12. Settings (Read-only View of Env Config)
13. Audit & Logs
14. Empty / Error / Loading States
15. Mobile & Responsive
16. Accessibility (WCAG 2.1 AA)
17. Localization

---

## 1. Design Principles

| Principle | Rationale |
|---|---|
| **Compliance-visible** | UI ต้องทำให้ user เห็นชัดว่าตอนนี้ระบบใน mode ไหน (VAT/Non-VAT), submission mode ไหน — ป้องกัน confusion |
| **Immutable transparency** | Posted documents แสดง "🔒 Posted" ชัดเจน + แสดง action ที่ทำได้ (Credit Note / Resend) ในที่เดียว |
| **Bilingual TH/EN** | ทุก label, button, error message รองรับ 2 ภาษา toggle ที่ user level |
| **Keyboard-first** | Accountant ใช้ keyboard เยอะ — รองรับ shortcut: `Ctrl+S` save, `Ctrl+P` post, `/` focus search |
| **Fast list views** | Lists ต้อง paginate + filter + sort ทำงานเร็ว — primary view ของ accountant |
| **Critical confirmation** | Action ที่ irreversible (Post, Submit ภ.พ.30) ต้องมี explicit confirm + summary preview |
| **Print-aware** | ทุก document detail page มี "Print Preview" + "Download PDF" ปุ่มเห็นชัด |

---

## 2. Design System & Tokens

### 2.1 Color Tokens

```css
/* Primary */
--color-primary-50:  #E3F2FD;
--color-primary-500: #1976D2;   /* main brand */
--color-primary-700: #1565C0;

/* Semantic */
--color-success:     #2E7D32;   /* posted, paid */
--color-warning:     #F57C00;   /* draft, pending */
--color-danger:      #C62828;   /* error, voided */
--color-info:        #0288D1;
--color-muted:       #757575;

/* Status colors */
--status-draft:      #9E9E9E;
--status-pending:    #FB8C00;
--status-posted:     #43A047;
--status-paid:       #1E88E5;
--status-voided:     #E53935;
--status-cancelled:  #757575;

/* Neutrals */
--color-bg:          #FAFAFA;
--color-surface:     #FFFFFF;
--color-border:      #E0E0E0;
--color-text:        #212121;
--color-text-muted:  #616161;
```

### 2.2 Typography

```css
--font-th: 'TH Sarabun New', 'Sarabun', sans-serif;
--font-en: 'Inter', 'Roboto', sans-serif;
--font-mono: 'JetBrains Mono', 'Consolas', monospace;

--text-xs:  12px;   /* tax id, meta */
--text-sm:  14px;   /* table cells */
--text-base: 16px;  /* body */
--text-lg:  18px;   /* form labels */
--text-xl:  24px;   /* section headers */
--text-2xl: 32px;   /* page title */
--text-3xl: 40px;   /* dashboard hero */
```

### 2.3 Spacing & Layout

```css
--space-1: 4px;
--space-2: 8px;
--space-3: 12px;
--space-4: 16px;
--space-6: 24px;
--space-8: 32px;
--space-12: 48px;

--container-max: 1440px;
--sidebar-w: 240px;
--header-h: 64px;
```

### 2.4 Status Badges

| Status | Badge Style |
|---|---|
| Draft | gray bg, dashed border |
| Submitted | yellow bg solid |
| Posted | green bg solid + 🔒 icon |
| e-Tax Sent | green bg + ✉ icon |
| Acknowledged | dark green + ✓ icon |
| Rejected | red bg + ⚠ icon |
| Voided | strikethrough + red text |
| Cancelled | gray + line-through |

---

## 3. Layout & Navigation

### 3.1 Global Layout

```
┌─────────────────────────────────────────────────────────────────┐
│ Header (64px)                                                   │
│ [Logo] [Search ──/──]              [VAT_MODE] [Bell] [User▼]    │
├──────────┬──────────────────────────────────────────────────────┤
│          │                                                      │
│ Sidebar  │  Main Content Area                                   │
│ (240px)  │                                                      │
│          │  [Breadcrumb]                                        │
│ - Home   │  [Page Title]                          [Actions]     │
│ - Sales  │                                                      │
│ - Purch  │  [Filter bar / Search]                               │
│ - Reports│  ┌──────────────────────────────────────────────┐    │
│ - Tax    │  │ Table / Form / Detail                        │    │
│ - Master │  │                                              │    │
│ - Admin  │  └──────────────────────────────────────────────┘    │
│          │  [Pagination]                                        │
└──────────┴──────────────────────────────────────────────────────┘
```

### 3.2 Header Component

- **Logo** — link to dashboard
- **Global search** — search invoices, customers, products (Cmd/Ctrl+K shortcut)
- **VAT_MODE indicator** — แสดง chip "VAT 7%" หรือ "NON-VAT" ตาม env config (read-only)
- **Notification bell** — count of pending tasks (e-Tax rejected, ภ.พ.30 alert)
- **User menu** — profile, language toggle (TH/EN), logout

### 3.3 Sidebar Menu

```
🏠 Dashboard
📄 Sales
   ├ Quotations            (05-2026-QT-*)
   ├ Sales Orders          (05-2026-SO-*)
   ├ Delivery Orders       (05-2026-DO-*)
   ├ Tax Invoices          (05-2026-TI-*) ★
   ├ Receipts              (05-2026-RC-*)
   ├ Credit Notes          (05-2026-CN-*) ★
   ├ Debit Notes           (05-2026-DN-*) ★
   ├ Billing Notes         (05-2026-BN-*)
   └ Customer Receipts
🛒 Purchases
   ├ Purchase Requests
   ├ Purchase Orders
   ├ Vendor Invoices
   ├ Payment Vouchers
   └ WHT Certificates (50 ทวิ)
👥 Master Data
   ├ Customers
   ├ Vendors
   ├ Products (SKU)
   └ Chart of Accounts
📊 Reports
   ├ Financial Statements (P&L, BS, CF)
   ├ Trial Balance
   ├ AR/AP Aging
   ├ VAT Output Register
   ├ VAT Input Register
   └ Custom Reports
🧾 Tax
   ├ ภ.พ.30 (VAT Return)
   ├ ภ.ง.ด.3 / 53 (WHT)
   ├ ภ.ง.ด.50 / 51 (CIT)
   └ ภ.พ.36
🔧 Admin
   ├ Users & Roles
   ├ Document Prefixes
   ├ API Keys
   ├ System Config (read-only)
   └ Audit Log

★ = e-Tax submitted to RD
```

Menu items hidden ตาม `VAT_MODE`:
- ถ้า `VAT_MODE=false` → ซ่อน Tax Invoices, Credit Note, Debit Note, VAT registers, ภ.พ.30, ภ.พ.36 menu

---

## 4. Authentication Screens

### 4.1 Login

- Email + Password fields
- "Remember me" checkbox
- "Forgot password?" link
- After login → 2FA challenge (TOTP code from authenticator app)
- After 5 failed attempts → account locked 15 min

### 4.2 2FA Setup (first login)

- QR code for authenticator
- Manual key fallback
- Backup codes (10 codes, single-use)

### 4.3 Password Policy

- ≥ 12 chars, 1 upper + 1 number + 1 special
- Expire 90 days
- No reuse of last 5 passwords

---

## 5. Dashboard

### 5.1 Above the Fold (Hero Section)

```
┌─────────────────────────────────────────────────────────────┐
│  สวัสดี, [ชื่อ User]                                       │
│  วันนี้ 15 พฤษภาคม 2026                                    │
│                                                            │
│  ┌──────────┬──────────┬──────────┬──────────┐            │
│  │ ขายเดือน │ ภาษีขาย  │ AR คงค้าง│ Pending  │            │
│  │ ฿1.2M    │ ฿84k     │ ฿340k    │ 12 docs  │            │
│  └──────────┴──────────┴──────────┴──────────┘            │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 Pending Tasks Widget

แสดง action items ที่ user role นั้น ๆ ต้องทำ:

- **Accountant:** Draft ภ.พ.30 รอ review, e-Tax rejected ต้องแก้
- **AR Clerk:** Overdue invoices ต้อง follow up
- **Approver:** PO/PV/Credit Note pending approval
- **Admin:** API key ใกล้หมดอายุ

### 5.3 Quick Actions

```
[+ Quotation]  [+ Tax Invoice]  [+ Customer]  [Generate ภ.พ.30]
```

### 5.4 Recent Activity Feed

Timeline แสดง activity 50 รายการล่าสุด (filter by date/type/user)

---

## 6. Master Data Screens

### 6.1 Customer List

```
┌─────────────────────────────────────────────────────────────────┐
│ ลูกค้า (Customers)                              [+ เพิ่มลูกค้า]  │
├─────────────────────────────────────────────────────────────────┤
│ [Search: ชื่อ/Tax ID/รหัส]   [Filter: ทั้งหมด ▼] [Export CSV]   │
├─────────────────────────────────────────────────────────────────┤
│ □ │ รหัส   │ ชื่อ            │ Tax ID       │ VAT │ Credit  │   │
│ □ │ C00001 │ บริษัท ABC จก. │ 0105556... สห│ ✓   │ ฿100k   │ ⋮ │
│ □ │ C00002 │ ลูกค้าทั่วไป   │ -            │ -   │ -       │ ⋮ │
│   │        │                 │              │     │         │   │
└─────────────────────────────────────────────────────────────────┘
                                              < 1 2 3 ... 12 >
```

**Columns:** Code, Name, Tax ID, Branch, VAT Status, Credit Limit, Payment Term, Last Transaction  
**Bulk actions:** Activate/Deactivate, Export, Merge duplicates

### 6.2 Customer Detail / Edit

- Tabs: Info, Addresses (multiple), Transactions, Documents, Notes
- Tax ID input → auto-validate checksum on blur (red border if invalid)
- Branch code dropdown (00000, 00001, ...)
- Add multiple shipping addresses
- Transactions tab = all docs related to this customer

### 6.3 Product (SKU) List

> หมายเหตุ: ระบบไม่จัดการ inventory — แสดงเฉพาะ SKU + description

```
┌────────────────────────────────────────────────────────────────┐
│ สินค้า/บริการ (Products & Services)              [+ เพิ่ม]      │
├────────────────────────────────────────────────────────────────┤
│ [Search]  [Type: All ▼]  [Category: All ▼]                     │
├────────────────────────────────────────────────────────────────┤
│ SKU      │ Name TH        │ Type    │ UoM │ List Price │ Tax  │
│ SKU-001  │ บริการ A       │ Service │ -   │ 5,000.00   │ VAT7 │
│ SKU-002  │ สินค้า B       │ Good    │ Pcs │ 1,200.00   │ VAT7 │
└────────────────────────────────────────────────────────────────┘
```

### 6.4 Chart of Accounts

- Tree view (expandable)
- Filter by type (Asset/Liability/Equity/Revenue/Expense)
- Edit account code, name, parent — but ห้ามแก้ของบัญชีที่มี transaction
- Bulk import from CSV (initial setup)

---

## 7. Sales Module Screens

### 7.1 Quotation List

```
┌─────────────────────────────────────────────────────────────────┐
│ ใบเสนอราคา                                       [+ สร้างใหม่]   │
├─────────────────────────────────────────────────────────────────┤
│ [Date range] [Status ▼] [Customer ▼] [Search]                  │
├─────────────────────────────────────────────────────────────────┤
│ เลขที่           │ วันที่    │ ลูกค้า      │ ยอด     │ สถานะ      │
│ 05-2026-QT-0042│ 15/05/26│ ABC Co.     │ ฿10,700 │ ✉ Sent     │
│ 05-2026-QT-0041│ 14/05/26│ XYZ Ltd.    │ ฿53,500 │ ✓ Accepted │
│ 05-2026-QT-0040│ 14/05/26│ ลูกค้าทั่วไป│ ฿1,070  │ Draft      │
└─────────────────────────────────────────────────────────────────┘
```

**Row actions:** View, Edit (Draft only), Send, Convert to SO, Revise (v2), Cancel, Duplicate

### 7.2 Quotation Create / Edit Form

```
┌─────────────────────────────────────────────────────────────────┐
│ ใบเสนอราคาใหม่                              [Save Draft] [Submit]│
├─────────────────────────────────────────────────────────────────┤
│ เลขที่: 05-2026-QT-???? (auto)        วันที่: 15/05/2026 (today)│
│ หมดอายุ: [03/06/2026] (default +14 วัน)                        │
│                                                                 │
│ ── ลูกค้า ──                                                    │
│ [🔍 ค้นหาลูกค้า...]  หรือ  [+ เพิ่มลูกค้าใหม่]                 │
│ ┌─────────────────────────────────────────────────────────┐    │
│ │ ABC Company Limited                                      │    │
│ │ Tax ID: 0105556123456  สาขา: 00000                      │    │
│ │ ที่อยู่: 123 ถ.สุขุมวิท แขวง.. เขต.. กทม.10110          │    │
│ └─────────────────────────────────────────────────────────┘    │
│                                                                 │
│ ── รายการสินค้า/บริการ ──                                       │
│ # │ SKU/Item       │ Description    │ Qty │ UoM │ Price  │ ... │
│ 1 │ [🔍 ค้นหา...]   │ ...            │ 2   │ Pcs │ 5,000  │ ... │
│ 2 │ ...                                                         │
│ [+ เพิ่มรายการ]                                                 │
│                                                                 │
│ ── สรุป ──                              ยอดก่อนภาษี: ฿10,000.00│
│                                          ส่วนลด: ฿0.00         │
│                                          VAT 7%:  ฿700.00      │
│                                          ─────────────────      │
│                                          รวม:     ฿10,700.00   │
│                                                                 │
│ ── เงื่อนไข ── (textarea)                                       │
│ ── หมายเหตุ ── (textarea)                                       │
└─────────────────────────────────────────────────────────────────┘
```

**Behavior:**
- Customer search debounced 300ms, autocomplete dropdown
- Add line item → SKU search → auto-fill description, price, default tax
- Tax calculation real-time as user types
- VAT field hidden if `VAT_MODE=false`
- "Save Draft" → save with status=Draft (no number issued)
- "Submit" → assign number + status=Sent + send email PDF

### 7.3 Quotation Detail / Print View

- Top: status badge + actions
- Body: rendered Quotation document (close to print preview)
- Right sidebar: timeline (created, sent, accepted), activity log
- Actions: Edit, Send, Accept, Reject, Convert to SO, Revise, Cancel, Duplicate, Print, Download PDF

### 7.4 Sales Order Screen

Similar to Quotation but:
- Optional reference to Quotation (auto-populate from quotation)
- Customer PO field (เลขใบสั่งซื้อของลูกค้า)
- Expected delivery date
- Status flow: Draft → Confirmed → Delivered → Closed

### 7.5 Delivery Order Screen

```
┌─────────────────────────────────────────────────────────────────┐
│ ใบส่งของ                                                        │
├─────────────────────────────────────────────────────────────────┤
│ เลขที่: 05-2026-DO-0023      วันที่: 15/05/26                  │
│ อ้างอิง SO: 05-2026-SO-0019                                    │
│                                                                 │
│ ผู้รับสินค้า: ABC Co.                                          │
│ ที่อยู่จัดส่ง: 999 ถ.พระราม 9 ...                              │
│                                                                 │
│ ── ผู้จัดส่ง ──                                                 │
│ ผู้ขนส่ง: บริษัท ขนส่ง XYZ                                     │
│ ทะเบียนรถ: 1กก-1234                                            │
│ คนขับ: นายสมชาย                                                │
│                                                                 │
│ ── รายการสินค้า ──                                              │
│ # │ SKU       │ ชื่อ              │ จำนวน │ หน่วย              │
│ 1 │ SKU-001   │ สินค้า A          │ 5     │ ชิ้น               │
│                                                                 │
│ ── ลายเซ็นผู้รับ ── (image upload หลังจัดส่ง)                  │
│                                                                 │
│ [Save Draft] [Post (Mark Delivered)]                           │
└─────────────────────────────────────────────────────────────────┘
```

**Post action:** mark `delivered_at`, lock from edit
**No stock impact** (inventory out of scope)

### 7.6 Tax Invoice Screen ★

```
┌─────────────────────────────────────────────────────────────────┐
│ ใบกำกับภาษี                                  [🔒 Posted]        │
│ 05-2026-TI-0078                                                 │
├─────────────────────────────────────────────────────────────────┤
│ ┌─ Status Bar ─────────────────────────────────────┐           │
│ │ ✓ Posted   ✓ e-Tax Sent   ⏳ Awaiting Ack       │           │
│ │ Sent to: customer@abc.com, csemail@rd.go.th    │           │
│ └──────────────────────────────────────────────────┘           │
│                                                                 │
│ [รายละเอียดใบกำกับภาษีตามรูปแบบมาตรฐาน...]                     │
│                                                                 │
│ ── Actions ──                                                   │
│ [📄 Download PDF] [📋 Download XML] [✉ Resend Email]           │
│ [🔄 Create Credit Note] [🔄 Create Debit Note]                  │
│                                                                 │
│ ── Timeline ──                                                  │
│ • 15/05/26 14:32 — Posted by Sales Clerk                       │
│ • 15/05/26 14:32 — XML signed (cert: company-signing)          │
│ • 15/05/26 14:32 — Email sent + cc RD                          │
│ • 15/05/26 14:35 — RD acknowledged (ref: ACK-XXX)              │
└─────────────────────────────────────────────────────────────────┘
```

**Posted = immutable** — ไม่มีปุ่ม Edit/Delete  
แก้ไขใด ๆ → กด "Create Credit Note" หรือ "Create Debit Note"

### 7.7 Tax Invoice Create — UX flow

```
[+ New Tax Invoice]
    ↓
[Select customer (or new)]
    ↓ ถ้า B2B + จด VAT → require Tax ID + branch code
    ↓ ถ้า B2C → name + address (Tax ID optional)
    ↓
[Add items, quantities, prices]
    ↓ tax calculation real-time
    ↓
[Review summary]
    ↓
[Click "Post"]
    ↓
[Confirmation modal]
    ┌──────────────────────────────────────┐
    │ ⚠ Confirm Post Tax Invoice           │
    │                                      │
    │ Number: 05-2026-TI-0079              │
    │ Customer: ABC Co.                    │
    │ Total: ฿10,700.00                    │
    │                                      │
    │ ⚠ After posting:                     │
    │  • Number assigned (immutable)       │
    │  • XML signed + email sent to:       │
    │    - customer@abc.com                │
    │    - csemail@rd.go.th (RD)           │
    │  • To correct → must issue           │
    │    Credit Note                       │
    │                                      │
    │ [Cancel]              [Confirm Post] │
    └──────────────────────────────────────┘
    ↓ confirm
[Posting...] spinner
    ↓
[Success → redirect to detail page]
```

### 7.8 Credit Note Screen

```
┌─────────────────────────────────────────────────────────────────┐
│ ใบลดหนี้                                                        │
├─────────────────────────────────────────────────────────────────┤
│ อ้างอิงใบกำกับภาษีเดิม:                                         │
│ [🔍 05-2026-TI-0078 — ABC Co. ฿10,700]                          │
│                                                                 │
│ เหตุผล (บังคับ): [ลดราคาสินค้า ▼]                              │
│ [TYPO / AMOUNT_ERROR / CUSTOMER_INFO / RETURN / PRICE_REDUCE]   │
│                                                                 │
│ รายละเอียดเหตุผล: (textarea, required)                         │
│                                                                 │
│ ── รายการที่ลด ──                                               │
│ # │ Item    │ จำนวนเดิม │ จำนวนใหม่ │ ผลต่าง   │              │
│ 1 │ SKU-001 │ 2 × 5,000 │ 1 × 5,000 │ -5,000   │              │
│                                                                 │
│ ── สรุป ──                                                      │
│   ยอดเดิม:   ฿10,700.00                                         │
│   ยอดใหม่:   ฿5,350.00                                          │
│   ผลต่าง:    -฿5,000.00 (+ VAT -฿350.00 = -฿5,350.00)          │
│                                                                 │
│ [Save Draft]              [Post (sends to RD + customer)]      │
└─────────────────────────────────────────────────────────────────┘
```

### 7.9 Customer Receipt (รับเงิน)

- Select customer
- List of unpaid TI (multi-select)
- Apply amount per invoice
- Payment method: Cash / Transfer / Cheque / Credit Card
- If Cheque: cheque no, bank, date
- WHT deducted by customer (ถ้าลูกค้าหัก WHT จากเรา) → field รับ WHT cert ของลูกค้า
- Auto-post JE: Dr.Cash/Bank Cr.AR

---

## 8. Purchase Module Screens

Similar structure to Sales but reversed direction. Key screens:

### 8.1 Vendor Invoice Entry (key for input VAT)

```
┌─────────────────────────────────────────────────────────────────┐
│ ใบกำกับภาษีซื้อ (Vendor Invoice)                                │
├─────────────────────────────────────────────────────────────────┤
│ เลขที่ในระบบ: VI-05-2026-0023  (auto)                          │
│ วันที่บันทึก: 15/05/26                                          │
│                                                                 │
│ ── ข้อมูลจากใบกำกับภาษีของผู้ขาย ──                            │
│ ผู้ขาย: [🔍 ค้นหา vendor...]                                    │
│ Vendor Tax ID: 0105... สาขา: 00000  (auto-fill จาก vendor)     │
│ เลขที่ใบกำกับของผู้ขาย: [INV-2026-12345]                       │
│ วันที่ใบกำกับของผู้ขาย: [10/05/2026]                            │
│ Tax Period (รอบที่ใช้สิทธิ์): [05/2026] (default = ปัจจุบัน)   │
│                                                                 │
│ ── รายการ ──                                                    │
│ # │ Item     │ Qty │ Price    │ Tax     │ Recoverable? │       │
│ 1 │ บริการ A │ 1   │ 5,000.00 │ VAT 7%  │ ✓            │       │
│                                                                 │
│ ── WHT (ถ้ามี) ──                                               │
│ Type: [ค่าบริการ 3% ▼]   Base: 5,000   WHT: 150.00            │
│                                                                 │
│ ── สรุป ──                                                      │
│   ราคา:        5,000.00                                         │
│   VAT 7%:        350.00                                         │
│   รวม:         5,350.00                                         │
│   หัก WHT:      -150.00                                         │
│   ────────────────────                                          │
│   จ่ายสุทธิ:    5,200.00                                        │
│                                                                 │
│ ── Upload ใบกำกับภาษี (PDF/image) ── (drag-drop area)          │
│                                                                 │
│ [Save Draft] [Post]                                            │
└─────────────────────────────────────────────────────────────────┘
```

**Validation:**
- Vendor Tax ID checksum
- Tax period within 6 months from invoice date (กฎ recoverable VAT 6 เดือน)
- ถ้า "Recoverable = ❌" → flag เป็นภาษีซื้อต้องห้าม, ไม่ลงในรายงานภาษีซื้อ

### 8.2 Payment Voucher

- Select vendor invoices to pay (multi)
- Apply amounts
- Auto-calculate WHT per invoice based on vendor default WHT type
- Generate 50 ทวิ พร้อมจ่าย (linked PDF/printable)
- Auto-post JE: Dr.AP/Expense Cr.Cash/Bank Cr.WHT Payable

### 8.3 WHT Certificate (50 ทวิ) Print View

ตาม format มาตรฐานของกรมสรรพากร (PDF template)

---

## 9. Tax & Reporting Screens

### 9.1 VAT Output Register (รายงานภาษีขาย)

```
┌─────────────────────────────────────────────────────────────────┐
│ รายงานภาษีขาย                                                   │
│ เดือน: [พฤษภาคม 2026 ▼]    [📥 Export Excel] [📄 PDF]          │
├─────────────────────────────────────────────────────────────────┤
│ ลำดับ│ วันที่   │ เลขที่ใบ      │ ลูกค้า    │ Tax ID  │ ยอด    │ VAT  │
│   1  │ 02/05/26│ 05-2026-TI-001│ ABC Co.   │ 0105... │ 10,000│  700│
│   2  │ 03/05/26│ 05-2026-TI-002│ XYZ Ltd.  │ 0107... │  5,000│  350│
│  ... │  ...    │ ...           │ ...       │ ...     │  ...  │ ... │
│ Total                                                  ฿xxx,xxx │
│                                                       VAT ฿xx,xxx│
└─────────────────────────────────────────────────────────────────┘
```

### 9.2 VAT Input Register — same pattern, different columns

### 9.3 ภ.พ.30 Workflow Screen

ดู Section 10 ข้างล่าง

---

## 10. ภ.พ.30 Submission Workflow

### 10.1 Auto Mode UI (PND30_SUBMISSION_MODE=auto)

```
┌─────────────────────────────────────────────────────────────────┐
│ ภ.พ.30 รายเดือน — Auto Mode                                     │
├─────────────────────────────────────────────────────────────────┤
│ เดือน: พฤษภาคม 2026                Deadline: 15 มิถุนายน 2026 │
│                                                                 │
│ ┌─ Status: DRAFT ───────────────────────────────────┐         │
│ │ Generated: 01/06/2026 08:00 (auto)                │         │
│ │ Reviewed by: -                                    │         │
│ └────────────────────────────────────────────────────┘         │
│                                                                 │
│ ── สรุปยอด ──                                                   │
│   ยอดขายรวม:                ฿xxx,xxx                            │
│   ยอดได้รับยกเว้น:              0                                │
│   ยอดอัตรา 0%:                  0                                │
│   ยอดที่ต้องเสียภาษี:        ฿xxx,xxx                            │
│   ภาษีขาย:                  ฿xx,xxx                              │
│                                                                 │
│   ยอดซื้อรวม:                ฿xxx,xxx                            │
│   ภาษีซื้อ:                  ฿xx,xxx                              │
│                                                                 │
│   ────────────────────                                          │
│   ภาษีที่ต้องชำระ:          ฿x,xxx                                │
│                                                                 │
│ ── Reconciliation ──                                            │
│   ✓ ยอดตรงกับ Output VAT Register                             │
│   ✓ ยอดตรงกับ Input VAT Register                              │
│   ✓ ยอดตรงกับ GL accounts                                     │
│   ⚠ มีใบกำกับภาษี 2 ใบยังไม่ได้รับ ack จาก RD                  │
│      [ดูรายละเอียด]                                            │
│                                                                 │
│ [ดูรายงานละเอียด] [แก้ไข Manual Adjustment] [Submit ภ.พ.30]    │
└─────────────────────────────────────────────────────────────────┘
```

**On Submit click:**
1. Confirm modal แสดง summary + warning
2. Call RD Open API
3. Show progress
4. Display filing_reference + payment QR code (if amount > 0)
5. Status → SUBMITTED

**Auto-submit safety net (23:00 of 15th):**
- ถ้า status ยังเป็น DRAFT — ระบบ auto-submit
- Email accountant + admin "Auto-submitted at 23:00 — please verify"
- Banner ที่ dashboard "⚠ ภ.พ.30 ถูก auto-submit แล้ว ตรวจสอบด่วน"

### 10.2 Manual Mode UI (PND30_SUBMISSION_MODE=manual)

```
┌─────────────────────────────────────────────────────────────────┐
│ ภ.พ.30 รายเดือน — Manual Mode                                   │
├─────────────────────────────────────────────────────────────────┤
│ [Same summary as Auto Mode]                                     │
│                                                                 │
│ ── Submission Workflow ──                                       │
│                                                                 │
│ Step 1: Review & Confirm                                        │
│   [Mark as Reviewed]                                            │
│                                                                 │
│ Step 2: Generate File                                           │
│   [📥 Generate ภ.พ.30 File] → download .xml                    │
│                                                                 │
│ Step 3: Manual Upload to RD                                     │
│   Go to: https://efiling.rd.go.th                              │
│   Upload file + submit                                          │
│   [🔗 Open RD Portal]                                           │
│                                                                 │
│ Step 4: Record Filing Reference                                 │
│   หลังจาก submit ที่ portal RD แล้ว ใส่เลขอ้างอิง:             │
│   [Filing Reference: __________________]                        │
│   [Mark as Filed]                                               │
│                                                                 │
│ ── Status ── DRAFT (not yet submitted)                          │
└─────────────────────────────────────────────────────────────────┘
```

**No auto-submit** — full manual control  
**Deadline alerts** still send (email accountant 3, 2, 1 day before)

---

## 11. API Key Management

```
┌─────────────────────────────────────────────────────────────────┐
│ API Keys                                          [+ Create Key] │
├─────────────────────────────────────────────────────────────────┤
│ Name                │ Scope          │ Created  │ Last Used │ ⋮ │
│ Shopify Webhook     │ tax_invoice.*  │ 01/03/26 │ 5 mins ago│ ⋮ │
│ Internal CRM        │ *.read         │ 15/04/26 │ 1 hr ago  │ ⋮ │
│ Mobile App          │ quotation.*    │ 10/05/26 │ 30 min ago│ ⋮ │
└─────────────────────────────────────────────────────────────────┘
```

**Create Key form:**
- Name (required)
- Description
- Scopes (multi-select):
  - quotation.create, quotation.read, quotation.update
  - sales_order.create, ...
  - tax_invoice.create, tax_invoice.read
  - reports.read
- Expiration: 30/60/90/365 days
- Show full key ONCE after creation → user copy + save securely
- Display masked after that (`sk_abc...***...xyz`)

**Actions per key:**
- Rotate (generate new, keep old 7 days for migration)
- Revoke (immediate)
- View usage stats (calls/day, last 30 days)

---

## 12. System Config Screen (Read-only)

```
┌─────────────────────────────────────────────────────────────────┐
│ System Configuration (Read-only)                                │
│                                                                 │
│ ⚠ ค่า config ด้านล่างมาจาก environment variables ของระบบ        │
│   ห้ามแก้ผ่าน UI — ติดต่อ DevOps ถ้าต้องการเปลี่ยน              │
├─────────────────────────────────────────────────────────────────┤
│ Tax Mode                                                        │
│   VAT_MODE:           true  (จด VAT)                            │
│   VAT_RATE:           0.07  (7%)                                │
│   VAT_EFFECTIVE_FROM: 2024-10-01                                │
│                                                                 │
│ e-Tax                                                           │
│   ETAX_ENABLED:       true                                      │
│   CA_PROVIDER:        TDID                                      │
│   CA_CERT_EXPIRY:     2027-03-15 (♻ renew 30 days before)      │
│                                                                 │
│ ภ.พ.30 Submission                                               │
│   PND30_SUBMISSION_MODE: manual                                 │
│   (no API credentials required)                                 │
│                                                                 │
│ Document Numbering                                              │
│   FORMAT:             MM-YYYY-PREFIX-NNNN                       │
│   RESET_CYCLE:        monthly                                   │
│                                                                 │
│ [📥 Export config snapshot for audit]                          │
└─────────────────────────────────────────────────────────────────┘
```

---

## 13. Audit & Logs

### 13.1 Activity Log

- Filter: User, Date range, Module, Action, Entity
- Columns: Timestamp, User, IP, Action, Entity, Before/After diff (modal)
- Export CSV/JSON

### 13.2 e-Tax Submission Log

- All e-Tax submissions with status
- Filter by status: pending/sent/ack/rejected
- Click row → detail (XML preview, email log, RD response)
- Retry button for failed submissions

### 13.3 Number Gap Audit

- Per document type per month
- ❌ red flag if gap found (with reason if available)

---

## 14. Empty / Error / Loading States

### 14.1 Empty State

```
┌────────────────────────────────────┐
│                                    │
│         📋  (large icon)           │
│                                    │
│  ยังไม่มีใบเสนอราคา                │
│                                    │
│  เริ่มสร้างใบเสนอราคาแรก          │
│                                    │
│    [+ สร้างใบเสนอราคา]            │
│                                    │
└────────────────────────────────────┘
```

### 14.2 Error State

- 4xx errors: friendly message + suggested action
- 5xx errors: generic message + retry button + support contact + error reference ID
- Validation errors: inline next to field

### 14.3 Loading State

- Skeleton screens (matching final layout)
- Progressive disclosure
- Optimistic UI updates for non-critical operations

---

## 15. Mobile & Responsive

### 15.1 Breakpoints

- Mobile: < 768px (limited functionality)
- Tablet: 768-1024px (most features)
- Desktop: > 1024px (full features)

### 15.2 Mobile Priorities

- View documents
- Approve/reject pending items
- Quick view dashboard
- Capture expense receipts (photo upload → AP Invoice draft)

**Not on mobile:**
- Complex create forms (Tax Invoice with many lines)
- Reports (export only)
- Settings/Admin

---

## 16. Accessibility (WCAG 2.1 AA)

- All form fields have labels
- Color contrast 4.5:1 minimum
- Focus visible (custom focus ring)
- Keyboard navigation full coverage
- Screen reader: ARIA labels, live regions for status updates
- No critical info conveyed by color alone (icon + text + color)

---

## 17. Localization

- TH primary, EN secondary
- Language toggle persists in user profile
- Date format: DD/MM/YYYY (Buddhist era option at user level, internal storage AD)
- Currency: THB with `฿` symbol, 2 decimals, comma thousands
- Number format: 1,234,567.89

---

**— END OF UI DESIGN —**
