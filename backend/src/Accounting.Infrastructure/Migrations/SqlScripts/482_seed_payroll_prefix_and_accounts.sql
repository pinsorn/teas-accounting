-- Payroll P-C seed: the PR document prefix + the 5 payroll GL accounts (keep in sync with
-- GlAccountsOptions Payroll keys). Accounts are seeded for EVERY existing company so a posted
-- run resolves its accounts regardless of tenant. Additive + idempotent.

INSERT INTO sys.document_prefixes
    (prefix_code, document_type, description_th, description_en, requires_etax, is_fiscal_doc, is_expense, is_active, created_at)
VALUES
    ('PR', 'PAYROLL_RUN', 'ใบจ่ายเงินเดือน', 'Payroll Run', FALSE, TRUE, TRUE, TRUE, NOW())
ON CONFLICT (prefix_code) DO NOTHING;

-- Payroll GL accounts (DR expenses / CR payables). Column set matches seed 120.
INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_type, normal_balance, is_header, is_active, created_at)
SELECT c.company_id, a.account_code, a.account_name_th, a.account_type, a.normal_balance, FALSE, TRUE, now()
FROM master.companies c
CROSS JOIN (VALUES
    ('5400', 'เงินเดือนและค่าจ้าง',                    'EXPENSE',   'DR'),
    ('5410', 'เงินสมทบประกันสังคม-ส่วนนายจ้าง',         'EXPENSE',   'DR'),
    ('2153', 'ภาษีเงินได้พนักงานหัก ณ ที่จ่ายค้างนำส่ง', 'LIABILITY', 'CR'),
    ('2160', 'เงินสมทบประกันสังคมค้างนำส่ง',            'LIABILITY', 'CR'),
    ('2170', 'เงินเดือนค้างจ่าย',                       'LIABILITY', 'CR')
) AS a(account_code, account_name_th, account_type, normal_balance)
ON CONFLICT (company_id, account_code) DO NOTHING;
