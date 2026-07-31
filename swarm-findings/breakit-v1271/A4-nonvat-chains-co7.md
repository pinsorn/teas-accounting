# A4 — non-VAT company end-to-end (co7), prod v1.27.1

Target `https://teas.kazaki-rio.com`, **co7 = บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด (id=7)**,
`/system/info` → `{"version":"1.27.1","vat_mode":false}`. `companyId:7` re-confirmed on
`GET /api/proxy/me` before every write (nvadmin02 userId 24 / nvchief02 userId 25). All work
driven through the public domain via the BFF proxy with a curl cookie jar. No source edits,
no commits.

> **Concurrency note.** Another agent was writing expense-claim and payroll documents to co7
> during this run (EX-0001…EX-0005, PR 12-2099-PR-0001). Every finding below is tied to a
> document **I created**, except finding **H4** (the 1170 balance), which I report as an
> *observed occurrence* per the dispatch's "every 1170/2151 touch on co7 is a finding" rule and
> explicitly do **not** claim as my repro.

## Scoreboard

| Sub-area | Result |
|---|---|
| R1 Purchase PO → VI → PV, fold-into-cost + vendor paid in FULL | **PASS** |
| R1 Purchase with WHT (VAT-registered vendor, 3%) | **PASS** |
| R1 Sales QT → SO → DO → IV → RC, numbers | **PASS** |
| R1 Dr = Cr on every posting / TB balanced | **PASS** |
| R1 AP clears exactly, sub-ledger reconciles to 2110 | **PASS** |
| R1 **AR ledger for the non-VAT sales chain** | **FAIL — H1** (no AR ever created) |
| R2 Tax Invoice blocked (direct / DO→TI / BN→TI) | **PASS** (422 `ti.non_vat_blocked` ×3) |
| R2 Credit / Debit Note blocked | **PASS** (no TI can exist → 422 `note.original_missing`) |
| R2 Output VAT (2151) untouched | **PASS** (TB 2151 = 0.00) |
| R2 VAT clamp on sales docs (QT/SO/DO/BN) via raw API | **PASS** (server clamps 0.07 → 0) |
| R2 VAT clamp on **Purchase Orders** via raw API | **FAIL — M7** (server accepts 7%) |
| R2 Input VAT (1170) untouched by co7 postings | **FAIL — H4** (1170 Dr 535.00) |
| R2 Vendor / employee paid the FULL gross on every purchase path | **PASS** (no underpayment found) |
| R2 ภ.พ.30 refused on a non-VAT company | **FAIL — M5** (generates, prints, **finalizes**) |
| R2 ภ.พ.36 offered on a non-VAT company | **PASS** (correct — ม.83/6 binds non-registrants) |
| R2 Non-VAT paperwork free of VAT wording | **PASS on wording**, **FAIL on arithmetic — H3** |
| R2 SO invoiced only once | **FAIL — H2** (double-billing) |
| R2 Unhandled 500s | **FAIL — M6** (`SO → delivery-orders`) |
| R2 Duplicate running doc numbers on co7 | not observed |
| R2 >2-decimal amounts reaching the GL | **co7 shares it** (see K1) |
| R2 Voided PV printing "ต้นฉบับ" | **co7 shares it** (see K2) |
| R2 Payroll into a closed period | N/A (all 12 FY2026 periods Open on co7) |

---

# CRIT / HIGH

## H1 — HIGH: the non-VAT sales chain never creates a receivable; an issued Invoice posts no journal at all

**Severity: HIGH (accounting correctness — the whole AR side of a non-VAT company is blind).**

A non-VAT company's billing document is the **Invoice / ใบแจ้งหนี้ (BillingNote)**, which is the
`vatMode:false` branch of `SO → create-invoice` and the only output of `DO → create-invoice`.
Issuing it produces **no journal entry**, so account **1130 ลูกหนี้การค้า is never debited** and
revenue is recognised only when cash arrives. AR aging, the customer statement and the AR control
account are permanently empty for a non-VAT company no matter how much is billed and outstanding.

Repro (all live, in order):

```
POST /api/proxy/delivery-orders/17/create-invoice        → 200 {"billing_note_id":33}
POST /api/proxy/billing-notes/33/issue                   → 204
GET  /api/proxy/billing-notes/33
  {"docNo":"07-2026-IV-0001","status":"Issued","totalAmount":5000.0000}
```

