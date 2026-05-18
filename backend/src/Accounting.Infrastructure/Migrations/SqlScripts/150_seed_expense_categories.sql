-- Default expense categories for the demo company (CLAUDE.md §6 Phase-1 / plan §17.3).
-- Previously missing — the Vendor Invoice / Payment Voucher flow is unusable without
-- at least the core set. ENT is non-recoverable input VAT (ม.82/5) to drive the ⚠ path.
-- Idempotent. default_expense_account_id → demo CoA 5200.

INSERT INTO sys.expense_categories
    (company_id, category_code, name_th, name_en, default_expense_account_id,
     default_is_recoverable_vat, is_capex, is_cogs, is_active, created_at)
SELECT 1, v.code, v.th, v.en,
       (SELECT account_id FROM master.chart_of_accounts
          WHERE company_id = 1 AND account_code = '5200'),
       v.rec, FALSE, FALSE, TRUE, now()
FROM (VALUES
    ('SVC',  'ค่าบริการ',          'Service fee',       TRUE),
    ('RENT', 'ค่าเช่า',            'Rent',              TRUE),
    ('OFF',  'ค่าใช้จ่ายสำนักงาน',  'Office supplies',   TRUE),
    ('ADS',  'ค่าโฆษณา',           'Advertising',       TRUE),
    ('ENT',  'ค่ารับรอง',          'Entertainment',     FALSE)
) AS v(code, th, en, rec)
ON CONFLICT (company_id, category_code) DO NOTHING;
