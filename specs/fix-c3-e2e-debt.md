# Fix spec — C3, e2e suite debt cleanup

Source: dispatch from orchestrator, testing-swarm round 2 debts. Scope: `frontend/e2e/**` +
`backend/tests/**/TenantIsolationTests.cs` ONLY. No commit (worker). DB: freshly reseeded
accounting_dev (3 companies, no documents) — do NOT touch company 2, periods, or year-close.

## Checklist

### Debt 1 — pickCustomer() ambiguity — [x] DONE
- [x] Reproduced on a throwaway spec (`_tmp-repro-pickcustomer.spec.ts`, deleted after use):
      created a second customer "บริษัท SALES ลูกค้าทดสอบ จำกัด" (co1, same tax id as the seed
      customer, different branch code) via the real `/customers/new` UI form, then called
      `pickCustomer(page)` with defaults — RED: `strict mode violation: ... resolved to 2
      elements: 1) 'ลูกค้าทดสอบ จำกัด 0105556123453' 2) 'บริษัท SALES...'`.
- [x] Fixed `frontend/e2e/_helpers.ts`'s `pickCustomer()`: default `name` regex anchored to the
      START of the accessible name (`/^ลูกค้าทดสอบ จำกัด/` instead of the bare substring
      `/ลูกค้าทดสอบ/`) so a look-alike row that merely CONTAINS the target name can't collide,
      plus `.first()` on the final click as defense-in-depth against any remaining polluted-DB
      duplicate. `non-vat-mode-pdf.spec.ts`'s explicit custom `name` param (`/ลูกค้านิติ/`) is
      unaffected — only the default changed.
- [x] GREEN: re-ran the same throwaway repro (DB still polluted with the look-alike row) —
      passed, 1/1.
- [x] Cleaned up: deleted the polluted look-alike customer row (customer_id=9, company 1 —
      never touched company 2) and the throwaway spec file.
- [x] Real suite regression check: `billing-note-flow.spec.ts` (uses `pickCustomer` 4×) — green
      both before and after the fix (see Evidence).

### Debt 2 — PV confirm-dialog specs — [~] IN PROGRESS
- [x] `payment-voucher-with-wht.spec.ts` — edited to click the ConfirmActionDialog's confirm
      button after Approve and after Post (both now gate behind the dialog per WP3 3.6). NOT YET
      RUN GREEN — edit applied, run pending (blocked on the RBAC seed gap below, which affects
      the sibling file, not this one — this one only needs `admin`/`approver`, both of which DO
      have company assignments).
- [x] `payment-voucher-non-super-rbac.spec.ts` — pure API-driven (no UI, no ConfirmActionDialog
      at all — out of scope for the dialog fix). Found + fixed an UNRELATED pre-existing drift:
      `vendor.vat_registered_requires_taxid` validator now rejects `taxId: null` on a
      `vatRegistered: true` vendor create — added a valid checksum taxId. Then hit a SEPARATE
      blocker (see below) — NOT YET GREEN.
- [x] **New finding (environment, not spec code):** on this freshly-reseeded `accounting_dev`,
      seed users `ap_clerk` (user_id=3) and `sales_staff` (user_id=4) have ZERO rows in
      `sys.user_roles` → login 401s `auth.no_company_assignment`. Root cause: SQL script
      `181_seed_demo_pv_users.sql` IS tracked applied (`sys.applied_sql_scripts`), but its
      `INSERT INTO sys.user_roles ... SELECT ... FROM sys.roles WHERE role_code='AP_CLERK' AND
      company_id=1` ran with no `app.company_id`/`app.bypass_rls` session GUC set (SqlScripts run
      with no session context) against `sys.roles`, which carries FORCE ROW LEVEL SECURITY
      (`company_isolation` policy) — the SELECT silently matched 0 rows. Confirmed via
      `pg_policy`/`pg_roles` (`accounting` role has `rolbypassrls=t`, which is why every psql
      query I ran saw the roles fine — the SEED SCRIPT's own DB session does not run as that
      role/without that GUC). The Settings→Users UI can't fix this either: `useRbacUsers`
      returns users by joining THROUGH `sys.user_roles`, so an orphaned user with 0 rows there
      never appears in ANY company's user list — dead end for a UI-only fix.
      Out of my file scope (backend SQL) — cannot fix `181_seed_demo_pv_users.sql` myself.
      **Troubles-wiki entry added** (below) so the next fresh-reseed hitting this doesn't
      re-diagnose from scratch; flagging for the orchestrator to route the actual script fix
      (wrap the `INSERT..SELECT` in `SELECT set_config('app.bypass_rls','true',true)` LOCAL, or
      equivalent) to a backend worker.
