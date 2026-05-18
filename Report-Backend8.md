# Report-Backend8 — Sprint 6 Wrap (Purchase completion: settle + ภ.พ.30 + UI + e2e)

**Date:** 2026-05-16 · **Sprint:** 6 (4 phases, gated, not bundled) · **Author:** Claude Code
**Prev:** [Answer-Sana-Backend7] · [Report-Backend7.md](./Report-Backend7.md)
**Next:** Sana plans the next sprint. No scope creep taken (no Quotation/PND3/FixedAssets).

---

## 1. Executive summary

All 4 phases built in the locked order, **each gated before the next** (6A∥6B,
6C after both, 6D after 6C). No spec contradiction surfaced → no Question-Backend6
needed. The gate again caught real issues (3) before they could ship.

| Gate (final) | Result |
|---|---|
| Backend build | 0 / 0 |
| Backend tests | Domain 32/32 · **Api 27/27** · 0 fail · 0 skip · **0 regression** |
| Frontend `tsc` | exit 0 |
| `next build` | exit 0 (6 purchase routes) |
| Playwright e2e | **11 / 11** via system Edge — **8 behavioral** (4 prior + record-vendor + 3 new Sprint-6) + 3 capture |
| Screenshots | 5 (`frontend/screenshots/s6-*.png`) |

## 2. What shipped, per phase

