# Answer-Sana-Backend7 — Sprint 6 Plan (AP loop completion + UI + e2e)

**Date:** 2026-05-16  
**From:** Ham (via Sana, Cowork)  
**To:** Claude Code  
**Re:** [Report-Backend7.md](./Report-Backend7.md) — Sprint 5.5 ✅ accepted

> Sprint 5.5 wrap accepted. Spec-first gate worked exactly as designed — 6 spec items
> implemented to spec, 6 new tests, 0 regression, migration clean. The B1.2 escalation
> + sign-off cycle is now proven as the standard pattern for any new aggregate root.
> Reuse this discipline whenever a future sprint touches a new legal/compliance entity.

---

## 0. Sign-off — Sprint 5.5

| Gate | Result |
|---|---|
| Backend build | 0/0 ✅ |
| Tests | 32 + 16 = 48 green, 0 fail/skip ✅ |
| Migration applied clean | ✅ (3 tables + PV cols + ck_pv_sod + ck_vi_settled + ix + FK + triggers/RLS via SqlScripts 060+140) |
| Endpoint auth-gated | ✅ (`/vendor-invoices` 401) |
| Refinements (§1A/B, §2 snapshot, §5 reject+hint, §6 backfill) | ✅ all applied per Answer-Followup |
| `tax.input_vat_register` re-point to `vat_claim_period` | ⚠ **NOT YET — Sprint 6 §A2 below** (correctly flagged, not improvised) |

**6 new tests cover:** VI GL × 3 branches, ม.82/4 window (default/+6 ok/+7 reject),
§5 closed-claim rejection with helpful hint, B2 SoD (self-approve fail, pre-approve post
fail, diff-user approve → post ok). PV-post hardening test migrated to B2 workflow
(intentional, not regression).

---

## Sprint 6 — scope (AP loop closure + UI + e2e)

This sprint is **bigger than a single screen slice** because §3 + §4 backend seams are
load-bearing for the UI semantics — if we ship UI on top of half-correct backend,
users will see wrong ภ.พ.30 numbers. Sequence is **backend wiring FIRST, then UI**.

Split into 4 internal phases, each with its own gate. Don't merge phases.

### Phase 6A — §3 PV-settles-VI GL branch (backend, 1-2 days)

The plumbing that makes PV→VI settlement work end-to-end.

**Backend changes:**
1. `PaymentVoucherService.PostAsync` — branch on `pv.VendorInvoiceId`:
   - If NULL (standalone PV): existing behaviour unchanged — `Dr Expense / Cr Bank / Cr WHT-Payable`
   - If set (PV settles VI): `Dr AP 2110 / Cr Bank / Cr WHT-Payable` (expense already hit at VI POST)
2. **`settled_amount` roll-up** at PV POST (per Answer-Followup §3):
   - At PV POST, for each `PaymentVoucherApplication` row, atomically increment
     `VendorInvoice.settled_amount += application.applied_amount`
   - Transition `settlement_status` based on roll-up:
     - `settled_amount <= 0.001` → UNPAID
     - `0.001 < settled_amount < total_amount - 0.01` → PARTIAL
     - `settled_amount >= total_amount - 0.01` → PAID
   - Must happen inside the PV POST transaction (atomic).
3. **`PaymentVoucherApplication`** validation in `CreateDraft`:
   - Sum of `applied_amount` across all applications cannot exceed
     `VI.total_amount - VI.settled_amount` (no over-settle).
   - Each application's VI must be in same `company_id` (RLS already enforces, but add
     explicit check with friendly error).
   - VI must be `status = Posted` (cannot settle a Draft VI).

**Tests (integration, ≥ 4):**
- Standalone PV (no VI) — existing GL unchanged, no `settled_amount` write.
- PV settles single VI fully — `Dr AP / Cr Bank / Cr WHT-Payable`, VI status → PAID,
  `settled_amount == total_amount`.
- PV partially settles VI — VI status → PARTIAL, `settled_amount > 0 && < total`.
- Over-settle attempt rejected (`applied_amount` total > `total_amount - settled_amount`).
- Cross-tenant attempt rejected.

**Gate:** backend build 0/0, all tests green, `settled_amount` invariants hold under
concurrent PV POST (run the existing concurrency test pattern from Sprint 1
hardening over PVA roll-up).

---

### Phase 6B — §4 VatReportService input side re-point (backend, 1 day)

Fix ภ.พ.30 input-side semantics. **Compliance fix.**

**Current state (per Report-Backend7 §"Flagged"):** `VatReportService` purchase side
keys off `PaymentVoucher.DocDate`. That's wrong — input VAT is claimed when the
**VendorInvoice** is recorded (anchored to `VI.vat_claim_period`), not when paid.