At that moment — invoice issued, 5,000.00 outstanding, nothing paid:

```
GET /api/proxy/reports/ar-aging
{"asOfDate":"2026-07-31","companyId":7,"rows":[],
 "totals":{...,"total":0},
 "reconciliation":{"controlAccountCode":"1130","controlAccountBalance":0.0,
                   "subLedgerTotal":0,"difference":0.0,"balanced":true}}
```

`GET /api/proxy/journals` shows **no JE** whose reference is `07-2026-IV-0001`. Revenue appears only
later, on the receipt:

```
POST /api/proxy/receipts/34/post → 200 {"docNo":"07-2026-RC-0001","amount":5000.0000}
GET  /api/proxy/journals/299
  07-2026-JV-0017 | RC 07-2026-RC-0001
    1120 เงินฝากธนาคาร      Dr 5000.00
    4000 รายได้จากการขาย     Cr 5000.00      ("Sales (non-VAT receipt) 07-2026-RC-0001")
```

**Still true right now with a genuinely unpaid invoice on the books.** `07-2026-IV-0002`
(BillingNote 34) is `Issued`, 5,000.00, never receipted — and:

```
GET /api/proxy/reports/ar-aging               → rows: [], 1130 control balance 0.00
GET /api/proxy/reports/customer-statement?customerId=19&fromDate=2026-07-01&toDate=2026-07-31
  {"customerName":"ลูกค้าทดสอบลายเซ็น","openingBalance":0,"lines":[],
   "totalDebit":0,"totalCredit":0,"closingBalance":0}
```

A customer who was billed twice (5,000 + 5,000) and paid once (5,000) has a **completely empty
statement** and a **zero AR balance**.

**The purchase side of the same company does accrue** — which is what makes this an asymmetry
rather than a deliberate cash-basis product:

```
GET /api/proxy/reports/ap-aging
  rows:[{"vendorName":"ผู้ขายจด VAT ทดสอบ V3b","current":1070.0000,"total":1070.0000}]
  reconciliation:{"controlAccountCode":"2110","controlAccountBalance":1070.0000,
                  "subLedgerTotal":1070.0000,"difference":0.0000,"balanced":true}
```

- **Expected:** issuing an Invoice recognises revenue and a receivable (Dr 1130 / Cr 4000), the
  receipt then clears AR (Dr 1120 / Cr 1130); AR aging and the customer statement show the
  outstanding 5,000.00 — mirroring how the VI/AP side already behaves.
- **Actual:** the Invoice posts nothing; 1130 is dead; revenue is cash-basis; AR aging and the
  customer statement are structurally incapable of ever showing a non-VAT company's receivables.

---

## H2 — HIGH: double-billing — a Sales Order already invoiced through its Delivery Order can be invoiced a second time

**Severity: HIGH (customer is billed twice for one order).**

`so.invoice_exists` only looks for an Invoice created **directly from** the SO. It does not
traverse `SO → DO → Invoice`, even though the DO stores the link.

Repro:

```
# leg 1 — the normal chain
POST /api/proxy/sales-orders/20/delivery-orders   → 200 {"delivery_order_id":17}
POST /api/proxy/delivery-orders/17/issue          → 204
POST /api/proxy/delivery-orders/17/mark-delivered → 204
POST /api/proxy/delivery-orders/17/create-invoice → 200 {"billing_note_id":33}
POST /api/proxy/billing-notes/33/issue            → 204   # 07-2026-IV-0001, 5,000.00
# ... receipted and Settled

# leg 2 — SAME SO, direct invoice
POST /api/proxy/sales-orders/20/create-invoice    → 200 {"billing_note_id":34,"tax_invoice_id":null}
POST /api/proxy/billing-notes/34/issue            → 204   # 07-2026-IV-0002, 5,000.00

# leg 3 — only NOW does the guard fire
POST /api/proxy/sales-orders/20/create-invoice
  → 422 {"title":"so.invoice_exists","detail":"Sales Order 20 already has an Invoice."}
```

The link the guard should have used is right there:

```
GET /api/proxy/delivery-orders/17
  {"deliveryOrderId":17,"docNo":"07-2026-DO-0001","salesOrderId":20,"billingNoteId":33,...}
```

