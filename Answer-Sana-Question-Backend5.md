# Answer-Sana-Question-Backend5 — Sprint 5 backend-gap decisions

**Date:** 2026-05-16  
**From:** Ham (via Sana, Cowork)  
**To:** Claude Code  
**Re:** [Question-Backend5.md](./Question-Backend5.md) — VendorInvoice absent + PV SoD absent

> **6th escalation save.** My "backend exists, just UI" premise in Answer-Sana-Backend5 was
> wrong by half. Glad you grep'd before improvising — building VendorInvoice + GL/period rules
> from a vibe would have been bad. Decisions below: build it properly, don't shortcut compliance.

---

## B1 — VendorInvoice backend: **B1-A (build it properly)** ✅

You're right that VendorInvoice is structural + compliance-affecting. Skipping it
(B1-B) means no Input-VAT register source, no ภ.พ.30 input side, no audit trail of
what we owe vs what we paid — the system can't actually do AP without it. AP-lite was
a bad fallback I shouldn't have implied.

**Build it. Following gates:**

1. **Spec-first** (per your CLAUDE.md §9 instinct): send a 1-page model/GL spec as
   `Question-Backend5-Followup.md` covering:
   - Aggregate root `VendorInvoice` + `VendorInvoiceLine` (mirror `TaxInvoice` /
     `TaxInvoiceLine` shape).
   - GL posting on `PostAsync`:
     - Per recoverable line: **Dr Expense (or CAPEX) · Dr Input-VAT 1170 · Cr AP 2110**
     - Per non-recoverable line (ExpenseCategory.is_recoverable_vat = false, e.g.
       ENT/VEHI): **Dr Expense (full incl. VAT) · Cr AP** — input VAT lumped into
       expense, NOT into 1170.
   - **Input-VAT claim-period rule ม.82/4**: new field `vat_claim_period` (year+month,
     defaults to vendor's TI date; user can override forward up to 6 months from vendor's
     TI date — validate). This is what `tax.input_vat_register` reads from to assign to
     ภ.พ.30 period.
   - Snapshot fields from the vendor's TI (vendor's `tax_invoice_no`, their `tax_invoice_date`)
     — these are the legal references on `tax.input_vat_register`.
   - Link `payment_voucher.vendor_invoice_id` (nullable) + `payment_voucher_application`
     join table (1 PV can settle N VI lines, like Receipt→TI). Many-to-many but the simple
     case is 1:1.
   - Migration adds: `purchase.vendor_invoices`, `purchase.vendor_invoice_lines`,
     `purchase.payment_voucher_applications`, FK from `payment_vouchers.vendor_invoice_id`.
   - RLS: same `company_id` filter pattern.
   - Compliance: same immutability trigger pattern as `tax_invoices` (POST-once, no
     UPDATE/DELETE on critical fields after post).
2. I sign off on the spec **before** you write the migration. If anything in the GL
   posting looks off, we iterate the spec, not the code.
3. **Scope cut:** **NO 3-way match** (PR → PO → GR) this sprint. Small SMEs go straight
   "receive vendor's TI → record VI → pay PV". The 3-way match pattern is a future
   Phase-2 expansion. Document this scope cut in `plan.md` (technical debt).
4. After migration applied + tests pass, build the UI: `/vendor-invoices` list/new/detail,
   reuse 5 components + the just-built ExpenseCategorySelector + VendorSelector.
5. Then PV gains the "settle VI" path (existing standalone PV still works — just an
   additional optional `vendor_invoice_id` field on PV create form).

**On the spec:** when you send Question-Backend5-Followup, include:
- ERD-ish summary of the 3 new tables + 1 FK
- 3 sample GL postings (full-VAT, mixed recoverable/non, non-VAT-mode)
- Worked example for the ม.82/4 claim-period rule (vendor invoice dated 2026-01-15, but
  recorded in our books on 2026-04-20 → can claim in any ภ.พ.30 from 2026-01 through
  2026-07 (6 months from TI date))

I'll sign off in `Answer-Sana-Question-Backend5-Followup.md` typically same day.

---

## B2 — PV approval / SoD: **B2-A (full Draft → Approved → Posted)** ✅

CLAUDE.md §12.1 says "SoD enforced". B2-B (app-level-only check) satisfies the letter
but loses the audit trail of "who approved when" — and real accounting workflows expect
a discrete Approval action that an approver sees in their pending queue.

**Spec for B2-A:**

- **States:** `Draft → Approved → Posted` (linear, no skip).
- **New endpoint:** `POST /payment-vouchers/{id}/approve`.
- **New permission:** `purchase.payment_voucher.approve` (seed in `Permissions.Purchase`
  + 100-permissions seed SQL; grant to Accountant/CFO roles).
- **New fields on `payment_voucher`:** `approved_by BIGINT NULL`, `approved_at TIMESTAMPTZ(3) NULL`.
  (already exist? confirm in migration).
- **App-level guard in `ApproveAsync`:** `pv.CreatedBy != _tenant.UserId` (refuse approve-own-draft).
- **App-level guard in `PostAsync`:** `pv.Status == Approved` (refuse post non-approved)
  AND `pv.ApprovedBy != _tenant.UserId` (this last is debatable — see below).
- **DB CHECK `ck_pv_sod`:** `(approved_by IS NULL) OR (approved_by <> created_by)` via
  SqlScripts + migration. Belt-and-braces with the app check.

**Sub-decision:** can the **approver** also be the **poster**?
- Strict reading of "SoD": no. Three distinct people (creator, approver, poster).
- Pragmatic: many SMEs have 2 people only — accountant creates, manager approves AND
  posts.
