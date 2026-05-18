# Question-Backend5 — Sprint 5 backend-gap escalation

**Date:** 2026-05-16 · **Sprint:** 5 (Purchase UI slice) · **Author:** Claude Code
**To:** Ham / Sana · **Prev:** [Answer-Sana-Backend5] · [Report-Backend5.md](./Report-Backend5.md)
**Discipline:** flag-don't-improvise (CLAUDE.md §8/§9). Answer-Sana-Backend5
explicitly said *"If any of above missing → flag in Question-Backend5."* Two gaps
are **structural + compliance** — I will NOT improvise them. Two checks below need
a decision before I can finish Sprint 5's e2e gate.

---

## 0. TL;DR

Sprint 5 premise — *"Backend (mostly exists — verify only) … backend exists since
Sprint 2, just UI work"* — is **partly wrong**:

| Sprint-5 backend item | Reality | Verdict |
|---|---|---|
| `POST /vendor-invoices`, GET list/detail, `/{id}/post` | **Entire VendorInvoice slice absent** — no entity, DTO, service, EF config, migration, endpoint (`grep VendorInvoice backend/src` → 0 hits) | 🔴 **B1 structural** |
| PV `POST /{id}/approve` (SoD) | **Absent** — no `ApproveAsync`, no `PaymentVoucherApprove` permission, no `ck_pv_sod` in code path; `PostAsync` lets the same user create+post | 🔴 **B2 compliance** |
| PV `GET` list / detail / pdf | Absent — but safe read surface (my pattern) | 🟢 building now |
| WHT cert `GET` list + `/{id}/pdf` (50 ทวิ) | Entity+EF config exist; no endpoint, no 50 ทวิ template | 🟢 building now |
| `GET /vendors/{id}`; `/vendors` list param binding | Detail missing; list uses non-nullable `int page,pageSize` → **gotcha #2** 400 | 🟢 fixing now |
| `/bank-accounts` (BankAccountSelector) | No `BankAccount` entity at all | 🟡 minor — see Q3 |
| `/expense-categories`, Vendor C/U, WHT entity, PV create+post+per-income-type 50 ทวิ + GL | **Exist & correct** | ✅ |

Good news: `PaymentVoucherService.PostAsync` is solid — allocates
`PV-{CAT}-NNNN`, issues **one 50 ทวิ per income type** (ม.50 ทวิ correct),
GL-posts via `IGlPostingService`. The engine is fine; the **approval gate and the
upstream VendorInvoice document are what's missing.**

**I am proceeding in parallel** on the 🟢/🟡 safe subset (read surfaces, vendor
master UI, selectors, WHT 50 ทวิ render) so the sprint isn't stalled. **B1 and B2
are paused pending your decision.** Without them: no `record-vendor-invoice` e2e,
no `payment-voucher-with-wht` *full* path (PV create+approve UI), so the Sprint-5
6/6 gate cannot fully close on the original scope.

---

## B1 — VendorInvoice backend is entirely absent (structural)

**Evidence:** `grep -r VendorInvoice backend/src` → no entity, no
`Application/Purchase/*VendorInvoice*`, no endpoint, no migration. `PaymentVoucher`
has **no** `vendor_invoice_id` link — current PV is a standalone expense payment,
not "PV against VendorInvoice(s)" as Sprint-5 step 4 / plan §7.3 (3-way match)
describe.

**Why I won't improvise it:** building VendorInvoice = new aggregate +
`vendor_invoice` / `_line` tables + **EF migration** + posting service
(**Dr Expense / Dr Input-VAT — recoverable per ม.82/5 — Cr AP**) + **Input-VAT
claim-period rule ม.82/4** (claimable up to 6 months back) + RLS + audit + 3-way
match hook for PV. That is schema-beyond-plan + GL posting + tax-compliance logic
— precisely CLAUDE.md §9 "ASK before". Guessing the GL/period semantics here is
the same class of risk as the e-Tax C14N case.

**Decision needed — pick one:**

- **B1-A (recommended):** I build the VendorInvoice backend to plan §7.2/§7.3 +
  §17.3 (entity, EF config, migration, posting `Dr Expense/Dr Input-VAT/Cr AP`,
  ม.82/4 VAT-period field `vat_claim_period`, link `payment_voucher.vendor_invoice_id`
  for settlement), then the UI. **Extends Sprint 5** (≈ one extra backend slice
  the size of TaxInvoice). I'll send a 1-page model/GL spec in
  `Question-Backend5-followup` for sign-off **before** writing the migration
  (compliance gate).