- **Expected:** leg 2 refused with `so.invoice_exists` — SO 20 (5,000.00) was already fully
  invoiced via DO 17.
- **Actual:** leg 2 returns 200 and mints a second customer-facing Invoice for the same 5,000.00.
  SO 20 is now billed 10,000.00 against a 5,000.00 order.
- The same endpoint's `vatMode:true` branch (`tiSvc.CreateFromSalesOrderAsync`) is reached by the
  identical guard, so a VAT company is likely exposed too — not tested here (co7 is non-VAT).

---

## H3 — HIGH: the printed Payment Voucher and Vendor Invoice of a non-VAT company do not foot — line items 1,000.00, grand total 1,070.00, nothing explains the 70.00

**Severity: HIGH (the vendor-facing / audit paper for a money document is internally inconsistent).**

On a non-VAT company the vendor's VAT correctly folds into cost, so the document **total** is the
gross while the **line items** stay ex-VAT. The paper then suppresses the entire
Subtotal/Before-VAT/VAT block, leaving a Grand Total that does not equal the sum of the printed
lines.

Repro (PV 55, settling VI `07-2026-VI-0001`; net 1,000.00 + 7% = 1,070.00 paid in full):

```
GET /api/proxy/payment-vouchers/55/paper
{"docType":"ใบสำคัญจ่าย","docNo":"07-2026-PV-IT-0001",
 "items":[{"description":"ค่าวัสดุสำนักงาน A4 (ผู้ขายจด VAT)","amount":1000.0000}],
 "summary":{"subtotal":1000.0000,"beforeVat":1000.0000,"vat":70.0000,
            "total":1070.0000,"showVat":false,"wht":null}}
```

`showVat:false` makes both renderers skip every row that would account for the 70.00:

- screen — `frontend/components/paper/PaperFoot.tsx:44,56` → `{showVat && (<>Subtotal…BeforeVat…VAT</>)}`
- PDF — `backend/…/Infrastructure/Pdf/PaperFootPlan.cs:31` → `if (s.ShowVat) { … }`

so the printed voucher is exactly:

```
  ค่าวัสดุสำนักงาน A4 (ผู้ขายจด VAT)            1,000.00
  จำนวนเงินรวมทั้งสิ้น                       ฿ 1,070.00
```

Worse with WHT (PV 56, `07-2026-PV-IT-0002`, net 10,000 + 700 VAT − 300 WHT = 10,400 paid):

```
GET /api/proxy/payment-vouchers/56/paper
 items:[{"description":"ค่าบริการ IT รายเดือน","amount":10000.0}]
 summary:{"subtotal":10000.0,"beforeVat":10000.0,"vat":700.0,
          "total":10400.0,"showVat":false,"wht":300.0}
```

→ prints items 10,000.00, then Grand Total **10,700.00**, WHT −300.00, Net 10,400.00. A 700.00
jump between the item list and the Grand Total, unlabelled.

**The fix was attempted and is dead code.** `frontend/app/(dashboard)/payment-vouchers/[id]/page.tsx`
prepares a non-VAT label for exactly this row:

```
93:    th: 'ภาษีมูลค่าเพิ่ม (เครดิตไม่ได้ — รวมเป็นต้นทุน)',
217:   summary={vatMode ? paperProps.summary : { ...paperProps.summary, vatLabel: paperVatLabel }}
```

but the backend ships `showVat:false`, so `PaperFoot` never renders the row the label is for.
(The on-screen stat strip at lines 266/280 *does* show it — only the paper/PDF is wrong.)

**The Vendor Invoice has the same defect and is worse.** `frontend/app/(dashboard)/vendor-invoices/[id]/page.tsx:181-189`
builds the paper as items = `l.amount` (1,000.00) and `vat: d.vatAmount`, but on a non-VAT company
`vatAmount` is **0** — the 70.00 lives in `nonRecoverableVatAmount`, which the mapping never reads:

```
GET /api/proxy/vendor-invoices/26
 {"docNo":"07-2026-VI-0001","subtotalAmount":1000.0000,"vatAmount":0.0000,
  "nonRecoverableVatAmount":70.0000,"totalAmount":1070.0000, ...}
```

