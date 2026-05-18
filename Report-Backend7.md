# Report-Backend7 — Sprint 5.5 Wrap (VendorInvoice + B2 PV approval, backend)

**Date:** 2026-05-16 · **Sprint:** 5.5 (backend structural) · **Author:** Claude Code
**Prev:** [Question-Backend5-Followup.md](./Question-Backend5-Followup.md) ·
[Answer-Sana-Question-Backend5-Followup.md](./Answer-Sana-Question-Backend5-Followup.md)
**Next:** Sana plans Sprint 6 (UI + e2e). UI intentionally NOT in this sprint (not batched).

---

## 1. Executive summary

Built strictly to the signed-off spec + 5 refinements, in the locked order 1-8.
All gates green on the real stack. One predicted, intentional behaviour change (the
PV-post hardening test now needs an Approve step — B2). One seam flagged for Sprint 6
(ภ.พ.30 input side still PV-date-based). Spec NOT reopened — no real contradiction;
the seam is wiring, deferred per §3.

| Gate | Result |
|---|---|
| Backend build | 0 / 0 |
| Backend tests | Domain 32/32 · **Api 16/16** (10 + **6 new**) · 0 fail · 0 skip |
| DbInitializer (teas_app) | EF migration + `060` + `140` applied clean; `/vendor-invoices` 401-gated |
| Regression | 0 unintended — PV-post test updated to B2 workflow (deliberate) |

## 2. Refinements applied (Answer-…-Followup)

- **§1A** `ix_vendor_invoices_vat_claim_period (company_id, vat_claim_period)` —
  note: `vat_claim_period` lives on the **header** (`vendor_invoices`), so the index
  is on that table (named `ix_vendor_invoices_vat_claim_period`); Sana's `ix_vil_*`
  shorthand = same intent (ภ.พ.30 lookup).
- **§1B** `ck_vi_settled`: `settled_amount >= 0 AND settled_amount <= total_amount + 0.01`.
- **§2** `is_recoverable_vat`/`is_capex`/`is_cogs` SNAPSHOT onto `vendor_invoice_lines`
  at draft-create; GL reads the line snapshot, **never** re-resolves the category.
- **§4** default `vat_claim_period` = period of `vendor_tax_invoice_date`.
- **§5** closed claim-period → reject `vi.claim_period_closed`; error names the next
  OPEN period inside the ม.82/4 window. (Tested.)
- **§6** B2 backfill skips `posted_by IS NULL` and rows where `created_by = posted_by`
  (would violate `ck_pv_sod`; CHECK permits NULL → left NULL, defensive).

## 3. Spec/codebase reconciliation (no contradiction, FYI)

- **"ONE migration … + immutability trigger"**: per CLAUDE.md §5.4, triggers / RLS /
  seed are raw-SQL `Migrations/SqlScripts/*` applied idempotently by `DbInitializer`
  (exactly how `tax_invoices` immutability ships — `040`). So: the **EF migration**
  carries the 3 tables + columns + FKs + `ck_pv_sod` + `ck_vi_settled` + the index;
  the **trigger `tg_vendor_invoices_immutable_after_post` + RLS** = new SqlScript
  `060`; **VI prefix + permissions + B2 backfill** = new SqlScript `140`. This is
  one schema-change *unit* and matches the established pattern — flagged so it's not
  read as a deviation.
- **`DocumentStatus.Approved`** added to the shared enum (PV-only consumer; other docs
  skip it). String-stored `'APPROVED'`. `PaymentVoucher.MarkPosted` now requires
  `Approved` (was `Draft`) — this is the B2 workflow, and is why the Sprint-1 PV
  hardening test was updated to approve (as a *different* user) before posting. Not a
  regression — the old direct Draft→Posted was the thing B2 forbids.

## 4. 🔶 Seam flagged for Sprint 6 (not a blocker, not improvised)

`VatReportService.GetRegisterAsync` builds the **purchase** (input-VAT) side from
`PaymentVouchers` filtered by `p.DocDate` — there is **no `tax.input_vat_register`
table**; the register is computed. The spec's "input_vat_register reads
`vat_claim_period`" describes the *intent*. For ภ.พ.30 to be legally correct, the
purchase side must source from **`VendorInvoice` lines bucketed by `vat_claim_period`**
(ม.82/4), not PV/`DocDate`. I did **not** silently rewrite VatReportService — that
re-point belongs with the PV-settles-VI wiring, which Answer §3 explicitly defers to
Sprint 6. The Sprint-5.5 test asserts the legal intent directly (claim-period stored
+ window + closed-period rejection); it does not assert through VatReportService
(which is still PV-based). **Action: Sprint 6 — re-point VatReportService purchase
side to `VendorInvoice.vat_claim_period`.** (In `plan.md`.)

## 5. What shipped (locked order 1-8)

1. **Entities:** `VendorInvoice`, `VendorInvoiceLine`, `PaymentVoucherApplication`;
   `PaymentVoucher` += `VendorInvoiceId`/`ApprovedBy`/`ApprovedAt` + `MarkApproved`
   (SoD guard) + `MarkPosted` now requires `Approved`. `DocumentStatus.Approved`.
