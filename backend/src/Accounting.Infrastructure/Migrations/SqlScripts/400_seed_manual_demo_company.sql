-- ============================================================
-- 400_seed_manual_demo_company.sql  —  Sprint 13b
-- ============================================================
-- A SEPARATE demo tenant ("Manual Demo Co., Ltd.", company_id = 2,
-- branch_id = 2) for Sana's live walkthrough capture. Kept fully apart
-- from the existing Demo Company (id = 1) so capture runs never pollute
-- the primary demo data.
--
-- Idempotent: every INSERT uses ON CONFLICT DO NOTHING on its natural
-- key, so DbInitializer can re-apply harmlessly. Periods are derived from
-- CURRENT_DATE so re-runs stay correct over time.
--
-- Passwords: crypt('Demo@1234', gen_salt('bf', 12)) — pgcrypto blowfish,
-- BCrypt.Net-verifiable. NEVER a literal $2a$ hash (runtime-gotchas §18).
--
-- Schema verified against the live migrated DB (information_schema), not
-- db/schema.sql (incomplete reference) — CLAUDE.md §8.
--
-- NOTE — sample Tax Invoices are INTENTIONALLY NOT seeded here:
--   * a POSTED TI written by raw SQL would have no matching GL journal
--     and would not consume sys.number_sequences → breaks trial-balance
--     and number-gap-audit invariants (CLAUDE.md §10: no non-monotonic
--     doc numbers, no fake posted state);
--   * a DRAFT TI adds a fragile FK chain (tax_codes/uom per company 2,
--     supplier snapshot, amounts) for marginal demo value.
--   Sana's first capture chapter creates a Tax Invoice live through the
--   UI — those become the "existing" rows for later chapters. Flagged in
--   the Sprint-13b report.
-- ============================================================

-- 1. Company + head-office branch ----------------------------------------
INSERT INTO master.companies (
    company_id, tax_id, name_th, name_en, legal_entity_type, vat_registered,
    fiscal_year_start_month, base_currency, reporting_standard,
    is_active, created_at, requires_business_unit)
VALUES (
    2, '0000000000002', 'บริษัท แมนนวล เดโม จำกัด', 'Manual Demo Co., Ltd.',
    'LimitedCompany', TRUE, 1, 'THB', 'TFRS_NPAE',
    TRUE, now(), TRUE)
ON CONFLICT (company_id) DO NOTHING;

INSERT INTO master.branches (
    branch_id, company_id, branch_code, name_th, name_en,
    is_head_office, is_active)
VALUES (2, 2, '00000', 'สำนักงานใหญ่', 'Head Office', TRUE, TRUE)
ON CONFLICT (branch_id) DO NOTHING;

-- 2. Minimal Chart of Accounts (mirrors company-1 seed 120) --------------
INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_type,
     normal_balance, is_header, is_active, created_at)
VALUES
    (2, '1110', 'เงินสด',                     'ASSET',     'DR', FALSE, TRUE, now()),
    (2, '1120', 'เงินฝากธนาคาร',              'ASSET',     'DR', FALSE, TRUE, now()),
    (2, '1130', 'ลูกหนี้การค้า',              'ASSET',     'DR', FALSE, TRUE, now()),
    (2, '1170', 'ภาษีซื้อ',                   'ASSET',     'DR', FALSE, TRUE, now()),
    (2, '2110', 'เจ้าหนี้การค้า',             'LIABILITY', 'CR', FALSE, TRUE, now()),
    (2, '2151', 'ภาษีขายค้างจ่าย',            'LIABILITY', 'CR', FALSE, TRUE, now()),
    (2, '2152', 'ภาษีหัก ณ ที่จ่ายค้างจ่าย',  'LIABILITY', 'CR', FALSE, TRUE, now()),
    (2, '4000', 'รายได้จากการขาย',            'REVENUE',   'CR', FALSE, TRUE, now()),
    (2, '4100', 'รับคืน / ส่วนลด',            'REVENUE',   'DR', FALSE, TRUE, now()),
    (2, '5100', 'ค่าใช้จ่ายค่าเช่า',          'EXPENSE',   'DR', FALSE, TRUE, now()),
    (2, '5200', 'ค่าใช้จ่ายค่าบริการ',        'EXPENSE',   'DR', FALSE, TRUE, now()),
    (2, '5300', 'ค่าใช้จ่ายโฆษณา',            'EXPENSE',   'DR', FALSE, TRUE, now())
