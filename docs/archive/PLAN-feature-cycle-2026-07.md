# TEAS Feature Cycle Plan — decided 2026-07-08

Scope decisions by Ham after the post-v1.14.0 gap analysis (repo inventory + Thai
market scan). **Product philosophy locked in:** TEAS is an accounting core that
other systems integrate with via API/MCP — subscription billing (recurring
invoices) and inventory/stock are explicitly OUT: the connected systems own
those and push documents in.

## Selected features (7)

### 1. Bank Reconciliation — KBiz CSV first 【L】
Reconcile bank statements against the books; today this is Excel work.
- **Bank account master:** bank / account number / linked GL cash-at-bank
  account per bank account (today account numbers are just text fields on
  profiles).
- **Statement import — KBiz (KBank) CSV first.** Real sample:
  `STM_SA3269_01FEB26_07JUL26.csv` (K-DEPOSIT saving statement w/ detail).
  Format notes from the sample:
  - ~11 metadata rows (account name/address, ref no, account no
    `232-1-13326-9`, period, branch, closing balance, withdrawal/deposit
    totals) before the column-header row.
  - Columns (leading empty col): วันที่ | เวลา/วันที่มีผล | รายการ |
    ถอนเงิน | ฝากเงิน | ยอดคงเหลือ | ช่องทาง | รายละเอียด.
  - Dates `DD-MM-YY` (CE two-digit), amounts quoted with thousand commas,
    separate withdrawal/deposit columns, opening `ยอดยกมา` row, interest +
    its WHT as separate lines. Encoding + multi-line quoted cells (address
    spans lines) must be handled.
  - Parser architecture: per-bank format adapter — KBiz adapter now, SCB/BBL
    adapters later without touching the core.
- **Matching screen:** auto-suggest matches statement-line ↔ Receipt (in) /
  Payment Voucher (out) by amount + date window; user confirms. Unmatched
  lines → create JE inline (bank fees, interest, WHT on interest).
- **Reconciliation report:** statement balance vs GL balance + outstanding
  items per period.
- Phase 2 (not now): direct bank feed APIs.

### 3. Expense Claims — no-login submitters 【M】
Employee expense reimbursement with approval + GL posting.
- **Decision: submitters do NOT need user accounts.** Any authenticated user
  can key in a claim on behalf of an employee; the claim MUST reference an
  employee from the existing Employee master (dropdown), not free text.
- Claim = multi-line (ExpenseCategory per line → GL account mapping already
  exists) + attachments (Attachment infra already exists).
- Flow: submit → approve (mirror the existing PV/PO approve pattern) → pay
  (generates/links a Payment Voucher) → GL.
- Phase 2 (not now): receipt OCR via the MCP/AI layer.

### 4. Fixed Assets + Depreciation 【M-L】
Asset register + automatic monthly depreciation.
- Register: name/category/acquire date/cost/useful life; straight-line only
  (Thai SME standard); link acquisition to a Vendor Invoice.
- Monthly depreciation JE engine (Dr depreciation expense / Cr accumulated
  depreciation) — integrates with period close: closing a period requires
  that month's depreciation JE.
- Disposal/sale flow with gain/loss calculation.
- Reports: asset register, accumulated depreciation per period (what
  auditors/RD ask for).
- Phase 2 (not now): tax-vs-book depreciation divergence for CIT.

### 5. Year-end Closing Entries 【S】
"Close fiscal year" action → generated closing JE (Dr all revenue / Cr all
expense / net → retained earnings 3xxx) + year lock. Validation: all 12
periods must be closed first. Today retained earnings is computed on-the-fly
at report time — works, but P&L accounts never actually reset in GL.
**Should ship before the first real customer's fiscal year-end.**

### 7. Period Close UI 【S】
Backend is complete (`/periods/{y}/{m}/close` + status); no FE page exists —
closing a period currently means calling the API by hand. One page: period
table + status + close button with confirm.

### 8. AR Aging CSV Export 【S】
ap-aging has CSV export; the new ar-aging doesn't. Same pattern (mind the
CRLF footgun in troubles-wiki).

### 9. DocType Thai labels in Statement/Ledger 【S】
Customer statement / vendor ledger tables show raw "TaxInvoice"/"Receipt"
strings — add i18n mapping keys.

## Rejected (with reason)
- **2 Recurring invoices** — integration-first: external services own
  subscription billing and push documents via API/MCP.
- **6 Inventory/stock** — same: external systems own stock.
- **10 Accounting-firm portal, 11 e-commerce connectors, 12 multi-currency
  full, 13 budgeting, 14 cost centers, 15 mobile app** — not now.

## Proposed sequencing (pending Ham's confirm)
| Cycle | Content | Size |
|---|---|---|
| A | Quick wins: #7 period UI + #8 ar-aging CSV + #9 docType i18n + #5 year-end closing | S×4, one release |
| B | #1 Bank reconciliation (KBiz CSV) | L |
| C | #3 Expense claims | M |
| D | #4 Fixed assets + depreciation | M-L |

Rationale: A ships accounting-correctness basics cheaply; B is the highest
market-value single feature; D after C because depreciation hooks into the
period-close flow A hardens.

Each cycle runs the full pipeline: design spec (Opus for footgun parts:
matching logic, closing JE, depreciation engine — all money surfaces) →
implement → Tier-2 review → gates → deploy.