`summary.showVat` is `undefined` here, so `PaperDocument.tsx:47` defaults it from
`sys.vatMode` = false → the block is hidden anyway; and even if it were shown it would print
"ภาษีมูลค่าเพิ่ม 0.00" and *still* not foot (1,000 + 0 ≠ 1,070).

- **Expected:** the paper foots — either the folded VAT gets its own labelled row
  (the label already written at line 93), or the items print gross.
- **Actual:** items 1,000.00 → Grand Total 1,070.00, with no line accounting for the difference,
  on both the ใบสำคัญจ่าย and the ใบกำกับภาษีซื้อ.
- Wording check: **no** VAT label, no "ใบกำกับภาษี", no VAT column appears on any non-VAT paper
  (`showVat:false` on all of PO/PV/QT/SO/DO/IV/RC papers) — the defect is arithmetic, not wording.

---

## H4 — HIGH (observed occurrence, NOT my repro): account 1170 ภาษีซื้อ carries a 535.00 debit on this non-VAT company

**Severity: HIGH.** Reported because the dispatch asks for every 1170/2151 touch on co7.
**Attribution: not produced by any document I created** — it is very likely a sibling agent's
expense-claim probe, and should be treated as a cross-reference, not a second filing.

```
GET /api/proxy/reports/trial-balance?asOfDate=2026-07-31
  ... {"accountCode":"1170","accountNameTh":"ภาษีซื้อ","debit":535.0000,"credit":0.0000,"net":535.0000}
```

Source:

```
GET /api/proxy/journals/293
  07-2026-JV-0012 | EX 07-2026-EX-0002
    1170 ภาษีซื้อ    Dr 535.00   description: "forced input VAT line"
    1110 เงินสด                 Cr 535.00
```

The line description ("forced input VAT line") indicates a deliberate bypass probe rather than the
documented REST path. **My own independent probe of the plain path was correctly guarded**, so the
guard added by `specs/fix-purchase-nonvat-ux.md` F-B is intact for normal callers:

```
POST /api/proxy/expense-claims       body: vatRate 0.07, isRecoverableVat TRUE, amount 1000
  → 201, draft reads back: {"totalAmount":1070.0,"vatAmount":70.0,
                            lines[0].isRecoverableVat: FALSE}          # forced false ✔
POST /api/proxy/expense-claims/23/pay → 200 {"docNo":"07-2026-EX-0006","totalAmount":1070.0000}
GET  /api/proxy/journals/302
  07-2026-JV-0018 | EX 07-2026-EX-0006
    5200 ค่าใช้จ่ายค่าบริการ  Dr 1070.00     ← folded, full gross
    1120 เงินฝากธนาคาร                    Cr 1070.00
  (no 1170 line; employee reimbursed the FULL 1,070.00)
```

**2151 ภาษีขายค้างจ่าย is clean** — TB shows `debit 0, credit 0, net 0`. No output-VAT leak anywhere.

---

# MEDIUM

## M5 — MED: a non-VAT-registered company can generate, print and **FINALIZE** a ภ.พ.30 return

Every Tax Invoice path correctly refuses (`422 ti.non_vat_blocked`). The ภ.พ.30 filing surface has
no equivalent guard — the UI merely hides the nav item.

```
POST /api/proxy/tax-filings/pnd30?period=202607&mode=preview   → 200
POST /api/proxy/tax-filings/pnd30?period=202607&mode=finalize  → 200
  {"period":202607,
   "company":{"taxId":"0105569000029",
              "nameTh":"บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด", "branchCode":"00000"},
   "filingDueDate":"2026-08-15","status":"Finalized", ...}

GET  /api/proxy/tax-filings
  [{"filingId":1,"formType":"PND30","period":202607,"status":"Finalized",
    "finalizedAt":"2026-07-31T02:07:11.237+00:00"}]

GET  /api/proxy/tax-filings/pnd30/pdf?period=202607
  → 200  application/pdf  289,953 bytes   (a filled ภ.พ.30 carrying the company's name + Tax ID)
```

Also open, returning zeros rather than refusing:
`GET /reports/vat-register?year=2026&month=7` → 200,
`GET /reports/pnd30?year=2026&month=7` → 200,
`GET /tax-filings/pp01/pdf` → 200 application/pdf 356,753 bytes.