2. **EF configs** (snake_case via convention): VI/VIL/PVA + PV amendments
   (`ck_pv_sod`, FK, filtered index); VI `ck_vi_settled` + `ix_vendor_invoices_vat_claim_period`.
3. **One EF migration** `20260516130856_Add_VendorInvoice_And_PvApproval`; SqlScripts
   `060_vendor_invoice_immutability_rls.sql` (trigger + no-delete + RLS),
   `140_seed_vendor_invoice_prefix_and_pv_approve.sql` (VI prefix, 4 perms, grants,
   B2 backfill).
4. **`IVendorInvoiceService`/`VendorInvoiceService`(+`.Read.cs`)**: CreateDraft (vendor +
   per-line category snapshot + ม.82/4 default/validate), UpdateDraft, SetClaimPeriod,
   Post (period gate + §5 closed-claim hint + VI-NNNN + GL), List/GetDetail.
   `GlPostingService.PostVendorInvoiceAsync` (Dr Expense/Asset · Dr InputVAT 1170 for
   recoverable · non-rec lumps VAT into expense ม.82/5 · Cr AP 2110).
5. **`PaymentVoucherService.ApproveAsync`** (SoD app-level; DB `ck_pv_sod` backstop);
   PostAsync now requires Approved.
6. **Endpoints:** `/vendor-invoices` (POST, PUT, POST /{id}/claim-period, POST
   /{id}/post, GET, GET /{id}); `POST /payment-vouchers/{id}/approve`; perms
   `Purchase.VendorInvoice{Create,Post,Read}` + `PaymentVoucherApprove`; DI.
7. **Tests (6 new, all green):** VI GL × 3 branches (full-recoverable / non-recoverable
   lump / no-VAT, each balanced JV); ม.82/4 window (default = TI month, +6 ok, +7
   reject); §5 closed-claim → `vi.claim_period_closed` naming next open period; B2
   (creator-self-approve → `pv.sod_violation`, post-before-approve → `pv.not_approved`,
   different-user approve then post → ok). PV-post hardening test migrated to B2.
8. **Gates re-verified** — table in §1; DbInitializer smoke on `teas_app` clean.

## 6. Files

**New:** `Domain/Entities/Purchase/{VendorInvoice,VendorInvoiceLine,PaymentVoucherApplication}.cs`;
`Application/Purchase/{VendorInvoiceDtos,IVendorInvoiceService}.cs` (+ `PaymentVoucherDtos`
`PaymentVoucherApprovedResult`); `Infrastructure/Purchase/{VendorInvoiceService,VendorInvoiceService.Read}.cs`;
`Infrastructure/Persistence/Configurations/Purchase/VendorInvoiceConfiguration.cs`;
`Api/Endpoints/VendorInvoiceEndpoints.cs`;
`Migrations/20260516130856_Add_VendorInvoice_And_PvApproval*.cs`;
`Migrations/SqlScripts/{060_vendor_invoice_immutability_rls,140_seed_vendor_invoice_prefix_and_pv_approve}.sql`;
`tests/.../Hardening/Sprint55VendorInvoiceTests.cs`.
**Modified:** `Domain/Enums/DocumentStatus.cs`, `Domain/Entities/Purchase/PaymentVoucher.cs`,
`Application/Ledger/IGlPostingService.cs`, `Application/Purchase/IPaymentVoucherService.cs`,
`Infrastructure/Ledger/GlPostingService.cs`, `Infrastructure/Purchase/PaymentVoucherService.cs`,
`Infrastructure/Persistence/{AccountingDbContext,Configurations/Purchase/PaymentVoucherConfiguration}.cs`,
`Api/Authorization/Permissions.cs`, `Api/Program.cs`, `Infrastructure/DependencyInjection.cs`,
`tests/.../Hardening/Sprint1HardeningTests.cs` (B2 approve step + `userId` provider).

## 7. Honest gaps / flags

1. **No UI this sprint** (Sana: not batched). `/vendor-invoices` + PV-approve +
   PV-create UI and the 2 full e2e (`record-vendor-invoice`, `payment-voucher-with-wht`)
   = Sprint 6.
2. **§4 seam** (above): VatReportService purchase side not yet re-pointed — Sprint 6.
3. **PV-settles-VI** (Dr-AP GL branch, `settled_amount` roll-up,
   UNPAID→PARTIAL→PAID, WHT base = net) — tables/link shipped, wiring = Sprint 6 (§3).
4. **No new runtime-gotcha.** The PV-post test break was *predicted and intentional*
   (B2 workflow), not a latent bug — recorded here, nothing for `runtime-gotchas.md`.
5. `bank_account` master + 3-way match remain tech debt (`plan.md`, confirmed cuts).
6. Long-path workaround unchanged (`subst U:`, `-m:1`); `code/` canonical; mirror synced.

## 8. Status

Sprint 5.5 **done done** — backend built to signed-off spec, 48 tests green
(32+16), DbInitializer applies the migration + both SqlScripts cleanly on a real DB,
SoD enforced app + DB, ม.82/4 + §5 covered by tests. Escalation discipline intact
(spec sign-off gate honoured; seam flagged not improvised). Awaiting Sana's Sprint 6
plan (UI + e2e + the §3/§4 wiring).
