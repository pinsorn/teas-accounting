-- ============================================================
-- 440_seed_nonvat_demo_company.sql  —  per-company-vat-mode spec (2026-06-12)
-- ============================================================
-- A NON-VAT demo tenant ("Non-VAT Demo Shop", company_id = 3, branch_id = 3).
-- Purpose:
--   * proves per-company VAT mode in one instance (company 1/2 = VAT, 3 = non-VAT);
--   * gives e2e a real non-VAT login (non-vat-mode-pdf.spec.ts) — the old
--     Tax__VatMode=false second-pass env toggle is dead (config removed);
--   * lets Ham demo the non-VAT experience without touching company 1.
--
-- Idempotent (ON CONFLICT DO NOTHING on natural keys), mirrors 400/410.
-- Password: crypt('Admin@1234', gen_salt('bf', 12)) — pgcrypto bcrypt
-- (NEVER a literal $2a$ hash — runtime-gotchas §18). Same password as the
-- company-1 admin so the e2e login helper works unchanged.
-- ============================================================

-- 1. Company (vat_registered = FALSE → non-VAT mode) + HQ branch ----------
INSERT INTO master.companies (
    company_id, tax_id, name_th, name_en, legal_entity_type, vat_registered,
    fiscal_year_start_month, base_currency, reporting_standard,
    is_active, created_at, requires_business_unit)
VALUES (
    3, '0000000000003', 'ร้านนอนแวต เดโม', 'Non-VAT Demo Shop',
    'LimitedCompany', FALSE, 1, 'THB', 'TFRS_NPAE',
    TRUE, now(), FALSE)
ON CONFLICT (company_id) DO NOTHING;

INSERT INTO master.branches (
    branch_id, company_id, branch_code, name_th, name_en,
    is_head_office, is_active)
VALUES (3, 3, '00000', 'สำนักงานใหญ่', 'Head Office', TRUE, TRUE)
ON CONFLICT (branch_id) DO NOTHING;

-- 2. Minimal Chart of Accounts -------------------------------------------
INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_type,
     normal_balance, is_header, is_active, created_at)
VALUES
    (3, '1110', 'เงินสด',          'ASSET',     'DR', FALSE, TRUE, now()),
    (3, '1120', 'เงินฝากธนาคาร',   'ASSET',     'DR', FALSE, TRUE, now()),
    (3, '1130', 'ลูกหนี้การค้า',   'ASSET',     'DR', FALSE, TRUE, now()),
    (3, '2110', 'เจ้าหนี้การค้า',  'LIABILITY', 'CR', FALSE, TRUE, now()),
    (3, '2151', 'ภาษีขายค้างจ่าย', 'LIABILITY', 'CR', FALSE, TRUE, now()),
    (3, '4000', 'รายได้จากการขาย', 'REVENUE',   'CR', FALSE, TRUE, now()),
    (3, '5200', 'ค่าใช้จ่ายค่าบริการ', 'EXPENSE', 'DR', FALSE, TRUE, now()),
    -- 5350 = irrecoverable input VAT (non-VAT can't reclaim — ม.83/6 path)
    (3, '5350', 'ภาษีซื้อต้องห้าม/ตัดจ่าย', 'EXPENSE', 'DR', FALSE, TRUE, now())
ON CONFLICT (company_id, account_code) DO NOTHING;

-- 3. Customers (non-VAT shop sells mostly walk-in + 1 corporate) ----------
INSERT INTO master.customers (
    company_id, customer_code, customer_type, name_th, name_en,
    tax_id, branch_code, vat_registered, billing_address,
    credit_limit, payment_term_days, default_currency, is_active, created_at)
VALUES
    (3, 'NV-IND-001', 'INDIVIDUAL', 'คุณนนท์ ซื้อประจำ', NULL,
        NULL, NULL, FALSE, '11 ถ.ตลาดน้อย กรุงเทพฯ 10100',
        0, 0, 'THB', TRUE, now()),
    (3, 'NV-COR-001', 'CORPORATE', 'บริษัท ลูกค้านิติ จำกัด', 'Corp Customer Co., Ltd.',
        '0105556123453', '00000', TRUE, '22 ถ.เจริญกรุง กรุงเทพฯ 10500',
        100000, 30, 'THB', TRUE, now())
ON CONFLICT (company_id, customer_code) DO NOTHING;

-- 4. Products (1 good + 1 service) ----------------------------------------
INSERT INTO master.products (
    company_id, product_code, name_th, name_en, product_type,
    default_uom_text, default_unit_price, description_th,
    is_active, created_at, updated_at, version)
VALUES
    (3, 'NV-GD-001', 'สินค้าทั่วไป',  'General goods',   'GOOD',
        'ชิ้น', 500.0000, 'สินค้าร้านนอนแวต', TRUE, now(), now(), 0),
    (3, 'NV-SVC-001', 'ค่าบริการทั่วไป', 'General service', 'SERVICE',
        'ครั้ง', 800.0000, 'บริการร้านนอนแวต', TRUE, now(), now(), 0)
ON CONFLICT (company_id, product_code) DO NOTHING;

-- 5. User (deterministic creds; same password as company-1 admin for e2e) --
INSERT INTO sys.users (
    user_id, username, email, password_hash, full_name,
    is_super_admin, is_active, failed_login_count, must_change_password,
    created_at, updated_at, version)
VALUES
    (3001, 'nonvat-admin', 'nonvat-admin@nonvatdemo.local',
        crypt('Admin@1234', gen_salt('bf', 12)), 'Non-VAT Demo Admin',
        TRUE, TRUE, 0, FALSE, now(), now(), 0)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 3001, r.role_id, 3, 3, DATE '2026-01-01'
FROM sys.roles r WHERE r.role_code = 'SUPER_ADMIN'
ON CONFLICT DO NOTHING;

-- 6. Accounting period: current month OPEN --------------------------------
INSERT INTO gl.accounting_periods
    (company_id, year, month, status, closed_at, closed_by, close_notes)
SELECT 3,
       EXTRACT(YEAR  FROM CURRENT_DATE)::INT,
       EXTRACT(MONTH FROM CURRENT_DATE)::INT,
       'OPEN', NULL, NULL, NULL
ON CONFLICT (company_id, year, month) DO NOTHING;

-- 7. Company profile (PDF headers need it; mirrors 410 shape) --------------
INSERT INTO master.company_profile (
    company_id,
    legal_name, tax_id, registration_number,
    registered_address_line1, registered_address_line2,
    registered_subdistrict, registered_district,
    registered_province, registered_postal_code,
    vat_registration_date, branch_code,
    trade_name, logo_url, phone, email, website,
    contact_name, bank_name, bank_account_no, bank_account_name,
    created_at, updated_at, updated_by_user_id)
VALUES (
    3,
    'ร้านนอนแวต เดโม', '0000000000003', '0000000000003',
    '55 ถนนตลาดน้อย', 'แขวงตลาดน้อย',
    'ตลาดน้อย', 'เขตสัมพันธวงศ์',
    'กรุงเทพมหานคร', '10100',
    NULL, '00000',
    'Non-VAT Demo', NULL, '02-999-8888', 'contact@nonvatdemo.local',
    NULL, 'คุณนอนแวต',
    'ธนาคารกสิกรไทย', '987-6-54321-0', 'ร้านนอนแวต เดโม',
    now(), now(), NULL)
ON CONFLICT (company_id) DO NOTHING;