FE gating exists but is FE-only: `components/app-shell/SidebarNav.tsx:109` and
`app/(dashboard)/tax-filings/page.tsx:11` both mark PND30 `vatOnly: true`. So any API-key / MCP
agent, or a directly-typed URL, is unguarded.

- **Expected:** `422 pnd30.non_vat_blocked` (or equivalent) on generate / PDF / finalize, matching
  the Tax Invoice precedent — filing a ภ.พ.30 while not VAT-registered is a filing offence.
- **Actual:** all three succeed; a Finalized filing record now exists on co7.
- **Not a finding:** ภ.พ.36 *is* correctly offered (`tax-filings/page.tsx:16`, no `vatOnly`) —
  ม.83/6 reverse-charge binds non-registrants too. `POST /tax-filings/pnd36?period=202607&mode=preview`
  → 200, `rows: []`, no crash.

## M6 — MED: HTTP 500 on `POST /sales-orders/{id}/delivery-orders` when the body omits `lines`

Unhandled exception, reproduced 3×:

```
POST /api/proxy/sales-orders/20/delivery-orders   body: {}
  → 500 {"type":"urn:teas:error:internal_error","title":"internal_error",
         "detail":"An unexpected error occurred."}

POST /api/proxy/sales-orders/20/delivery-orders
  body: {"docDate":"2026-07-31","customerId":19,"isCombinedWithTi":false,"fromSalesOrderId":20}
  → 500 (same)
```

Contrast — the sibling routes validate properly:

```
POST /api/proxy/quotations       body: {}  → 400 with fieldErrors (customerId / lines / currency)
POST /api/proxy/delivery-orders  body: {}  → 404 customer.not_found
```

`SalesChainEndpoints.cs:91-94` binds `CreateDeliveryOrderRequest` with no `IValidator<>` in the
handler, so a null `Lines` reaches the service.

- **Expected:** 400 with field errors.
- **Actual:** 500 `internal_error`.

## M7 — MED: Purchase Orders do not clamp VAT server-side on a non-VAT company, and the PO detail screen prints a bare "ภาษีมูลค่าเพิ่ม" row

Every sales document clamps a client-supplied 7% to zero server-side. The PO does not.

```
POST /api/proxy/purchase-orders   line: taxCodeId 1, taxCode "VAT7", taxRate 0.07
  → 201 {"purchase_order_id":27}
GET  /api/proxy/purchase-orders/27
  {"subtotalAmount":1000.0000,"vatAmount":70.0000,"totalAmount":1070.0000,
   "lines":[{"lineAmount":1000.0000,"taxAmount":70.0000,"totalAmount":1070.0000}]}
```

Same payload shape against the sales chain, all clamped correctly:

| route | sent taxRate | resulting vatAmount |
|---|---|---|
| `POST /quotations` (id 39) | 0.07 | **0.00** ✔ |
| `POST /sales-orders` (id 21) | 0.07 | **0.00** ✔ |
| `POST /delivery-orders` (id 18) | 0.07 | **0.00** ✔ |
| `POST /billing-notes` (id 35) | 0.07 | **0.00** ✔ |
| `POST /purchase-orders` (id 27) | 0.07 | **70.00** ✘ |

Display side: `frontend/app/(dashboard)/purchase-orders/[id]/page.tsx:226` renders
`{ label: t('vat'), value: d.vatAmount, muted: true }` with **no** `vatMode` gate, so PO 27 shows a
plain "ภาษีมูลค่าเพิ่ม 70.00" row on a company whose `vat_mode` is false. The create form does
clamp (`PurchaseOrderForm.tsx:150,194-195`, `vendorVat = vatMode && vendor.vatRegistered`), so this
is an FE-only guard — the exact defence-in-depth gap that `specs/fix-purchase-nonvat-ux.md` F-B
closed for expense claims with three independent layers.

## M8 — MED: every non-VAT PO → VI with a VAT-registered vendor raises a **false** over-receipt warning

Structural, not data-dependent. The UI clamps the PO to the **net** (1,000.00) while the VI
correctly folds VAT into cost (**1,070.00**) — so VI/PO = 107% clears the 105% threshold on every
such pair, for a PO and VI that describe the identical purchase.