**Backend changes:**
1. `VatReportService.BuildInputVatRegisterAsync(year, month, ...)`:
   - **Source change:** read from `vendor_invoices` joined with `vendor_invoice_lines`
     where `vi.vat_claim_period == year*100 + month` AND `vi.status = Posted` AND
     `vil.is_recoverable_vat = true` AND `vil.vat_amount > 0`.
   - **Drop** the previous PV-based source for the input-side register.
   - Aggregate per VI (not per VIL line) — one row per vendor invoice, summing
     recoverable VAT.
   - Snapshot fields used in the register:
     - `vendor_tax_invoice_no` (vendor's TI number, the legal reference)
     - `vendor_tax_invoice_date` (vendor's TI date — what RD audits)
     - `vendor_tax_id`, `vendor_branch_code`, `vendor_name`
     - `subtotal_amount` (the recoverable net base)
     - SUM of `vil.vat_amount` where recoverable
2. **Migration `tax.input_vat_register`** — re-key the table to (vendor_invoice_id,
   vat_period) if it stores entries. If it's purely a materialized SELECT for ภ.พ.30
   generation (i.e., not a stored table), this is just a query change with no
   migration.
   - **Check first** (your call which it is in the current implementation).

**Tests:**
- Integration: 3 VIs across 2 vat_claim_periods (202604, 202605) → register for
  202604 returns only the matching VIs, sum = expected.
- Integration: VI with non-recoverable lines → only recoverable lines in register
  (non-rec excluded).
- Integration: VI in Draft state → NOT in register (only Posted contributes).
- Integration: VI's `vat_claim_period` ≠ VI's `doc_date` month — confirm register
  uses `vat_claim_period` not `doc_date`.

**Gate:** all VAT-report tests green, ภ.พ.30 preview (existing endpoint) returns
correct totals.

---

### Phase 6C — VendorInvoice UI + PV approve UI (frontend, 2-3 days)

Reuse all 5 form components + the new VendorSelector + ExpenseCategorySelector built
in Sprint 5.

**New pages:**
1. **`/vendor-invoices` list** — DataTable + filter (vendor, date range, status,
   settlement_status), cursor paginate. Reuse pattern from `/tax-invoices`.
2. **`/vendor-invoices/new` form:**
   - VendorSelector (existing)
   - Vendor's `tax_invoice_no` (free text) + `tax_invoice_date` (DateInput, NOT
     locked-today — this is the vendor's date, not ours)
   - `doc_date` locked to today (our recording date)
   - `vat_claim_period` selector:
     - Show as month-year picker
     - Default: month of `vendor_tax_invoice_date`
     - Constrain options to `[TI month .. TI month + 6]` (7 periods)
     - If selected period is closed → inline error with helpful hint per §5
       ("กรุณาเลือก {next open period in window: X, Y, ...}")
   - LineItemsTable (existing) extended:
     - per line: ExpenseCategorySelector (existing) → auto-fills `is_recoverable_vat`
       + `is_capex` + `expense_account_id` defaults
     - VAT auto-calc per line based on tax_code + line amount
     - Show ⚠ "ภาษีซื้อต้องห้าม" inline when category is non-recoverable
   - PostConfirmDialog: warning per immutability rule
3. **`/vendor-invoices/[id]` detail:**
   - Render the VI (similar to TI detail)
   - "Post" button if status=Draft
   - "Settle with PV" button if status=Posted && settlement_status≠PAID — navigates
     to `/payment-vouchers/new?fromVendorInvoiceId={id}` (prefills the PVA)
   - Settlement progress badge: "Settled X% (฿Y / ฿Z)"
4. **`/payment-vouchers/[id]` detail enhanced:**
   - Show current status badge (Draft / Approved / Posted) — use existing StatusBadge
   - "Approve" button if status=Draft AND user has `purchase.payment_voucher.approve`
     perm AND user ≠ creator
   - "Post" button if status=Approved AND user has `purchase.payment_voucher.post` perm
   - Disabled state with hint when button can't show (e.g. "ผู้สร้างไม่สามารถ approve
     เอกสารตัวเองได้")
   - Show `approved_by` + `approved_at` if approved
5. **i18n th/en updates** in `messages/{th,en}.json`:
   - New labels for vat_claim_period, settlement_status, approve action, etc.

**Gate after 6C:** `tsc` exit 0, `next build` 0 errors, manual click-through of all
new screens via dev server.

---

### Phase 6D — e2e + screenshots + verify (1 day)

**e2e specs (the 2 that were paused in Sprint 5 + 1 new):**

1. **`record-vendor-invoice.spec.ts`** (originally paused) — full path:
   - login → /vendor-invoices/new
   - VendorSelector pick → enter vendor TI no/date → vat_claim_period selected (default
     = TI month, accept) → 1 line: expense category PROF, amount 10000, tax VAT7
   - Verify computed totals (subtotal 10000, vat 700, total 10700)
   - Click Post → PostConfirm → confirm
   - Redirect to detail page → verify `VI-NNNN` displayed + status=Posted
   - Navigate to /reports/vat-input-register?year=&month= → verify the new VI row
     appears with sum=700 (closes Phase 6B circle)

2. **`payment-voucher-with-wht.spec.ts`** (originally paused) — full path:
   - Setup: a posted VI exists (programmatic or seeded)
   - Login as user-A → /payment-vouchers/new?fromVendorInvoiceId={VI.id}
   - Form prefilled with VI snapshot + PVA applied_amount = VI.total
   - Category prefilled (PROF in fixture) → WHT type auto = 3% on NET 10000 → 300
   - Submit Draft (status=Draft)
   - Logout, login as user-B (has approve perm, ≠ user-A)
   - Open the PV → click Approve → confirm → status=Approved
   - User-B clicks Post (allowed since approver MAY = poster) → confirm
   - Verify PV status=Posted, JV balanced (Dr AP 10700 / Cr Bank 10400 / Cr WHT 300)
   - Verify VI status flips PAID + settled_amount=10700
   - Verify 50 ทวิ available at /wht-certificates/{whtId}/pdf (binary 200)

3. **NEW: `pv-sod-violations.spec.ts`** — compliance regression guard:
   - User-A creates PV draft
   - User-A clicks Approve → expect error "ผู้สร้างไม่สามารถ approve เอกสารตัวเองได้"
   - DB still shows status=Draft (no transition leaked)

**Screenshots (4-5):**
- s6-01 — /vendor-invoices list (with at least 2 rows of mixed statuses)
- s6-02 — /vendor-invoices/new form filled out, vat_claim_period dropdown open showing
  the 7-period window
- s6-03 — /vendor-invoices/[id] detail showing settlement_status=PARTIAL
- s6-04 — PV detail with Approve button visible (logged in as approver, not creator)
- s6-05 — PV detail post-approve showing "Approved by [name]" + Post button

**Re-verify gates:**
- Backend build 0/0
- All tests (Domain 32, Api ≥ 22 — 16 from 5.5 + 6 from Phase 6A + integration from 6B + new e2e-supporting tests)
- `tsc` exit 0
- `next build` 0 errors
- Playwright: now 8 specs (5 from Sprint 5 + 3 from this sprint)
- 0 regression
- Update `runtime-gotchas.md` if anything new surfaces

---

## Sprint 6 dependencies graph

```
6A backend §3 ─┐
               ├─→ 6C UI (depends on §3 settled_amount + PV branch)
6B backend §4 ─┘
               ├─→ 6D e2e (depends on §4 register correctness)

6C UI ──────────→ 6D e2e (e2e drives the screens)
```

Don't start 6C until 6A + 6B are gate-green. Don't start 6D until 6C is gate-green.

---

## Sana side-work (parallel, non-blocking)

While you're on 6A-6D, I'll:

1. **`docs/plan.md`** — append "Future Phase-2 AP work" section with the 4 items from
   Answer-Followup §7 (vendor void/reversal, vendor-issued CN, partial-payment WHT
   prorated, bank reconciliation).
2. **`docs/api/openapi.yaml`** — add `/vendor-invoices` GET/POST/{id}/{id}/post and
   `/payment-vouchers/{id}/approve` after you ship them in 6A/6C. Will mirror your
   shipped contract per the established pattern.
3. **TH copy review** of new `messages/th.json` keys you add in 6C — same first-pass
   trust + ping me only if you're unsure of a term.

None blocks you.

---

## Things deliberately NOT in Sprint 6

- 3-way match (PR → PO → GR) — already filed as tech debt per Sprint 5.5
- Vendor-issued credit note (their CN reducing what we owe) — future
- Partial-payment WHT proration — future
- Bank reconciliation — future
- Quotation → SO → DO sales pre-flow — future (this is the eventual Sprint 8+ work)
- ภ.ง.ด.3 / 53 returns UI — future
- Trial Balance / P&L UI — future
- Fixed Assets — future

Don't get tempted to bundle. Sprint 6 is dense enough.

---

## Acknowledge

Append to `progress.md`:
`Answer-Sana-Backend7 received — Sprint 6 = 4 phases (6A §3 PV-settles-VI, 6B §4 input-register re-point, 6C VI+PV-approve UI, 6D e2e+screenshots). Start 6A.`

When 6A done → ack continues with "6A green, starting 6B"; same per phase. On 6D
green → `Report-Backend8.md` with 4-5 screenshots.

If anything in 6A or 6B contradicts the §3/§4 spec from Answer-Followup, flag
**before** writing the code — same `Question-Backend{N}.md` discipline.

---

**Ship it. AP loop closes at end of Sprint 6.**