**6A — PV-settles-VI GL branch + settled roll-up.** `CreatePaymentVoucherRequest`
+`VendorInvoiceId`. `PostAsync`: when set → load VI (tenant-filtered), guard
Posted + same-company + no-over-settle (0.01 tol), insert
`payment_voucher_application`, `vi.SettledAmount +=` (stored, never SUM),
`SettlementStatus` UNPAID→PARTIAL→PAID, `IConcurrencyVersioned` blocks
double-settle. GL: `VendorInvoiceId` set → **Dr AP 2110** (expense already booked
at VI post) / Cr WHT / Cr Bank; standalone PV unchanged. 7 tests
(standalone/full/partial/over-settle/not-posted/cross-tenant/**concurrency**).

**6B — input-VAT register re-point (§4 seam from Report-Backend7).** Confirmed
`tax.input_vat_register` is a **computed query, not a table** → no migration.
`VatReportService` purchase side now sources `VendorInvoices` WHERE
`Status=Posted AND VatClaimPeriod==yyyymm AND VatAmount>0` (1 row/VI; legal refs =
vendor TI no/date snapshot); dropped the PV/`DocDate` source. 4 tests (two-period
filter, non-rec excluded, Draft excluded, **claim≠doc_date**).

**6C — UI.** `DocStatus`+`Approved`; `StatusBadge`+`Approved`; sidebar +Vendor
Invoices. `/vendor-invoices` list + new (`VendorSelector`, vendor-TI no/date
editable, doc_date locked, **ม.82/4 claim-period picker [TI..+6]**, per-line
`ExpenseCategorySelector` + ⚠ non-rec, PostConfirm) + detail (Post if Draft,
**Settle-with-PV** if Posted&!PAID, settlement progress bar). `/payment-vouchers/
new` (PV create; `?fromVendorInvoiceId` prefill → settle). PV detail +Approve
(Draft, SoD-hinted) +Post (Approved) + approved-by/at + settling-VI link; defer
banner removed. i18n th/en. Types/queries hooks.

**6D — e2e + screenshots.** `record-vendor-invoice`, `payment-voucher-with-wht`
(SoD: **admin creates → approver approves+posts**, then 50 ทวิ exists + its PDF
HTTP 200), `pv-sod-violations` (creator self-approve blocked, stays Draft).
5 screenshots; theme fidelity clean (Answer-Sana §5.4 — nothing to flag).

## 3. Enabling backend changes (necessary wiring, not new scope)

- **PV line `ExpenseAccountId` → `long?`**, falls back to the PV's expense-category
  default (mirrors VI; the PV-create UI has no account picker by design).
- **PV line `WhtTypeId` → category `DefaultWhtTypeId` fallback** (CLAUDE.md §12.1
  "category auto-fills default WHT type"). Without it a WHT line can't issue its
  50 ทวิ — required for the Sana-mandated `payment-voucher-with-wht` e2e.
- **Seeds (missing Phase-1 data, idempotent):** `150` default expense categories
  (plan §17.3, incl. **ENT non-recoverable** for the ม.82/5 ⚠ path) — the VI/PV
  flow was previously unusable in any env without these; `160` `approver` user
  (DEV/SMOKE, the SoD second actor); `170` SVC category → SVC WHT-type link.

These are documented behaviors that were simply unwired because PV-create UI is
new this sprint — flagged here so they're not read as scope expansion.

## 4. Bugs caught by the gate

1. **Playwright `selectOption({label})` requires a string, not a regex** — broke
   all 3 new specs + screenshots at the category `<select>`. Fixed to exact labels.
2. **sonner success toast intercepts a *following* click** — the post-Approve
   toast overlaid the Post button → `locator.click` pointer-intercept timeout.
   Force-click on the verified target. **New runtime-gotcha (see §6).**
3. **Test ExpenseCategory codes used `Random.Next(100,999)`** — collided on the
   re-used `teas_test` DB once the suite grew (runtime-gotchas §14 class:
   non-idempotent fixed-ish keys). Switched to `Guid` suffix (as vendor codes
   already do). This is what produced the transient "26/27" — **not** the
   WhtTypeId change; final run 27/27.

No app defects shipped. (1) and (3) were test-quality; (2) is a real but cosmetic
UX overlap (flagged, not re-themed — Sana owns the UX call).

## 5. Files

**Backend modified:** `Application/Purchase/PaymentVoucherDtos.cs`
(`VendorInvoiceId`, `ExpenseAccountId` nullable, `PaymentVoucherApprovedResult`
already 5.5), `Infrastructure/Purchase/PaymentVoucherService.cs` (settle block +
account/WHT category fallback), `Infrastructure/Ledger/GlPostingService.cs`
(PV-settles-VI Dr-AP branch), `Infrastructure/Reports/VatReportService.cs`
(purchase side → VI.vat_claim_period), `Application/Purchase/PurchaseReadDtos.cs`
+ `PaymentVoucherService.Read.cs` (PV detail +VendorInvoiceId/ApprovedBy/At).
**Backend new SqlScripts:** `150_seed_expense_categories.sql`,
`160_seed_approver_user.sql`, `170_link_expense_category_default_wht.sql`.
**Frontend new:** `app/(dashboard)/vendor-invoices/{page,new/page,[id]/page}.tsx`,
`app/(dashboard)/payment-vouchers/new/page.tsx`, `e2e/{record-vendor-invoice,
payment-voucher-with-wht,pv-sod-violations,screenshots-sprint6}.spec.ts`.
**Frontend modified:** `lib/{types,queries}.ts`, `components/ui/StatusBadge.tsx`,
`components/app-shell/SidebarNav.tsx`, `app/(dashboard)/payment-vouchers/
{page,[id]/page}.tsx`, `e2e/_helpers.ts`, `messages/{th,en}.json`.
**Tests modified:** Sprint1/Sprint55/Sprint6Settlement/Sprint6VatRegister
(Guid-unique category codes; Sprint1 PV test already on B2 from 5.5).

## 6. New runtime-gotcha for Sana to append (doc is Sana-owned)

**§16 — Playwright: a toast/snackbar intercepts the *next* click.** Symptom:
`locator.click` times out with *"<section aria-label='Notifications'> … subtree
intercepts pointer events"* even though the target resolved + is enabled. Cause: a
sonner/toast rendered by the *previous* action overlays the action bar for its
display duration. Fix: wait for `[data-sonner-toast]` to detach, OR (when the
target is verified) `click({ force: true })`, OR dismiss the toast first.
Prevention: any e2e step that clicks immediately after an action that fires a
success toast must account for the overlay. (Also: Playwright `selectOption`
`label`/`value` are **exact strings**, never regex — `{label:/x/}` throws
"expected string, got object".)

## 7. Honest gaps / flags

1. **Purchase RBAC seed gap (pre-existing, NOT introduced here):**
   `110_seed_roles_and_permissions.sql` never created the
   `purchase.payment_voucher.{create,post,read}` / `purchase.wht.read` permission
   rows nor granted them to non-super roles (only `140` added VI perms + approve).
   Effect: today only **super-admins** can create/post PVs; a normal AP_CLERK
   can't. The `160` approver fixture is super-admin to sidestep this for e2e.
   **Recommend a Phase-2 RBAC-seed completeness pass** (`plan.md`). Not blocking.
2. **Toast overlay** (§4.2 / §6) — cosmetic UX, flagged in `plan.md`, not fixed.
3. **WHT type is category-derived only** — the PV-create UI has no explicit WHT
   type picker; it relies on the expense category's default (CLAUDE.md §12.1). A
   category with no default + a WHT rate would fail at post with a clear domain
   error. Acceptable per spec; an explicit override picker is future polish.
4. e2e = 8 critical paths (Sprint discipline; no gold-plate). Multi-line VI, the
   ENT ⚠ click-path, and PV-settles-VI *via the UI button* are covered by
   integration tests / capture, not a dedicated behavioral spec.
5. Long-path workaround unchanged (`subst U:`, `-m:1`); `code/` canonical; synced.

## 8. Status

Sprint 6 **done done** — 4 phases gated, backend 0/0 + **59 tests** (27 Api +
32 Domain, 0 regression), frontend tsc 0 + prod build, **Playwright 11/11** on the
real stack (admin→approver SoD proven end-to-end through the UI, 50 ทวิ PDF 200),
5 screenshots eyeballed clean. PV-settles-VI + ภ.พ.30-by-claim-period + the full
VI/PV/approve UI are live. Escalation discipline intact (no improvisation; the
necessary category-default wiring is documented behavior, flagged). Awaiting
Sana's next-sprint direction (the §7.1 Purchase-RBAC seed pass is the natural
next backend item; or Trial Balance / ภ.พ.30 / Quotation chain per Answer-Sana-
Backend7 §"Sprint 6 main thing").
