-- BP-01 (RV2) — complete the company-1 expense-category master to the canonical 19
-- recommended in accounting-system-plan.md §17.3. The original 150_seed shipped only a
-- 5-code starter set (SVC/RENT/OFF/ADS/ENT); RE-VALIDATE expected the §17.3 set.
--
-- Idempotent: ON CONFLICT (company_id, category_code) DO NOTHING — so RENT + ENT (already
-- seeded by 150) keep their existing ids/refs; the other 17 §17.3 codes are inserted.
-- The 3 legacy ad-hoc codes (SVC/OFF/ADS) are LEFT as-is (active) — existing PVs + tests
-- reference them; reconciling them to PROF/OFFI/MARK is a future data-cleanup, not done here.
--
-- COMPLIANCE-RELEVANT default set here: default_is_recoverable_vat per §17.3
--   (ม.82/5 — ENT รับรอง + VEHI รถยนต์นั่ง ≤7 = ภาษีซื้อต้องห้าม → FALSE; SAL/INTR carry no
--    input VAT → FALSE; everything else TRUE).
-- NOT set here (left to the existing per-line PV flow / 170_link, to avoid baking a wrong
--   compliance default): default_tax_code_id and default_wht_type_id stay NULL. The §17.3
--   WHT-rate column (RENT 5% / WAGE 3% / PROF 3% / …) should be wired in a follow-up once the
--   tax.wht_types rows are confirmed for this company — flagged for Sana/Ham.
-- default_expense_account_id: prefer the real §17.3 CoA code (62010…); fall back to the demo
--   5200 account (used by 150_seed) when the granular 62xxx chart is not seeded.

INSERT INTO sys.expense_categories
    (company_id, category_code, name_th, name_en, default_expense_account_id,
     default_is_recoverable_vat, is_capex, is_cogs, is_active, created_at)
SELECT 1, v.code, v.th, v.en,
       COALESCE(
         (SELECT account_id FROM master.chart_of_accounts
            WHERE company_id = 1 AND account_code = v.acct),
         (SELECT account_id FROM master.chart_of_accounts
            WHERE company_id = 1 AND account_code = '5200')),
       v.rec, v.capex, v.cogs, TRUE, now()
FROM (VALUES
    --  code,    name_th,                     name_en,                  acct,    recoverable, capex, cogs
    ('RENT',  'ค่าเช่าออฟฟิศ/อาคาร',          'Office/building rent',   '62010', TRUE,  FALSE, FALSE),
    ('UTIL',  'ค่าสาธารณูปโภค',               'Utilities',              '62020', TRUE,  FALSE, FALSE),
    ('SAL',   'เงินเดือน',                    'Salary',                 '61010', FALSE, FALSE, FALSE),
    ('WAGE',  'ค่าจ้างแรงงาน',                'Wages',                  '61020', TRUE,  FALSE, FALSE),
    ('MARK',  'ค่าโฆษณา/Marketing',           'Advertising/marketing',  '62030', TRUE,  FALSE, FALSE),
    ('PROF',  'ค่าบริการวิชาชีพ',             'Professional services',  '62040', TRUE,  FALSE, FALSE),
    ('IT',    'ค่า IT / Cloud / Software',    'IT / Cloud / Software',  '62050', TRUE,  FALSE, FALSE),
    ('TRAV',  'ค่าเดินทาง / ที่พัก',          'Travel / lodging',       '62060', TRUE,  FALSE, FALSE),
    ('COMM',  'ค่าโทรศัพท์/Internet',         'Telephone / Internet',   '62070', TRUE,  FALSE, FALSE),
    ('OFFI',  'วัสดุสำนักงาน',                'Office supplies',        '62080', TRUE,  FALSE, FALSE),
    ('ENT',   'ค่ารับรอง',                    'Entertainment',          '62090', FALSE, FALSE, FALSE),
    ('VEHI',  'รถยนต์นั่ง (≤7 ที่นั่ง)',       'Passenger car (<=7)',    '62100', FALSE, FALSE, FALSE),
    ('INSU',  'ค่าประกันภัย',                 'Insurance',              '62110', TRUE,  FALSE, FALSE),
    ('TRAIN', 'ค่าอบรม',                      'Training',               '62120', TRUE,  FALSE, FALSE),
    ('LEGAL', 'ค่าทนาย/บัญชี',                'Legal / accounting',     '62130', TRUE,  FALSE, FALSE),
    ('INTR',  'ดอกเบี้ยจ่าย',                 'Interest expense',       '81010', FALSE, FALSE, FALSE),
    ('COGS',  'ต้นทุนสินค้าขาย',              'Cost of goods sold',     '51010', TRUE,  FALSE, TRUE),
    ('CAPEX', 'สินทรัพย์ถาวร (capitalize)',   'Fixed asset (capex)',    '12200', TRUE,  TRUE,  FALSE),
    ('MISC',  'อื่น ๆ',                       'Miscellaneous',          '62990', TRUE,  FALSE, FALSE)
) AS v(code, th, en, acct, rec, capex, cogs)
ON CONFLICT (company_id, category_code) DO NOTHING;
