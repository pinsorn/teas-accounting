-- Demo company + branch + minimum CoA + WHT types.
-- Idempotent: uses ON CONFLICT to allow re-runs.
-- Account codes match GlAccounts section in appsettings — keep these in sync.

INSERT INTO master.companies (
    company_id, tax_id, name_th, legal_entity_type, vat_registered,
    fiscal_year_start_month, base_currency, reporting_standard, is_active, created_at)
VALUES (
    1, '0000000000000', 'Demo Company (เดโม)', 'LimitedCompany', TRUE,
    1, 'THB', 'TFRS_NPAE', TRUE, now())
ON CONFLICT (company_id) DO NOTHING;

-- Self-heal (Unit D / F1, specs/fix-company-roles-seed-ordering.md half b): if 510 has ALREADY
-- run (this script applying on a LATER boot, after SeedDemoData was flipped true post-510 —
-- see 636's header for the exact repro), seed company 1's role catalogue immediately so it is
-- not stranded until 510 re-runs — 510 is SYSTEM and only ever runs once. On a normal fresh
-- install 510 has not defined the function yet at this point in file order (120 < 510
-- numerically) and this guard no-ops harmlessly; 510's own fan-out (step 4) covers company 1
-- later in the same boot, unchanged. NB: no curly braces (ExecuteSqlRawAsync/string.Format).
DO $do$
BEGIN
    IF to_regprocedure('sys.seed_company_roles(integer)') IS NOT NULL THEN
        PERFORM set_config('app.bypass_rls', 'true', true);
        PERFORM sys.seed_company_roles(1);
    END IF;
END
$do$;

INSERT INTO master.branches (
    branch_id, company_id, branch_code, name_th, is_head_office, is_active)
VALUES (1, 1, '00000', 'สำนักงานใหญ่', TRUE, TRUE)
ON CONFLICT (branch_id) DO NOTHING;

-- ChartOfAccount cols: company_id, account_code, account_name_th, account_type, normal_balance, is_header, is_active, created_at
INSERT INTO master.chart_of_accounts (company_id, account_code, account_name_th, account_type, normal_balance, is_header, is_active, created_at) VALUES
    (1, '1110', 'เงินสด',                     'ASSET',     'DR', FALSE, TRUE, now()),
    (1, '1120', 'เงินฝากธนาคาร',              'ASSET',     'DR', FALSE, TRUE, now()),
    (1, '1130', 'ลูกหนี้การค้า',              'ASSET',     'DR', FALSE, TRUE, now()),
    (1, '1170', 'ภาษีซื้อ',                  'ASSET',     'DR', FALSE, TRUE, now()),
    (1, '2110', 'เจ้าหนี้การค้า',             'LIABILITY', 'CR', FALSE, TRUE, now()),
    (1, '2151', 'ภาษีขายค้างจ่าย',           'LIABILITY', 'CR', FALSE, TRUE, now()),
    (1, '2152', 'ภาษีหัก ณ ที่จ่ายค้างจ่าย', 'LIABILITY', 'CR', FALSE, TRUE, now()),
    (1, '4000', 'รายได้จากการขาย',           'REVENUE',   'CR', FALSE, TRUE, now()),
    (1, '4100', 'รับคืน / ส่วนลด',           'REVENUE',   'DR', FALSE, TRUE, now()),
    (1, '5100', 'ค่าใช้จ่ายค่าเช่า',          'EXPENSE',   'DR', FALSE, TRUE, now()),
    (1, '5200', 'ค่าใช้จ่ายค่าบริการ',        'EXPENSE',   'DR', FALSE, TRUE, now()),
    (1, '5300', 'ค่าใช้จ่ายโฆษณา',           'EXPENSE',   'DR', FALSE, TRUE, now())
ON CONFLICT (company_id, account_code) DO NOTHING;

-- WhtType minimal seed. form_type stored as string ('PND3'/'PND53'/'PND1').
-- Sprint 8.6: effective_from is part of the unique key (company_id, code,
-- effective_from) — must be set + named in ON CONFLICT (the old 2-col unique
-- index was dropped by AddARWhtSupport). Full 13-type set seeded in 220.
INSERT INTO tax.wht_types
    (company_id, code, name_th, income_type_code, form_type, rate, is_active, effective_from)
VALUES
    (1, 'RENT', 'ค่าเช่า',     '5', 'PND3',  0.05, TRUE, DATE '2020-01-01'),
    (1, 'SVC',  'ค่าบริการ',   '3', 'PND53', 0.03, TRUE, DATE '2020-01-01'),
    (1, 'ADS',  'ค่าโฆษณา',    '4', 'PND53', 0.02, TRUE, DATE '2020-01-01')
ON CONFLICT (company_id, code, effective_from) DO NOTHING;
