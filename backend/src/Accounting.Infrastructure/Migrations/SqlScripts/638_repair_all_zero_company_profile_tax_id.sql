-- R2/L1-1 (PLAN-fix-findings-r2.md, Unit U1) — Ham's decision: ภ.ง.ด.1/ภ.ง.ด.1ก/สปส.1-10
-- filings resolve the employer's Tax ID as `prof?.TaxId ?? c?.TaxId ?? ""`
-- (Pnd1FilingService.cs / SsoFilingService.cs) — `??` only substitutes on NULL, and
-- master.company_profile.tax_id is NOT NULL, so 637's repair of master.companies.tax_id never
-- reaches a filing whose company_profile row still holds the placeholder. company 1's
-- company_profile row was seeded by 420_seed_company1_profile.sql with tax_id =
-- '0000000000000' (mirroring companies' own original placeholder) and 637 never touched
-- company_profile at all — confirmed a real rendered ภ.ง.ด.1 PDF still shows the placeholder
-- (findings-r2/findings-leg1.md, L1-1). This script closes that ONE desync.
--
-- Targeting logic mirrors 637 EXACTLY (literal WHERE, not a hardcoded company id):
--   UPDATE master.company_profile SET tax_id = '0105000000012' WHERE tax_id = '0000000000000';
-- Verified against accounting_dev + teas_test (psql, read-only) before writing this script:
-- master.company_profile currently holds company_id=1 -> '0000000000000' (the placeholder;
-- desynced from companies, which 637 already repaired to '0105000000012'), company_id=2 ->
-- '0000000000002', company_id=3 -> '0000000000003' (their OWN original seed values — NOT the
-- literal all-zero placeholder, and their master.companies rows hold the identical values, so
-- there is no companies/company_profile desync for co2/co3 — 637 never touched them either,
-- deliberately: it is scoped to the ONE literal placeholder string, not a broad "fix anything
-- that looks fake" pass. Repairing co2/co3's fake-but-consistent ids is out of this unit's
-- scope). company_id=4 already holds a real-looking id, untouched by either script.
--
-- Unlike 637, this script does NOT claim "at most one row can ever match": master.companies
-- has a UNIQUE index (ix_companies_tax_id) forcing that, but master.company_profile.tax_id
-- carries NO unique index (confirmed via pg_constraint — only NOT NULL + the company_id PK/FK).
-- Harmless either way: the WHERE clause only ever matches a value that is BY DEFINITION a
-- never-filled-in placeholder (13 zero digits), so any number of matched rows are equally
-- correct to repair to the same dummy value.
--
-- Same properties as 637:
--   * company-agnostic (SYSTEM-script contract, DbInitializer.DemoScripts doc comment) — repairs
--     WHATEVER company (if any) currently holds the placeholder;
--   * a no-op by construction on any database (prod included) where no profile row was ever
--     created with this placeholder, or where it was already fixed;
--   * RLS: master.company_profile has relrowsecurity = false (verified via pg_class, same
--     query 637 used) — no SET LOCAL app.bypass_rls needed.
--
-- Dummy value 0105000000012 — the IDENTICAL value 637 already stamped on master.companies for
-- this same company, so this UPDATE re-synchronizes the two tables rather than introducing a
-- second fictional value. See 637's own header for the checksum derivation.
--
-- Idempotent: the WHERE clause only ever matches the literal placeholder; a second run (or
-- replay on an already-repaired database) matches zero rows.
--
-- Codex review 2026-08-20 F1 — same class of bug as 637's own header: the literal-value-only
-- WHERE above would launder ANY real tenant's placeholder company_profile.tax_id into the
-- fictional 0105000000012. Added a JOIN back to master.companies (not company_profile's own
-- legal_name — company_profile is the desynced side here, see this file's header above) checking
-- the demo company's stable seeded identity (name_th = 'Demo Company (เดโม)', same literal 120
-- uses). Any other tenant's placeholder profile is left invalid so the filing/WHT guards keep
-- refusing it until a real Tax ID is entered.
--
-- NB: NEVER put curly braces anywhere in this file — DbInitializer runs it through
-- ExecuteSqlRawAsync, which treats brace characters as string.Format placeholders and fails at
-- boot.

UPDATE master.company_profile p
SET tax_id = '0105000000012'
WHERE p.tax_id = '0000000000000'
  AND EXISTS (
    SELECT 1 FROM master.companies c
    WHERE c.company_id = p.company_id AND c.name_th = 'Demo Company (เดโม)'
  );
