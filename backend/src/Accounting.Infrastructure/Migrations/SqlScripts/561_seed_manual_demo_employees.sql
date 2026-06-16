-- ============================================================
-- 561_seed_manual_demo_employees.sql  —  manual demo (ch6/7 payroll)
-- ============================================================
-- Two demo EMPLOYEES on the Manual Demo Co. (company_id = 2) so the
-- ch6/ch7 payroll walkthroughs have subjects to read:
--
--   MD-EMP-001  ประยุทธ์ มั่นคง   salary 35,000  SINGLE          → PIT ฿293
--   MD-EMP-002  สุดารัตน์ ใจงาม   salary 22,000  MARRIED + 2 kids → PIT ฿0
--
-- These are READ by 06.01 (payroll run, throws if absent), 07.06 (ภ.ง.ด.1)
-- and the annual ภ.ง.ด.1ก / 50ทวิ docs. The DRAFT payroll run for period
-- 202602 that consumes them is created at runtime (frontend/manual/
-- seed-demo-runtime.py) — a run posts GL + consumes a number sequence, so it
-- CANNOT be clean-SQL-seeded without breaking the trial-balance / number-gap
-- invariants (same rule as the Tax Invoice note in 400_seed_manual_demo_company.sql).
--
-- Idempotent: ON CONFLICT (company_id, employee_code) DO NOTHING
-- (unique ix_employees_company_id_employee_code). employee_id is left to the
-- identity sequence — NEVER hardcode it (migration 560 exists because explicit
-- ids outran the sequence and broke later API inserts).
--
-- national_id carries a VALID Thai check digit (mod-11). marital_status is the
-- text enum 'SINGLE'/'MARRIED' (ck_employees_marital). Hire 2026-01-01 so both
-- are "active in period" for any 2026 payroll run.
-- ============================================================

INSERT INTO master.employees (
    company_id, employee_code, title_th, first_name_th, last_name_th,
    national_id, tax_id, hire_date, base_salary,
    sso_applicable, marital_status, spouse_has_income, children_count,
    is_active, created_at)
VALUES
    (2, 'MD-EMP-001', 'นาย', 'ประยุทธ์', 'มั่นคง',
        '1101000000017', '1101000000017', DATE '2026-01-01', 35000.0000,
        TRUE, 'SINGLE', FALSE, 0,
        TRUE, now()),
    (2, 'MD-EMP-002', 'นางสาว', 'สุดารัตน์', 'ใจงาม',
        '1101000000025', '1101000000025', DATE '2026-01-01', 22000.0000,
        TRUE, 'MARRIED', FALSE, 2,
        TRUE, now())
ON CONFLICT (company_id, employee_code) DO NOTHING;
