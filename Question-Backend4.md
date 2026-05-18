# Question-Backend4 — Sprint 4 model-shape blockers (legal docs)

**Date:** 2026-05-16
**From:** Claude Code
**To:** Ham / Sana
**Re:** Answer-Sana-Backend4 §1–§3. Ack'd + executing; 2 questions gate the CN/Receipt
shape **before** I build (these are ม.86/9–10 / ใบเสร็จ legal documents — per CLAUDE.md
§8/§9 + the established escalation norm I will not silently redesign them).

> Q1–Q2 block the *form/model* shape. Everything that doesn't depend on the answers
> (read endpoints, reason-code enum, Receipt over the existing model, e2e, re-verify)
> I'm proceeding on now. Answers let me finish CN/DN correctly.

---

## Q1 — Receipt: standalone vs application-based (BLOCKS Receipt GL + form)

**What exists (Sprint-2 era, shipped + tested):** `CreateReceiptRequest` requires
`Applications: [{ taxInvoiceId, appliedAmount }]` (validator `NotEmpty`). `PostAsync`
allocates `RC-NNNN`, and **GL = Dr Cash/Bank / Cr AR by settling each linked TI's
`AmountPaid`/`PaymentStatus`**. The credit side is meaningful *because* it's against a
specific TI's receivable.

**Answer-Sana-Backend4 §1 asks for:** "ใบเสร็จ standalone — when **not** combined with
TI", "Field: **optional** reference to TI". A receipt with **no** TI application has no
AR row to credit — GL `Cr AR` needs a target (which customer's receivable, which
invoice, or a generic unapplied-cash/advance account?).

**Question:** For a standalone (no-TI) receipt, what is the GL credit?
- (a) Credit a generic **"เงินรับล่วงหน้า / unapplied customer credit"** liability
  account (advance payment), reconciled later; or
- (b) Standalone receipts are **out of scope** for Sprint 4 — keep
  application-based only (must reference ≥1 TI), TI-ref mandatory not optional; or
- (c) Some other account you want (give the code; it must exist in the seeded CoA).

Until answered I will: build Receipt **list/detail/pdf** + the frontend over the
**existing application-based** model (option b behaviour), and gate the standalone path.

## Q2 — Credit/Debit Note: amount-based vs line-based (BLOCKS CN/DN form)

**What exists:** `CreateTaxAdjustmentNoteRequest` is **amount-based** —
`{ noteType, originalTaxInvoiceId, reason (free text), adjustmentSubtotal, taxRate }`.
No line items. `PostAsync` + `GlPostingService.PostTaxAdjustmentNoteAsync` already do
Dr SalesReturn + Dr/Cr Output-VAT / Cr-Dr AR off `adjustmentSubtotal`. This is legally
sufficient for ม.86/10 (a CN reduces by a stated amount + reason).

**Answer-Sana-Backend4 §2 asks for:** "Lines: editable but **capped at original
quantities** (validation)" — i.e. a **line-level** CN that mirrors the original TI's
lines and bounds each qty ≤ original. That's a structural redesign of the
`TaxAdjustmentNote` aggregate (add lines table, snapshot original lines, per-line caps,
recompute GL per line) on a legal document.

**Question:** Which model for CN/DN?
- (a) **Keep amount-based** (ship faster, legally valid): CN form = pick original TI →
  show its total → enter adjustment **amount** + `reasonCode` + `reasonText`; validation
  = adjustment ≤ original total. (I add the `reasonCode` enum + read endpoints + form.)
- (b) **Redesign to line-based** as specced — bigger change; confirm and I'll do it as
  the bulk of Sprint 4 (snapshot original lines, per-line qty cap, per-line GL). This
  pushes the Receipt/e2e items but is the "correct" UX you described.

Recommend **(a)** for Sprint 4 (amount-based is compliant + reuses shipped GL) and
schedule line-based CN as its own slice if you want the richer UX — but it's your call;
I won't pick for a legal document.

## Q3 — `reasonCode` enum placement (non-blocking, confirm)

Adding `TaxAdjustmentReasonCode { Typo, AmountError, CustomerInfo, Return, PriceReduce,
Cancel }` (CN) — Answer-Sana-Backend4 §2. DN reason set "different" (§3) but not
enumerated. **Question:** give the DN reason codes, or reuse the same enum filtered in
UI? Proceeding with the CN set; DN will reuse pending your list.

---

## Proceeding now (not blocked on answers)

- Receipt: `list` / `detail` / `pdf` endpoints + `IReceiptService` read methods, over
  the existing application-based model (Q1 option-b behaviour).
- CN/DN: `list` / `detail` / `pdf` endpoints + read methods; add `reasonCode` enum +
  field (additive, safe regardless of Q2).
- Frontend: `/receipts`, `/credit-notes`, `/debit-notes` list+detail screens (reuse
  PageHeader/DataTable/StatusBadge/DocumentNumberBadge). Create forms wait on Q1/Q2.
- e2e `issue-receipt` (application-based path), re-verify gates.

If Q1=a or Q2=b I'll extend; nothing built now will be wasted (read/list/detail/enum
are needed either way). Status will land in **Report-Backend5** with whatever's green.
