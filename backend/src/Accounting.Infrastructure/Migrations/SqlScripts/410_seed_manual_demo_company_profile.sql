-- ============================================================
-- 410_seed_manual_demo_company_profile.sql  —  Sprint 13d P6
-- ============================================================
-- Company Profile (hybrid lock) for the manual-demo tenant (company_id = 2,
-- seeded in 400). Separate file from 400 because 400 is already recorded in
-- sys.applied_sql_scripts (DbInitializer tracks by filename) — a new file
-- re-runs cleanly. Runs AFTER MigrateAsync, so master.company_profile (added
-- by migration 20260519041450_AddCompanyProfile) exists.
--
-- Hard fields mirror the company row seeded in 400 (legal identity bound to
-- ภ.พ.20). Soft fields are sample contact/banking data for walkthroughs.
-- Idempotent: ON CONFLICT (company_id) DO NOTHING.
-- ============================================================

INSERT INTO master.company_profile (
    company_id,
    -- HARD (read-only via UI in Phase 1)
    legal_name, tax_id, registration_number,
    registered_address_line1, registered_address_line2,
    registered_subdistrict, registered_district,
    registered_province, registered_postal_code,
    vat_registration_date, branch_code,
    -- SOFT (admin-editable)
    trade_name, logo_url, phone, email, website,
    contact_name, bank_name, bank_account_no, bank_account_name,
    -- audit
    created_at, updated_at, updated_by_user_id)
VALUES (
    2,
    'บริษัท แมนนวล เดโม จำกัด', '0000000000002', '0000000000002',
    '199 อาคารเดโม ชั้น 9 ถนนสุขุมวิท', 'แขวงคลองเตย',
    'คลองเตย', 'เขตคลองเตย',
    'กรุงเทพมหานคร', '10110',
    DATE '2020-01-01', '00000',
    'Manual Demo', NULL, '02-123-4567', 'contact@manualdemo.local',
    'https://manualdemo.local', 'คุณเดโม แอดมิน',
    'ธนาคารกสิกรไทย', '123-4-56789-0', 'บริษัท แมนนวล เดโม จำกัด',
    now(), now(), NULL)
ON CONFLICT (company_id) DO NOTHING;