```
POST /api/proxy/purchase-orders   (taxRate 0, as the FE sends on a non-VAT co) → PO 07-2026-PO-0001, total 1,000.00
POST /api/proxy/vendor-invoices   (vatRate 0.07, purchaseOrderId 26)           → VI draft, total 1,070.00
POST /api/proxy/vendor-invoices/26/post
  → 200 {"docNo":"07-2026-VI-0001","totalAmount":1070.0000,
         "poOverReceiptWarning":"รับเกินใบสั่งซื้อ: รวม VI 1,070.00 > PO 1,000.00 (เกิน 105%) — โปรดตรวจสอบ"}
```

- **Expected:** no warning — the VI matches the PO exactly, the 70.00 is the vendor's VAT folding
  into cost as designed.
- **Actual:** a warning on every non-VAT PO→VI pair, training users to ignore a real control.
  (Confirmed the warning is the comparison and not the data: VI 27, posted with no PO link,
  returns `"poOverReceiptWarning":null`.)

---

# LOW

## L9 — LOW: an invalid enum value returns HTTP 400 with a completely empty body

```
POST /api/proxy/tax-adjustment-notes   body: {"noteType":"CreditNote", ...}   # valid value is "Credit"
  → 400, Content-Length 0, no problem+json, no fieldErrors
```

Every other error on the API returns a structured `urn:teas:error:*` document, e.g. the same request
with `"noteType":"Credit"` → `400 {"title":"validation","fieldErrors":[{"field":"reasonCode",...}]}`.
(One attempt of this request also surfaced as a Cloudflare **520** — origin returned nothing;
not reproducible on retry, so not filed separately.)

---

# Known-elsewhere bugs — does co7 share them?

**K1 — >2-decimal amounts reaching the GL: YES, and the surface is wider than previously filed.**
Confirmed through the **Payment Voucher** path (previously seen on expense claims), which I drove
myself:

```
POST /api/proxy/payment-vouchers   line amount: 100.005
  → 201, approve 200, post 200 {"docNo":"07-2026-PV-IT-0004","totalPaid":100.0050}
GET  /api/proxy/journals/306
  07-2026-JV-0022 | PV 07-2026-PV-IT-0004
    5200  Dr 100.005
    1120            Cr 100.005          ← three decimals in the general ledger
```

co7's trial-balance totals are consequently sub-satang: `{"debit":544060.031,"credit":544060.031}`.
Negative and zero amounts *are* rejected (`400 'Amount' must be greater than '0'`), so it is
specifically the decimal-scale check that is missing.

**K2 — a voided PV still prints "ต้นฉบับ": YES.**

```
POST /api/proxy/payment-vouchers → 58 ; approve 200 ; POST /payment-vouchers/58/cancel → 204
GET  /api/proxy/payment-vouchers/58        → {"status":"Voided"}
GET  /api/proxy/payment-vouchers/58/paper  → {"docNo":"(ร่าง)",
        "watermark":{"text":"ต้นฉบับ","variant":"success"}}
```

