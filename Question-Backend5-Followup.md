# Question-Backend5-Followup — VendorInvoice model + GL spec (B1-A sign-off gate)

**Date:** 2026-05-16 · **Sprint:** 5.5 · **Author:** Claude Code · **To:** Ham / Sana
**Gate:** Per [Answer-Sana-Question-Backend5](./Answer-Sana-Question-Backend5.md) §B1.2
— **I will NOT write the migration until you sign off** in
`Answer-Sana-Question-Backend5-Followup.md`. If GL looks off, we iterate the spec, not code.

Scope this sprint: VendorInvoice (VI) + PV-settles-VI link + B2 PV approval.
**Scope cut (confirmed):** NO 3-way match (PR→PO→GR) — filed as tech debt in `plan.md`.

---

## 1. ERD — 3 new tables + 2 FK columns (`purchase` schema)

```
purchase.vendor_invoices                 (aggregate root — mirrors sales.tax_invoices)
  vendor_invoice_id   BIGINT PK
  company_id INT NN · branch_id INT NN              -- RLS: app.company_id (same pattern)
  doc_no              VARCHAR  NULL                  -- our internal no; NULL→VI-NNNN on POST
  status              INT NN  (Draft|Posted|Voided)
  doc_date            DATE NN                        -- date we record it (Asia/Bangkok, today)
  -- vendor's legal source doc (the ใบกำกับภาษีซื้อ) — snapshot, the ม.82/4 legal refs:
  vendor_tax_invoice_no    VARCHAR NN
  vendor_tax_invoice_date  DATE NN
  -- ม.82/4 input-VAT claim period (year*100+month, e.g. 202604). default = period of
  -- vendor_tax_invoice_date; user may move FORWARD up to 6 months from that date.
  vat_claim_period    INT NN
  -- vendor snapshot (frozen — vendors editable later, like TI customer snapshot):
  vendor_id BIGINT NN · vendor_tax_id VARCHAR NULL · vendor_branch_code VARCHAR NULL
  vendor_name VARCHAR NN · vendor_address VARCHAR NULL · vendor_type INT NN
  -- amounts NUMERIC(19,4):
  currency_code VARCHAR(3)='THB' · exchange_rate NUMERIC=1
  subtotal_amount · vat_amount · non_recoverable_vat_amount · total_amount · total_amount_thb
  settled_amount NUMERIC(19,4)=0 · settlement_status VARCHAR='UNPAID'   -- UNPAID|PARTIAL|PAID
  notes VARCHAR NULL
  posted_at TIMESTAMPTZ(3) NULL · posted_by BIGINT NULL
  created_at/by · updated_at/by · version    -- IAuditable + IConcurrencyVersioned

purchase.vendor_invoice_lines            (mirrors sales.tax_invoice_lines)
  line_id BIGINT PK · vendor_invoice_id BIGINT FK NN · line_no INT NN
  expense_category_id INT NN              -- drives expense GL + recoverable + capex/cogs
  expense_account_id  BIGINT NN           -- resolved at draft from category default (overridable)
  description         VARCHAR NN
  amount              NUMERIC(19,4) NN    -- net, ex-VAT
  tax_code_id         INT NULL · vat_rate NUMERIC NN · vat_amount NUMERIC(19,4) NN
  is_recoverable_vat  BOOL NN             -- snapshot of category.DefaultIsRecoverableVat
  is_capex            BOOL NN             -- snapshot of category.IsCapex
  is_cogs             BOOL NN             -- snapshot of category.IsCogs

purchase.payment_voucher_applications    (PV settles N VI — mirrors receipt→TI applications)
  application_id BIGINT PK
  payment_voucher_id BIGINT FK NN · vendor_invoice_id BIGINT FK NN
  applied_amount NUMERIC(19,4) NN
  UNIQUE(payment_voucher_id, vendor_invoice_id)

-- FK columns added:
purchase.payment_vouchers.vendor_invoice_id BIGINT NULL   -- simple 1:1 fast-path; NULL = standalone PV (current behaviour preserved)
purchase.payment_vouchers.approved_by BIGINT NULL · approved_at TIMESTAMPTZ(3) NULL   -- B2
```

**Immutability:** same trigger pattern as `tax_invoices` — once `status=Posted`, block
`UPDATE`/`DELETE` on critical columns (amounts, vendor snapshot, vat_claim_period,
vendor_tax_invoice_no/date, doc_no). Enforced DB-trigger + app (`MarkPosted` guard).
**Number:** `VI-NNNN` from existing `NumberSequenceService` (prefix `VI`, monthly,
allocated on POST only — gapless, same as TI).

---

## 2. GL posting on `VendorInvoice.PostAsync` (the AP accrual)

Resolve via `GlAccountsOptions`: **AP=2110**, **InputVAT=1170**. Expense account =
`vendor_invoice_lines.expense_account_id` (from category). CapEx line → category's
fixed-asset account (still `expense_account_id`, just a 1xxx asset code in the seed).
JV built with existing `GlPostingService` pattern (balanced, `JV-NNNN`).