- **Decision:** allow approver = poster. Hard requirement is `approved_by ≠ created_by`,
  and `posted_by` only needs to be a user with `payment_voucher.post` permission. So in
  the constraint: just `ck_pv_sod` (approver ≠ creator). Don't add a 3-party check.

**UI implications:**
- PV list shows status badge: Draft / Approved / Posted (use existing StatusBadge).
- Detail page: if Draft + user has `approve` perm → "Approve" button visible.
- After approve: detail page shows "Approved by [Name] at [time]" + "Post" button if
  user has `post` perm.
- ActionConfirmDialog reuse: warning "การ Approve ทำให้เอกสารพร้อม Post — ผู้อนุมัติต้องไม่ใช่ผู้สร้าง"

**Migration sequence:**
1. Add columns `approved_by`, `approved_at` (nullable).
2. Add DB CHECK `ck_pv_sod`.
3. Backfill: existing posted PVs (if any) — set `approved_by = posted_by`,
   `approved_at = posted_at` (one-time data fix; document in migration).
4. New code paths use Approved state going forward.

---

## Q3 — minor confirmations

### Q3.1 — BankAccountSelector: **SKIP confirmed** ✅

Render bank/cheque as plain inputs on the PV create form (when we get there). A
`bank_account` master is a future master-data slice — file as technical debt in
`plan.md`. Currently `payment_voucher.bank_account_id` is a raw `long` reference to
nothing — leave it; null is fine when payment_method != Cheque.

### Q3.2 — 50 ทวิ template: **BUILD per plan §15.10** ✅

No official RD PDF/XML form to match — the layout in `accounting-system-plan.md` §15.10
is the spec. Use QuestPDF (already configured). A4 portrait. Include the standard 50 ทวิ
header per RD format, payee + payer blocks, income category (ม.40(...)), table of
WHT-by-rate lines, signature box. One PDF per `WhtCertificate` row.

**Verify your PDF passes a sanity check:** open it, eyeball next to one of the
existing physical 50 ทวิ forms a Thai accountant has. Layout match isn't pixel-perfect
but field placement should be unambiguous.

### Q3.3 — `/vendors` gotcha #2 fix: **YES, apply** ✅

`int?` + `?? 1` / `?? 50` defaults. Already on your safe-subset list — proceed.

---

## Runtime gotcha §15 (new — your finding)

You called out an e2e selector bug worth adding to `docs/runtime-gotchas.md`. I'll
append:

> **§15. Playwright `getByRole('cell', { name })` — substring match ambiguity**
>
> Symptom: `getByRole('cell', { name: 'V-001' })` matches both the dedicated code
> column AND the name column where 'V-001' appears as a substring of the vendor's
> human name ("Vendor V-001 Acme"). Test fails on "2 elements found".
>
> Fix: pass `{ exact: true }` to force exact match, or target a stable `data-testid`
> instead of relying on cell text.
>
> Prevention: when a row has overlapping text across cells, exact-match the column
> you actually want OR test-id the cell.

I'll add this to `docs/runtime-gotchas.md` (Sana-owned). Don't touch the doc yourself.

---

## Sprint 5 close-out plan (post-this-answer)

Given B1-A + B2-A, Sprint 5's "wrap" is going to bleed into Sprint 6 — that's fine,
we're not on a release deadline. New plan:

### Sprint 5.5 — Backend structural work (B1 + B2)

1. You send `Question-Backend5-Followup.md` with the VendorInvoice model/GL spec
   (per §B1 above).
2. I sign off in `Answer-Sana-Question-Backend5-Followup.md` (same day target).
3. You build:
   - VendorInvoice entity + EF config + migration + posting service + endpoints
   - PV approve workflow (B2-A) — endpoint + permission + migration with `ck_pv_sod`
   - VendorInvoice tests (unit + 1 integration) + PV approve tests
4. Re-verify gates: backend 0/0, all tests + 1 new PV-approve test green, no regression.
5. Update `runtime-gotchas.md` if anything new surfaced.

### Sprint 6 — Purchase UI completion + Phase-2 backend start

1. UI for VendorInvoice list/new/detail
2. UI for PV approve flow (button in detail page)
3. PV create form (now properly with optional `vendor_invoice_id` selector)
4. e2e: `record-vendor-invoice.spec.ts` + `payment-voucher-with-wht.spec.ts` (full path)
5. Screenshots refresh
6. Then start Sprint 6 main thing: pick from {Trial Balance UI / ภ.พ.30 UI / Quotation chain}

Don't try to do Sprint 5.5 + 6 in one batch. Split.

---

## Status summary (what Sana actually sees on the screen)

Sana eyeballed `s5-02-vendor-create.png` after your Sprint 5 wrap — **the UI is good**:
- Sarabun renders clean
- teas theme + sidebar TH labels (ซื้อ section visible with all 3 children)
- 2-column form layout per Design(UI) §3 / §7
- Dropdown (นิติบุคคล), checkbox (จด VAT), TH/EN toggle bottom-left all present
- Disabled Save button state correct on empty form
- No theme clash flagged

Visual fidelity passes. Don't refactor the theme.

---

## Acknowledge

Append to `progress.md`:
`Answer-Sana-Question-Backend5 received — B1=A (build VendorInvoice with spec sign-off gate), B2=A (Draft→Approved→Posted), Q3 confirmed, gotcha §15 noted. Sprint 5.5 starts with Question-Backend5-Followup (VI model/GL spec).`

---

**Don't overthink. Send the spec.**
