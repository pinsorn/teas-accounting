# Answer-Sana-Question-Backend5-Followup — VendorInvoice model + GL spec sign-off

**Date:** 2026-05-16  
**From:** Ham (via Sana, Cowork)  
**To:** Claude Code  
**Re:** [Question-Backend5-Followup.md](./Question-Backend5-Followup.md) — VI model/GL spec  
**Gate:** **B1.2 sign-off ✅ — proceed to migration after applying 5 small refinements below.**

> Spec is clean and aligned with plan §7 + §17.3 + ม.82/4 + §15.10. Three GL postings are
> all accounting-correct. The escalation discipline pays off again — building this from
> a "vibe" would have shipped at least one wrong-direction posting. Proceed per refinements.

---

## 1. ERD — ✅ approved as written

Mirror-of-TaxInvoice shape is the right call (consistent, audit-friendly). Three tables
+ FK columns + `vat_claim_period INT yyyymm` — all fine.

**No structural changes requested.** Just two small nits to apply during implementation:

- **Nit A — index `vat_claim_period`.** This is what `tax.input_vat_register` will key
  off when generating ภ.พ.30 input-side. Add index
  `ix_vil_vat_claim_period (company_id, vat_claim_period)` on `vendor_invoice_lines`
  (or on `vendor_invoices` if the period lives only on the header — your call; if
  per-line then it's per-line). Without this, monthly ภ.พ.30 generation will table-scan.
- **Nit B — `settled_amount` overflow defense.** Plain `NUMERIC(19,4) DEFAULT 0` is
  fine, but add a CHECK: `settled_amount >= 0 AND settled_amount <= total_amount`
  (or rounding tolerance +0.01). Belt-and-braces against PV-applications accidentally
  over-settling.

Neither blocks anything — apply during the migration.

---

## 2. GL three-way — ✅ approved (all three are correct)

- **Recoverable** (Dr Expense + Dr InputVAT 1170 / Cr AP 2110) — standard, correct.
- **Non-recoverable ม.82/5** (Dr Expense net+vat / Cr AP) — **correct**: when VAT can't
  be claimed as input credit, it becomes part of the expense per ม.82/5. ENT/VEHI
  categories already flag `is_recoverable_vat=false` (per
  `accounting-system-plan.md` §17.3), so this branch is data-driven without manual
  intervention. Good.
- **VAT-mode OFF / zero-rated** (Dr Expense / Cr AP) — fine.

**One must-do for the implementation:** the line's `is_recoverable_vat` decision MUST
be a **snapshot** (already in your spec — column on `vendor_invoice_lines`). DO NOT
re-resolve from `expense_categories.default_is_recoverable_vat` at POST time — the
category's setting could change later and that must not retroactively alter posted GL.

Confirm you'll lock this at draft-create time (when the line is added) and re-validate
at POST without re-resolving.

---

## 3. PV-settles-VI GL branch — ✅ approved (model sound)

Dr AP 2110 / Cr Bank/Cash / Cr WHT-Payable 2152 — exactly right. The expense+input
VAT already landed at VI POST, so PV is purely a payable clearing + cash movement +
WHT withholding. Standalone PV (no VI link) unchanged — backward compat ✅.

**One detail to nail in Sprint-6 implementation (not blocking this migration):**

When PV settles a VI with WHT, the math has to balance:
- VI total = 10,700 (e.g. 10,000 + 700 VAT)
- WHT withheld at payment = 300 (3% of net 10,000, ม.50ทวิ on the NET, not the total)
- Cash paid = 10,400
- GL: `Dr AP 10,700 · Cr Bank 10,400 · Cr WHT-Payable 300` ← balanced ✅

Two subtle things to lock in code:
- **WHT base = NET of VAT** per Thai practice (ม.50 ทวิ withholding is on the net
  service value before VAT, not on the total). Currently
  `PaymentVoucherService.PostAsync` likely computes WHT off a base you pass in — make
  sure the form/UI in Sprint 6 defaults the WHT base to VI's `subtotal_amount` (net),
  not `total_amount` (gross). Easy to get wrong.
- **`settled_amount += applied_amount`** updates on `vendor_invoices` at PV POST, and
  `settlement_status` transitions: UNPAID → PARTIAL (when 0 < settled < total) → PAID
  (when settled ≥ total − 0.01 tolerance). Don't compute by SUM at read time — store
  it. Cheaper, and matches the immutability mindset.

Flagged here so you have it on file for Sprint 6; not part of this migration.

---

## 4. ม.82/4 window — ✅ approved exactly as written

Anchored to **vendor_tax_invoice_date**, window = **[TI month .. TI month + 6]**
inclusive = 7 periods. Reject anything before TI month (illegal) or after TI month + 6
(claim expired).

Validation lives in **`VendorInvoiceService.SetClaimPeriodAsync`** (or whatever the
mutation path is) AND as a DB CHECK if you want belt-and-braces (CHECK can use
`vat_claim_period BETWEEN ToYyyymm(vendor_tax_invoice_date) AND PlusMonths(...,6)` —
might be ugly in plain SQL, app-side validation alone is fine).

Default to **TI month** (= claim ASAP) on draft create — that's the safest behaviour
for users who don't touch the field.

---

## 5. Closed-period × vat_claim_period — **(a) REJECT** ✅

Your lean is correct. Match `IPeriodCloseService.EnsureOpenAsync` semantics throughout —
**all** period-affecting operations refuse a closed target period. No special-case for
late VAT claims; the user must pick the **next open period within the allowed 7-month
window** (give them this error message — actionable, not just "rejected").

**Error UX (worth nailing in the form):** when user picks a closed period, the
error should say something like:
> "ไม่สามารถใช้รอบ {202601} ได้ (ปิดงบแล้ว) — กรุณาเลือก {รอบ open ตัวถัดไปในหน้าต่างที่อนุญาต: 202604, 202605, ..., 202607}"

If ALL 7 candidate periods are closed (very unlikely — needs > 7 months between vendor
TI date and recording), the line can't be claimed at all — surface that as a hard error
with a hint to amend prior return manually. Don't auto-amend.

This UX detail is for Sprint 6, but the **rejection rule itself goes in the migration
sprint** (in `VendorInvoiceService.PostAsync`).

---

## 6. B2 restatement — ✅ matches intent exactly

`Draft→Approved→Posted`, `ck_pv_sod = (approved_by IS NULL) OR (approved_by <> created_by)`,
approver MAY be poster. Backfill `approved_by=posted_by, approved_at=posted_at` for
existing posted PVs. Permission `purchase.payment_voucher.approve`, seed + grant
Accountant/CFO. Sign-off ✅.

**One backfill defensive nit:** if any existing posted PV has `posted_by IS NULL`
(shouldn't, but defensive), skip the backfill row rather than violating the future
NOT-NULL invariant. Document the count in the migration's comment.

---

## 7. Items NOT in spec — flag for plan.md / future sprints

These came up while reviewing — none block the current migration, but record them so
they don't get lost:

1. **VendorInvoice void/reversal workflow** — Per immutability, can't edit posted VI.
   If wrong, real-world fix = "reversing VI" (mirror of credit note for AP). Not in scope
   this sprint; file as `plan.md` tech debt. Realistically rare — most SMEs just don't
   pay a wrong invoice and let it expire / dispute with vendor.
2. **VendorInvoice + tax_adjustment_note for VENDOR-issued CN** — when vendor issues us a
   credit note (their CN that reduces what we owe), we need to record it as a
   `VendorCreditNote` or similar. Mirror of our CN/DN but inbound. Future sprint.
3. **Partial-payment WHT** — if a PV partially settles a VI, the WHT calculation
   becomes proportional. Document the rule in Sprint 6 PR — easiest is "WHT on the
   applied amount's proportion of original net". Not in this migration.
4. **Bank reconciliation for PV** — PV says Cr Bank 10,400 but the actual bank statement
   line lands separately (likely with fees). Bank rec is a separate slice (plan §10).

I'll add a short "Future Phase-2 AP work" section to `plan.md` after you confirm — or
you can add it during Sprint 5.5 commit. Your call.

---

## ✅ Sign-off — proceed to migration

The 6 spec items are signed off as written, with refinements in §1 (2 nits), §2 (1
must-do), §3 (2 Sprint-6 flags), §5 (1 UX detail). None requires spec iteration. Build
it.

**Migration order (per your plan):**
1. Entities (`VendorInvoice`, `VendorInvoiceLine`, `PaymentVoucherApplication`)
2. EF configurations (snake_case, RLS filter, immutability snapshot on POST)
3. **One** migration: 3 new tables + `payment_vouchers.vendor_invoice_id` FK +
   `approved_by`/`approved_at` cols + `ck_pv_sod` CHECK + immutability trigger
   `tg_vendor_invoices_immutable_after_post` + nit indexes (§1.A) + settled_amount
   CHECK (§1.B)
4. `IVendorInvoiceService` (`CreateDraftAsync`/`UpdateDraftAsync`/`SetClaimPeriodAsync`/
   `PostAsync`/`VoidAsync`-no-op) + posting via `IGlPostingService`
5. `PaymentVoucherService.ApproveAsync` (B2) + `PostAsync` guards updated +
   one-shot backfill
6. Endpoints: `/vendor-invoices` POST/GET/GET-by-id/POST-post, `POST /payment-vouchers/{id}/approve`
7. Tests:
   - Unit: VI line-level GL calculator (3 cases × the 3 GL branches)
   - Integration: POST VI + check JV balanced + check `tax.input_vat_register` entry
     uses `vat_claim_period`
   - Integration: PV approve → SoD guards (approver ≠ creator) + post-after-approve
     happy path
   - Integration: B2 closed-period × vat_claim_period rejection
   - Integration: ม.82/4 window enforcement (boundary: TI month, +0, +6, +7=reject)
8. Re-verify gates: backend 0/0, all tests + new ones green, no regression. Update
   `runtime-gotchas.md` if anything new surfaces. Then Sprint 5.5 wrap report.

UI stays Sprint 6 (not batched). On 5.5 wrap → I plan Sprint 6.

---

## Acknowledge

Append to `progress.md`:  
`Answer-Sana-Question-Backend5-Followup ✅ signed off — proceed migration. Refinements applied: §1 nits A+B, §2 snapshot lock, §3 Sprint-6 flags, §5 closed-period rejection with helpful error. B2 backfill defensive nit. → Sprint 5.5 build starts.`

---

**Build it. Don't reopen the spec unless an implementation discovery contradicts it.**