Per line:
- **Recoverable** (`is_recoverable_vat = true`):
  `Dr Expense/Asset = net` · `Dr InputVAT 1170 = vat` · `Cr AP 2110 = net+vat`
- **Non-recoverable** (`is_recoverable_vat = false` — ENT/VEHI, ม.82/5):
  `Dr Expense = net+vat` (VAT lumped into expense, **NOT** 1170) · `Cr AP 2110 = net+vat`
- **VAT-mode OFF / zero-rate line** (`vat = 0`): `Dr Expense = net` · `Cr AP = net`

PV settling a VI (Sprint 6 wiring; stated here for review): when
`payment_voucher.vendor_invoice_id` is set, PV POST GL becomes
`Dr AP 2110` (instead of Dr Expense) · `Cr Bank/Cash` · `Cr WHT-Payable 2152` — i.e.
expense already hit the books at VI POST; PV just clears the payable. **Standalone PV
(no VI) keeps today's behaviour unchanged.** Only the VI tables + link + B2 are in this
migration; the PV-settle GL branch is Sprint-6 code (flagged so the model is reviewed
holistically now).

### 3 sample postings

**A. Full-VAT recoverable** — service ฿10,000 + 7% VAT, category PROF (recoverable):
```
Dr  6xxx Professional fee      10,000.00
Dr  1170 Input VAT                700.00
    Cr 2110 Accounts Payable           10,700.00
vat_claim_period from vendor_tax_invoice_date.
```

**B. Mixed recoverable + non-recoverable** — one VI, 2 lines:
line1 consulting ฿20,000 +7% (recoverable); line2 entertainment ฿5,000 +7% (ENT, non-rec):
```
Dr  6xxx Consulting            20,000.00
Dr  1170 Input VAT              1,400.00
Dr  6xxx Entertainment          5,350.00   (5,000 + 350 VAT lumped — ม.82/5)
    Cr 2110 Accounts Payable           26,750.00
```

**C. Non-VAT-mode** (Tax:VatMode=false) or zero-rated — ฿8,000, no VAT:
```
Dr  6xxx Expense                8,000.00
    Cr 2110 Accounts Payable            8,000.00
```

---

## 3. ม.82/4 input-VAT claim-period — worked example

Rule: input VAT is claimable in the period of the vendor's tax-invoice date **or any
of the following 6 months** (ม.82/4). `vat_claim_period` (INT yyyymm) is the period
the line's input VAT lands in `tax.input_vat_register` → ภ.พ.30.

> Vendor's TI dated **2026-01-15**. We record the VI in our books on **2026-04-20**
> (doc_date). Allowed `vat_claim_period` ∈ **{202601, 202602, 202603, 202604, 202605,
> 202606, 202607}** — i.e. 2026-01 (TI month) through 2026-07 (TI month + 6).
> **Default** = 202601 (period of vendor TI date). User may set it forward to any value
> in that set; **reject** < 202601 or > 202607. (Validation = months-between(
> vendor_tax_invoice_date, claim_period_first_day) ∈ [0, 6].) Note: the window is
> anchored to **vendor_tax_invoice_date**, NOT our doc_date — recording late doesn't
> shrink the legal window, but a claim_period before the TI month is illegal.

Open question for you: if `vat_claim_period` would fall in an already-**closed**
accounting period, do we (a) reject at VI POST, or (b) allow and let it surface in that
period's ภ.พ.30 as a late claim? I lean (a) reject — consistent with
`IPeriodCloseService.EnsureOpenAsync` used everywhere else. Confirm.

---

## 4. B2 — PV approval (per Answer §B2-A; restating for one sign-off)

`Draft→Approved→Posted`. `POST /payment-vouchers/{id}/approve`, perm
`purchase.payment_voucher.approve` (seed + grant Accountant/CFO). Cols
`approved_by`/`approved_at`. `ApproveAsync`: refuse if `pv.CreatedBy ==
_tenant.UserId`. `PostAsync`: refuse unless `Status==Approved`. DB CHECK
`ck_pv_sod = (approved_by IS NULL) OR (approved_by <> created_by)`. Approver MAY also
be poster (2-person SME — confirmed). Backfill existing posted PVs:
`approved_by=posted_by, approved_at=posted_at` (one-time, documented in migration).

---

## 5. Sign-off checklist (please ✅/✏️ each in Answer-…-Followup)

1. ERD: 3 tables + FK columns + `vat_claim_period` as INT yyyymm — OK?
2. GL: recoverable / non-recoverable / no-VAT three-way — OK? (esp. non-rec lumping into expense)
3. PV-settles-VI GL branch (Dr AP not Dr Expense) — OK for Sprint-6, model sound?
4. ม.82/4 window anchored to vendor_tax_invoice_date, [TI month .. +6] — OK?
5. Closed-period × vat_claim_period: (a) reject vs (b) allow late — your call.
6. B2 restatement matches your intent — OK?

On ✅ I proceed: entities → EF config → **one** migration (VI tables + PV link + B2
cols + `ck_pv_sod` + immutability trigger) → posting service → endpoints → tests →
re-verify gates. UI is Sprint 6 (not batched).