ON CONFLICT (company_id, account_code) DO NOTHING;

-- 3. WHT types (mirrors 120; needed by expense categories / vendors) -----
INSERT INTO tax.wht_types
    (company_id, code, name_th, income_type_code, form_type, rate,
     is_active, effective_from)
VALUES
    (2, 'RENT', 'ค่าเช่า',    '5', 'PND3',  0.05, TRUE, DATE '2020-01-01'),
    (2, 'SVC',  'ค่าบริการ',  '3', 'PND53', 0.03, TRUE, DATE '2020-01-01'),
    (2, 'ADS',  'ค่าโฆษณา',   '4', 'PND53', 0.02, TRUE, DATE '2020-01-01')
ON CONFLICT (company_id, code, effective_from) DO NOTHING;

-- 4. Expense categories (SVC/RENT/ADS/ENT/OFFICE; ENT non-recoverable) ----
INSERT INTO sys.expense_categories
    (company_id, category_code, name_th, name_en, default_expense_account_id,
     default_is_recoverable_vat, is_capex, is_cogs, is_active, created_at)
SELECT 2, v.code, v.th, v.en,
       (SELECT account_id FROM master.chart_of_accounts
          WHERE company_id = 2 AND account_code = '5200'),
       v.rec, FALSE, FALSE, TRUE, now()
FROM (VALUES
    ('SVC',    'ค่าบริการ',          'Service fee',     TRUE),
    ('RENT',   'ค่าเช่า',            'Rent',            TRUE),
    ('ADS',    'ค่าโฆษณา',           'Advertising',     TRUE),
    ('ENT',    'ค่ารับรอง',          'Entertainment',   FALSE),
    ('OFFICE', 'ค่าใช้จ่ายสำนักงาน', 'Office supplies', TRUE)
) AS v(code, th, en, rec)
ON CONFLICT (company_id, category_code) DO NOTHING;

-- 5. Business Units (ECOM / LAB / REPT) ----------------------------------
INSERT INTO master.business_units
    (company_id, code, name_th, name_en, is_active,
     created_at, updated_at, version)
VALUES
    (2, 'ECOM', 'อีคอมเมิร์ซ',     'E-Commerce',  TRUE, now(), now(), 0),
    (2, 'LAB',  'แล็บ',            'Laboratory',  TRUE, now(), now(), 0),
    (2, 'REPT', 'สัตว์เลื้อยคลาน', 'Reptiles',    TRUE, now(), now(), 0)
ON CONFLICT (company_id, code) DO NOTHING;

-- 6. Customers (3 INDIVIDUAL walk-in + 2 CORPORATE incl. Acme B2B) -------
INSERT INTO master.customers (
    company_id, customer_code, customer_type, name_th, name_en,
    tax_id, branch_code, vat_registered, billing_address,
    credit_limit, payment_term_days, default_currency, is_active, created_at)
VALUES
    (2, 'MC-IND-001', 'INDIVIDUAL', 'คุณสมชาย ใจดี',        NULL,
        NULL, NULL, FALSE, '12 ถ.สุขุมวิท กรุงเทพฯ 10110',
        0, 0,  'THB', TRUE, now()),
    (2, 'MC-IND-002', 'INDIVIDUAL', 'คุณสมหญิง รักเรียน',   NULL,
        NULL, NULL, FALSE, '34 ถ.พหลโยธิน กรุงเทพฯ 10400',
        0, 0,  'THB', TRUE, now()),
    (2, 'MC-IND-003', 'INDIVIDUAL', 'คุณวีระ ตั้งใจ',       NULL,
        NULL, NULL, FALSE, '56 ถ.รัชดาภิเษก กรุงเทพฯ 10310',
        0, 0,  'THB', TRUE, now()),
    (2, 'MC-COR-001', 'CORPORATE',  'บริษัท แอคมี จำกัด',   'Acme Co., Ltd.',
        '0105556123453', '00000', TRUE, '99 ถ.ทดสอบ กรุงเทพฯ 10110',
        500000, 30, 'THB', TRUE, now()),
    (2, 'MC-COR-002', 'CORPORATE',  'บริษัท บลู โอเชียน จำกัด', 'Blue Ocean Co., Ltd.',
        '0105556987654', '00000', TRUE, '88 ถ.สีลม กรุงเทพฯ 10500',
        300000, 30, 'THB', TRUE, now())
ON CONFLICT (company_id, customer_code) DO NOTHING;