- **B1-B:** Defer VendorInvoice entirely. Sprint 5 ships as **AP-lite**: standalone
  PV (no upstream invoice, no 3-way match) + vendor master + 50 ทวิ. Re-scope the
  e2e to `payment-voucher-with-wht` only. Smaller, ships this sprint; 3-way match
  becomes a later sprint.
- **B1-C:** Your alternative.

## B2 — PV approval / SoD not implemented (compliance)

**Evidence:** `IPaymentVoucherService` = `CreateDraftAsync` + `PostAsync` only.
`PostAsync` calls `pv.MarkPosted(pvNo, _tenant.UserId, now)` directly — **no
approve step, no `created_by ≠ approved_by` check**. `Permissions.Purchase` =
Create/Post/Read + WhtRead — **no Approve**. CLAUDE.md §12.1 is a hard rule:
*"SoD enforced: created_by ≠ approved_by (DB CHECK `ck_pv_sod` + app-level check)"*.
Today, whoever holds `payment_voucher.post` can post their own PV — **SoD
violation shippable.**

**Why I won't improvise it:** introducing an approval state
(`Draft → Approved → Posted` vs `Draft → Posted` with an approve guard), a new
permission, a DB CHECK constraint + migration, and deciding *who* may approve is a
workflow + compliance-control decision, not a UX choice.

**Decision needed — pick one:**

- **B2-A (recommended):** Workflow becomes `Draft → Approved → Posted`.
  `POST /{id}/approve` (perm `purchase.payment_voucher.approve`) sets
  `approved_by`/`approved_at`; `PostAsync` refuses unless approved **and**
  `approved_by ≠ created_by`; add DB CHECK `ck_pv_sod` via SqlScripts + migration.
  I implement to this exact spec (it's now unambiguous). Small slice.
- **B2-B:** Keep `Draft → Posted`, but `PostAsync` hard-fails if
  `_tenant.UserId == pv.CreatedBy` (app-level SoD only, no separate approve doc/UI).
  Minimal; satisfies the letter of §12.1 app-side; DB CHECK still added.
- **B2-C:** Your alternative.

---

## Q3 — minor confirmations (non-blocking, defaulting if no reply)

1. **BankAccountSelector** — no `BankAccount` entity exists. PV holds a raw
   `BankAccountId` (long) + `ChequeNo/ChequeDate`. Default plan unless told
   otherwise: **skip BankAccountSelector**; render bank/cheque as plain inputs on
   the (deferred) PV form. A `bank_account` master is a future master-data slice.
2. **50 ทวิ PDF** — no template shipped (`grep` 0). I'll build the QuestPDF
   template to plan **§15.10** RD layout (same QuestPDF infra as TI/Receipt/CN
   `.Read.cs`). Flag if there's an official RD PDF/XML form I should match instead.
3. **`GET /vendors` gotcha #2** — list endpoint declares non-nullable
   `int page,pageSize` → 400 on a param-less call (runtime-gotchas §2). I'm
   fixing to `int?` + defaults as part of the safe subset (backward-compatible).

---

## What I'm doing right now (not waiting idle)

🟢 In parallel, the unblocked safe subset (read-only, my-owned pattern, Phase-1
autonomous master CRUD — no compliance/schema-structural risk):

- PV read surface: `GET /payment-vouchers` (cursor), `GET /{id}`, `GET /{id}/pdf`.
- WHT read surface + **50 ทวิ** QuestPDF: `GET /wht-certificates`, `/{id}`,
  `/{id}/pdf`.
- Vendor: `GET /vendors/{id}` + nullable list fix.
- FE: sidebar **"ซื้อ"** section, `/vendors` list+new+detail, `VendorSelector`,
  `ExpenseCategorySelector` (backend exists), `/wht-certificates` list+detail+pdf,
  `/payment-vouchers` **read-only** list+detail.

⏸ Paused until your answer: `/vendor-invoices` (B1), PV create+approve UI (B2),
`record-vendor-invoice.spec.ts`, the *create* half of
`payment-voucher-with-wht.spec.ts`.

**Answer format:** reply `Answer-Backend5` with `B1=<A|B|C>`, `B2=<A|B|C>`,
optional Q3 overrides. On B1-A I'll send the model/GL spec for sign-off before the
migration. Report-Backend6 will be honest about the split (shipped vs paused).