(Separately confirmed a **posted** PV cannot be cancelled at all:
`POST /payment-vouchers/57/cancel` → `422 pv.cannot_cancel` — "only a Draft or Approved PV may be
cancelled". So this reaches print only for the Draft/Approved-then-voided shape.)

**K3 — duplicate running doc numbers: not observed on co7.**
`GET /reports/number-gaps?year=2026&month=7` → `{"gaps":[],"hasGaps":false}`; no duplicate `docNo`
seen across the PO/VI/PV/QT/SO/DO/IV/RC documents created this leg.

**K4 — payroll into a closed period: N/A on co7.** `GET /periods/2026/year-status` → all 12 FY2026
periods `Open`, `isClosed:false`. (Noted only in passing, not filed and not mine: another agent
posted `12-2099-JV-0001 / PR 12-2099-PR-0001` on co7 — payroll dated **December 2099**, outside any
defined fiscal year.)

---

# What passed — the money invariants this leg exists to prove

**Fold-into-cost + vendor paid in FULL — the exact failure class the dispatch names — is CORRECT
on every purchase path tested.** No underpayment, no stranded AP, no `1170` line anywhere in my
postings.

| Path | Input | JE | Vendor/employee receives |
|---|---|---|---|
| VI (VAT vendor, net 1,000 @ 7%) | `vatRate 0.07, hasInputVat null` | `07-2026-JV-0009`: Dr **5200 1,070.00** / Cr 2110 1,070.00 | AP raised at gross |
| PV settling that VI | `vendorInvoiceId 26` | `07-2026-JV-0011`: Dr 2110 1,070.00 / Cr 1120 1,070.00 | **1,070.00 — in full** |
| Standalone PV, VAT vendor + WHT 3% | net 10,000 @ 7%, whtTypeId 62 | `07-2026-JV-0013`: Dr **5200 10,700.00** / Cr 2152 300.00 / Cr 1120 10,400.00 | 10,400 + 300 to RD = **10,700 in full** |
| Expense claim, `isRecoverableVat: TRUE` forced | net 1,000 @ 7% | `07-2026-JV-0018`: Dr **5200 1,070.00** / Cr 1120 1,070.00 | **1,070.00 — in full** |
| VI with `hasInputVat: TRUE` forced | net 1,000 @ 7% | `07-2026-JV-0021`: Dr **5200 1,070.00** / Cr 2110 1,070.00 | guard held, `isRecoverableVat` forced false |
| Non-VAT sales receipt (applied) | IV 5,000 | `07-2026-JV-0017`: Dr 1120 5,000.00 / Cr 4000 5,000.00 | no 2151 |
| Non-VAT standalone cash bill + WHT 3% | 5,000, whtTypeId 62 | `07-2026-JV-0020`: Dr 1120 4,850.00 / Dr 1180 150.00 / Cr 4000 5,000.00 | foots; paper Grand 5,000 → WHT −150 → Net 4,850 ✔ |

WHT base is correctly the **net** (10,000), not the VAT-inclusive gross — `whtAmount 300.00`, not
749.00. VI settlement is exact: `settledAmount 1070.0000`, `settlementStatus "PAID"`. Vendor
sub-ledger reconciles: `vendor-ledger` vendor 15 → `closingBalance 1070.0000` vs
`controlAccountBalance 1070.0000`, `difference 0.0000`.

**Cross-mode refusal is solid:**

```
POST /api/proxy/tax-invoices                          → 422 ti.non_vat_blocked
POST /api/proxy/delivery-orders/17/create-ti          → 422 ti.non_vat_blocked
POST /api/proxy/billing-notes/33/create-tax-invoice   → 422 ti.non_vat_blocked
   detail: "VAT-not-registered companies cannot issue Tax Invoices (ม.86/4).
            Use a delivery note / receipt instead."
POST /api/proxy/tax-adjustment-notes (Credit, originalTaxInvoiceId 1 and 33)
                                                      → 422 note.original_missing  (no TI can exist;
                                                         also no cross-tenant leak of co5's TI ids)
```

**Trial balance balances:** `{"debit":544060.031,"credit":544060.031,"balanced":true}`; every one of
the 8 journals I posted has `totalDebit == totalCredit`.

---

# Artifacts left on co7 (all mine, all in open period 2026-07)

PO `07-2026-PO-0001` (26), PO 27 (draft, VAT-injection probe) ·
VI `07-2026-VI-0001` (26, PAID), `07-2026-VI-0002` (27, unpaid 1,070.00) ·
PV `07-2026-PV-IT-0001` (55), `-0002` (56, WHT cert `07-2026-WT-0002`), `-0003` (57),
`-0004` (59, the 100.005 probe), PV 58 (Voided) ·
QT `07-2026-QT-0003` (38), QT 39 · SO `07-2026-SO-0001` (20), SO 21 ·
DO `07-2026-DO-0001` (17), DO 18 ·
IV `07-2026-IV-0001` (BN 33, Settled), **`07-2026-IV-0002` (BN 34, the duplicate from H2)**, BN 35 (draft) ·
RC `07-2026-RC-0001` (34), `07-2026-RC-0002` (35) ·
EX `07-2026-EX-0006` (23) ·
**Tax filing `filingId 1` PND30 202607 Finalized** (from M5 — a non-VAT company now has a
finalized ภ.พ.30 on record).
Journals created: `07-2026-JV-0009, -0011, -0013, -0017, -0018, -0019, -0020, -0021, -0022`.
