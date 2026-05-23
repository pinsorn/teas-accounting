-- ============================================================
-- 420_seed_company1_profile.sql  —  company_id = 1 (admin tenant)
-- ============================================================
-- The default demo company (seeded in 120) had no master.company_profile row,
-- so GET /company-profile returned 404 for the `admin` login. Seed a profile
-- mirroring the company-1 legal identity (120) + sample soft fields.
-- Idempotent: ON CONFLICT (company_id) DO NOTHING.
-- ============================================================

INSERT INTO master.company_profile (
    company_id,
    -- HARD (read-only via UI in Phase 1) — mirrors master.companies row (120)
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
    1,
    'Demo Company (เดโม)', '0000000000000', '0000000000000',
    '1 อาคารเดโม ชั้น 1 ถนนสาทร', 'แขวงทุ่งมหาเมฆ',
    'ทุ่งมหาเมฆ', 'เขตสาทร',
    'กรุงเทพมหานคร', '10120',
    DATE '2020-01-01', '00000',
    'Demo Company', NULL, '02-000-0000', 'contact@demo.local',
    'https://demo.local', 'ผู้ดูแลระบบ',
    'ธนาคารกสิกรไทย', '000-0-00000-0', 'Demo Company (เดโม)',
    now(), now(), NULL)
ON CONFLICT (company_id) DO NOTHING;