-- 7. Vendors -------------------------------------------------------------
--   3 domestic: 2 VAT-registered + 1 non-VAT (ร้านโชห่วยตัวอย่าง)
--   1 foreign no Thai VAT-D (Amazon Web Services Inc., US)
--   1 foreign WITH Thai VAT-D (Netflix)
--   CHECK ck_vendors_foreign_vatreg: is_foreign ⟹ vat_registered
--   CHECK ck_vendors_vatd_foreign:   has_thai_vat_d_reg ⟹ is_foreign
INSERT INTO master.vendors (
    company_id, vendor_code, vendor_type, tax_id, branch_code, name_th,
    name_en, vat_registered, address, payment_term_days, default_currency,
    default_wht_type_code, is_active, created_at,
    country_code, is_foreign, has_thai_vat_d_reg)
VALUES
    (2, 'MV-DOM-001', 'CORPORATE', '0105551111119', '00000',
        'บริษัท ออฟฟิศ ซัพพลาย จำกัด', 'Office Supply Co., Ltd.',
        TRUE,  '101 ถ.ลาดพร้าว กรุงเทพฯ 10230', 30, 'THB',
        'SVC',  TRUE, now(), 'TH', FALSE, FALSE),
    (2, 'MV-DOM-002', 'INDIVIDUAL', NULL, NULL,
        'ร้านโชห่วยตัวอย่าง', 'Sample Grocery Shop',
        FALSE, '202 ถ.เพชรบุรี กรุงเทพฯ 10400', 0, 'THB',
        NULL,   TRUE, now(), 'TH', FALSE, FALSE),
    (2, 'MV-DOM-003', 'CORPORATE', '0105552222227', '00000',
        'บริษัท พร็อพเพอร์ตี้ เช่า จำกัด', 'Property Rental Co., Ltd.',
        TRUE,  '303 ถ.สาทร กรุงเทพฯ 10120', 30, 'THB',
        'RENT', TRUE, now(), 'TH', FALSE, FALSE),
    (2, 'MV-FOR-001', 'CORPORATE', NULL, NULL,
        'Amazon Web Services Inc.', 'Amazon Web Services Inc.',
        TRUE,  '410 Terry Ave N, Seattle, WA 98109, USA', 30, 'USD',
        NULL,   TRUE, now(), 'US', TRUE, FALSE),
    (2, 'MV-FOR-002', 'CORPORATE', NULL, NULL,
        'Netflix International B.V.', 'Netflix International B.V.',
        TRUE,  'Karperstraat 8-10, Amsterdam, Netherlands', 30, 'USD',
        NULL,   TRUE, now(), 'NL', TRUE, TRUE)
ON CONFLICT (company_id, vendor_code) DO NOTHING;

-- 8. Products (3 SERVICE + 4 GOOD + 3 EXEMPT_GOOD live/feed) -------------
--   product_type ∈ GOOD | SERVICE | EXEMPT_GOOD | EXEMPT_SERVICE
--   (no LIVE/FEED enum — live animals & feed are EXEMPT_GOOD; the name
--    conveys the kind for the walkthrough).
INSERT INTO master.products (
    company_id, product_code, name_th, name_en, product_type,
    default_uom_text, default_unit_price, description_th,
    is_active, created_at, updated_at, version)
