# REPORT — VAT dummy company + untested-path round (2026-07-18)

Handoff: `HANDOFF-vat-dummy-company-test.md`. Prod v1.21.5.
Round: create VAT-enabled dummy company → payroll Post full chain → VAT sales chain → ภ.พ.30.

## Findings

| # | Severity | Area | Finding | Status |
|---|----------|------|---------|--------|
| F-1 | **CRITICAL** | Onboarding / RLS | Company creation is **broken on prod since the 600 RLS hardening (2026-07-08)**: `CompanyService.CreateAsync` was missed by the superadmin-tenant-scope Family-B inventory. It writes branch / company_profile / CoA / tax codes / WHT types / expense categories / roles for the NEW company while the DB session is still pinned to the CALLER's company → RLS 42501 on `master.branches`. Worse: the method has **no wrapping transaction**, so the first `SaveChangesAsync` (companies row — no RLS) COMMITS and everything after is lost → **half-created tenant** (company row exists; 0 branches, 0 CoA, 0 tax codes, 0 roles). Every new-customer onboarding on prod fails this way. | **FIXED + VERIFIED LIVE** — 4b92efd (tx wrap + LOCAL company_id pin; CompanyCreateRlsTests red→green; Opus APPROVE) → v1.21.6 deployed (DEPLOY_OK 10/10 probes, DB backup, sql_scripts 69 unchanged) → orphan co4 deleted → dummy recreated via UI = company 5 seeded 1 branch / 25 CoA / 12 tax codes / 15 WHT / 19 expcat / 11 roles (matches co2/co3) |
| F-2 | Low | FE | On the create-company 500, FE shows generic "An unexpected error occurred" toast and the modal stays open inviting resubmits (blocked only by the duplicate-taxId guard). Root fix is F-1; FE-side generic-500 toast is acceptable. | Log only |

### F-1 evidence
- UI: `POST /api/proxy/companies` click #1 at 11:58:12 → error toast; company row WAS created (ID 4). Retries → 422 `company.duplicate`.
- pm2 `teas-api` log 2026-07-18T11:58:12: `Npgsql.PostgresException 42501: new row violates row-level security policy for table "branches"` (DbUpdateException in `AccountingDbContext`).
- Prod DB after: `master.companies` has id=4 (บริษัท ทดสอบ VAT (DUMMY) จำกัด) but branches/coa/tax_codes per company: co2 = 1/25/12, co3 = 1/25/12, **co4 = 0/0/0**.
- Same class as the 2026-07-09 log entry `42501 ... "chart_of_accounts"` (earlier unnoticed occurrence).
- Root cause file: `backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs` `CompanyService.CreateAsync` (~line 186): 4 sequential `SaveChangesAsync` + `sys.seed_company_roles()` with no transaction and no tenant-context pin. `600_superadmin_scoped_rls.sql` G1 tables it writes (chart_of_accounts, tax_codes, wht_types, expense_categories) have **no bypass arm at all**, branches (G2) has one but nothing pins it.
- Why tests never caught it: teas_test connects as Postgres SUPERUSER → RLS bypassed (memory: rls-masked-by-superuser-tests). `OnboardingFoundingAddressTests` passes vacuously.

### F-1 fix (spec `specs/fix-company-create-rls-atomic.md`)
Wrap `CreateAsync` in ONE transaction; after the companies-row `SaveChangesAsync` allocates the new id, pin `set_config('app.company_id', <newId>, true)` (LOCAL, auto-reverts at commit) so all seeding writes run AS the new tenant — passes every `company_isolation` policy naturally. Zero DDL / zero new SqlScript / zero policy weakening. Model pattern already in-repo: `VatRegisterSnapshotJob.cs:95-98`; spec 600 itself lists the company-id LOCAL pin as the "tighter alternative" mechanism. Repair: delete orphan co4 row (zero children), recreate via UI post-deploy.

## Test progress
- [x] Step 0.1 — login OK, user is Super Admin, create-company UI = /settings/companies (superadmin)
- [~] Step 0.2 — dummy company created but HALF-SEEDED (F-1); recreate after fix deploy
- [ ] Step 0.3 verify tax codes · 0.4 minimal master data
- [ ] Plan A payroll full chain · Plan B VAT chain · Plan C ภ.พ.30/misc