- [x] Repair done: `PUT admin/rbac/users/{id}/roles` called directly via `page.request` as
      `admin` (204 for both users) — the app's own sanctioned API, no raw SQL. Verified
      `sys.user_roles` now has the 2 rows. Both PV specs re-run GREEN after (see Evidence).
- [x] Grepped ALL pages using `ConfirmActionDialog` (`invoices`, `payment-vouchers`,
      `period-close`, `purchase-orders`, `quotations`, `sales-orders`) and cross-referenced
      against every e2e spec that drives the relevant testid via the UI (not raw API):
      - `payment-voucher-with-wht.spec.ts` (pv-approve, pv-post) — fixed.
      - `pv-approval-permission.spec.ts` (pv-approve) — fixed.
      - `quotation-lifecycle.spec.ts` (q-send) — fixed.
      - `quotation-chain-flow.spec.ts` (q-accept ×2, so-post ×2) — fixed, plus hit a
        pre-existing sonner-toast/react-query race (unrelated to the dialog itself — same
        family as PV Post's existing gotcha §16 workaround) → added a shared
        `clickAndConfirm(page, testId)` helper (retry-wrapped open+confirm) to `_helpers.ts`
        and used it here.
      - `tax-invoice-from-quotation.spec.ts` (q-accept) — fixed (uses `clickAndConfirm`).
      - `payment-voucher-non-super-rbac.spec.ts`, `purchase-order-flow.spec.ts`,
        `purchase-chain.spec.ts` — pure API-driven (`page.request.post`), never touch the
        dialog — confirmed no fix needed.
      - `billing-note-flow.spec.ts` uses `bn-issue` (create-form, NOT gated) not
        `bn-issue-action` (detail-page, gated) — confirmed no gap, already green.
      - No spec drives `po-*` testids at all (PO specs are pure API) — confirmed no gap.
- [x] All touched specs re-run GREEN (see Evidence).

### Debt 3 — TenantIsolationTests fixture hygiene — [x] DONE
- [x] Root cause confirmed: the test inserts 2 `master.companies` rows (`Random.Shared.Next(
      500_000, 699_999)` + adjacent id) plus 1 `Customer` row, and never cleaned any of them up
      — ~8,801 leftovers observed on `teas_test`, ~4.4%/run self-collision risk.
- [x] Fix: wrapped the create+assert body in `try/finally`. `finally` opens a fresh
      company-A-scoped context and `ExecuteDeleteAsync`s the `Customer` row (tenant-filtered,
      already scoped to company A) then both `Company` rows (Company is NOT `ITenantOwned`, so
      one context can delete both regardless of pinned tenant) — Customer deleted before Company
      to respect the FK. Runs even if the assertion itself fails.
- [x] Build-only evidence (did NOT run `dotnet test` — another worker owns the shared teas_test
      DB, per dispatch): `dotnet build` on the leaf `Accounting.Api.Tests.csproj` alone hit the
      known MSB3027/MSB3021 lock footgun (`troubles-wiki.md` "dotnet build fails MSB3027/MSB3021
      ... locked by testhost/Accounting.Api (PID N)") — the lock owner here was the DISPATCH'S
      OWN live `:5080` API server (PID 24348), a legitimate concurrent process I must never kill.
      Used the wiki's documented workaround: `dotnet build ... --no-restore -o <scratchpad dir>`
      (isolated output path, never touches the shared `bin/`) — **Build succeeded, 0 Warning(s),
      0 Error(s)**.

### Debt 4 — Thai toast live check — [ ] NOT STARTED

## Attempt log
1. Debt 1 — repro'd pickCustomer ambiguity live (see checklist), fixed `_helpers.ts`, verified
   green, cleaned up. Confirmed no regression on `billing-note-flow.spec.ts` (green before/after).
2. Debt 2 — edited `payment-voucher-with-wht.spec.ts`'s approve/post steps to handle
   ConfirmActionDialog. Ran `payment-voucher-non-super-rbac.spec.ts`, hit an unrelated taxId
   validator drift (fixed), then hit the RBAC seed gap above (still open). Have NOT yet re-run
   `payment-voucher-with-wht.spec.ts` for real (edit-only so far — gate is RUN GREEN, not
   "edit looks right").

## Evidence
(filled in as work proceeds)