VALUES
    (2, 'MP-SVC-001', 'ค่าบริการตรวจแล็บ',      'Lab testing service',  'SERVICE',
        'ครั้ง',  1500.0000, 'บริการตรวจวิเคราะห์ตัวอย่างในห้องแล็บ', TRUE, now(), now(), 0),
    (2, 'MP-SVC-002', 'ค่าที่ปรึกษา',           'Consultation service', 'SERVICE',
        'ชั่วโมง', 2000.0000, 'บริการให้คำปรึกษา', TRUE, now(), now(), 0),
    (2, 'MP-SVC-003', 'ค่าบริการดูแลระบบ',      'System maintenance',   'SERVICE',
        'เดือน',  8000.0000, 'บริการดูแลระบบรายเดือน', TRUE, now(), now(), 0),
    (2, 'MP-GD-001',  'ตู้เลี้ยงปลา ขนาดกลาง',  'Aquarium tank (M)',    'GOOD',
        'ใบ',     3500.0000, 'ตู้กระจกพร้อมขอบ', TRUE, now(), now(), 0),
    (2, 'MP-GD-002',  'เครื่องกรองน้ำ',         'Water filter',         'GOOD',
        'เครื่อง', 1200.0000, 'เครื่องกรองน้ำสำหรับตู้ปลา', TRUE, now(), now(), 0),
    (2, 'MP-GD-003',  'หลอดไฟ UVB',             'UVB lamp',             'GOOD',
        'หลอด',    850.0000, 'หลอดไฟสำหรับสัตว์เลื้อยคลาน', TRUE, now(), now(), 0),
    (2, 'MP-GD-004',  'กรงสัตว์เลื้อยคลาน',     'Reptile enclosure',    'GOOD',
        'ใบ',     4200.0000, 'กรงไม้พร้อมกระจก', TRUE, now(), now(), 0),
    (2, 'MP-EXM-001', 'เต่าบก (สัตว์มีชีวิต)',  'Tortoise (live)',      'EXEMPT_GOOD',
        'ตัว',    2500.0000, 'สัตว์มีชีวิต — ยกเว้น VAT (ม.81(1)(ก))', TRUE, now(), now(), 0),
    (2, 'MP-EXM-002', 'ปลาทอง (สัตว์มีชีวิต)',  'Goldfish (live)',      'EXEMPT_GOOD',
        'ตัว',      50.0000, 'สัตว์มีชีวิต — ยกเว้น VAT', TRUE, now(), now(), 0),
    (2, 'MP-EXM-003', 'อาหารปลา',               'Fish feed',            'EXEMPT_GOOD',
        'ถุง',     180.0000, 'อาหารสัตว์ — ยกเว้น VAT (ม.81(1)(ข))', TRUE, now(), now(), 0)
ON CONFLICT (company_id, product_code) DO NOTHING;

-- 9. Users (deterministic creds; bcrypt via pgcrypto) --------------------
INSERT INTO sys.users (
    user_id, username, email, password_hash, full_name,
    is_super_admin, is_active, failed_login_count, must_change_password,
    created_at, updated_at, version)
VALUES
    (2001, 'demo-admin',      'demo-admin@manualdemo.local',
        crypt('Demo@1234', gen_salt('bf', 12)), 'Demo Admin',
        TRUE,  TRUE, 0, FALSE, now(), now(), 0),
    (2002, 'demo-accountant', 'demo-accountant@manualdemo.local',
        crypt('Demo@1234', gen_salt('bf', 12)), 'Demo Accountant',
        FALSE, TRUE, 0, FALSE, now(), now(), 0),
    (2003, 'demo-approver',   'demo-approver@manualdemo.local',
        crypt('Demo@1234', gen_salt('bf', 12)), 'Demo Approver',
        FALSE, TRUE, 0, FALSE, now(), now(), 0)
ON CONFLICT (user_id) DO NOTHING;

-- 10. Role assignments (scoped to company 2 / branch 2) ------------------
--   demo-admin      → SUPER_ADMIN
--   demo-accountant → ACCOUNTANT + AR_CLERK + AP_CLERK
--   demo-approver   → APPROVER + CHIEF_ACCOUNTANT
INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT u.uid, r.role_id, 2, 2, DATE '2026-01-01'
FROM (VALUES
    (2001, 'SUPER_ADMIN'),
    (2002, 'ACCOUNTANT'), (2002, 'AR_CLERK'), (2002, 'AP_CLERK'),
    (2003, 'APPROVER'),   (2003, 'CHIEF_ACCOUNTANT')
) AS u(uid, role_code)
JOIN sys.roles r ON r.role_code = u.role_code
ON CONFLICT DO NOTHING;

-- 11. Accounting periods: previous month CLOSED + current month OPEN -----
--   Derived from CURRENT_DATE so the seed stays correct over time.
INSERT INTO gl.accounting_periods
    (company_id, year, month, status, closed_at, closed_by, close_notes)
SELECT 2,
       EXTRACT(YEAR  FROM d)::INT,
       EXTRACT(MONTH FROM d)::INT,
       'CLOSED', now(), 2001, 'Seeded closed period (manual-demo)'
FROM (SELECT date_trunc('month', CURRENT_DATE) - INTERVAL '1 month' AS d) s
ON CONFLICT (company_id, year, month) DO NOTHING;

INSERT INTO gl.accounting_periods
    (company_id, year, month, status, closed_at, closed_by, close_notes)
SELECT 2,
       EXTRACT(YEAR  FROM CURRENT_DATE)::INT,
       EXTRACT(MONTH FROM CURRENT_DATE)::INT,
       'OPEN', NULL, NULL, NULL
ON CONFLICT (company_id, year, month) DO NOTHING;
