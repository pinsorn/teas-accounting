-- ============================================================
-- 450_seed_demo_company_tax_codes.sql
-- ============================================================
-- The default VAT tax-code set for the SQL-seeded demo tenants
-- co2 (Manual Demo Co., id = 2) and co3 (Non-VAT Demo Shop, id = 3).
--
-- WHY: app-created companies get this set from CompanyService.CreateAsync
--   (MasterDataServices.cs — the DefaultTaxCodes array, mirrors seed 240
--   for company 1). co2/co3 are seeded by raw SQL (400/440) that INSERTs
--   the company row directly and bypasses CreateAsync, so they had ZERO
--   rows in tax.tax_codes — breaking the ภ.พ.30 sales/purchase category
--   join and leaving every transaction without a tax-code reference.
--
-- This script mirrors DefaultTaxCodes EXACTLY (same codes, name_th,
--   tax_type, direction, is_exempt/is_zero_rated flags, legal_ref) for
--   BOTH company_id = 2 AND company_id = 3. The rate itself is NOT a
--   scalar on tax.tax_codes (held in companies.vat_rate per §4.6 + the
--   per-line snapshot); CreateAsync does not seed tax.tax_rates, so this
--   seed mirrors tax_codes only — identical scope to the app path.
--
-- Applied in lexical order by DbInitializer AFTER 400/440 (which create
--   the company rows) and apply-once tracked. Idempotent: ON CONFLICT
--   (company_id, code) DO NOTHING.
-- ============================================================

INSERT INTO tax.tax_codes
  (company_id, code, name_th, tax_type, direction,
   is_recoverable, is_exempt, is_zero_rated, is_reverse_charge, is_active, legal_ref)
VALUES
  -- Standard taxable VAT 7% (ม.80) — for ภ.พ.30 taxable categorisation join
  (2, 'VAT7',             'ภาษีขาย 7%',                'VAT', 'OUTPUT', TRUE,  FALSE, FALSE, FALSE, TRUE, 'ม.80'),
  (2, 'VAT-IN7',          'ภาษีซื้อ 7%',               'VAT', 'INPUT',  TRUE,  FALSE, FALSE, FALSE, TRUE, 'ม.80'),
  -- Zero-rated (ม.80/1)
  (2, 'VAT-OUT-0-EXP',    'ส่งออก',                    'VAT', 'OUTPUT', TRUE,  FALSE, TRUE,  FALSE, TRUE, 'ม.80/1(1)'),
  (2, 'VAT-OUT-0-SVC-ABR','บริการในไทยใช้ในต่างประเทศ', 'VAT', 'OUTPUT', TRUE,  FALSE, TRUE,  FALSE, TRUE, 'ม.80/1(2)'),
  -- Exempt (ม.81)
  (2, 'EXEMPT-AGRI',      'พืชผลทางการเกษตร',          'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ก)'),
  (2, 'EXEMPT-LIVE',      'สัตว์มีชีวิต',              'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ข)'),
  (2, 'EXEMPT-FERT',      'ปุ๋ย',                      'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ค)'),
  (2, 'EXEMPT-FEED',      'อาหารสัตว์',                'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ง)'),
  (2, 'EXEMPT-VETMED',    'ยาเคมีสัตว์/พืช',           'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(จ)'),
  (2, 'EXEMPT-BOOK',      'หนังสือ นิตยสาร',           'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ฉ)'),
  (2, 'EXEMPT-EDU',       'การศึกษา',                  'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ช)'),
  (2, 'EXEMPT-MED',       'การแพทย์',                  'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ญ)'),
  -- co3 (Non-VAT Demo Shop) — same default set; a non-VAT company still
  -- carries the standard tax-code reference data for consistent joins.
  (3, 'VAT7',             'ภาษีขาย 7%',                'VAT', 'OUTPUT', TRUE,  FALSE, FALSE, FALSE, TRUE, 'ม.80'),
  (3, 'VAT-IN7',          'ภาษีซื้อ 7%',               'VAT', 'INPUT',  TRUE,  FALSE, FALSE, FALSE, TRUE, 'ม.80'),
  (3, 'VAT-OUT-0-EXP',    'ส่งออก',                    'VAT', 'OUTPUT', TRUE,  FALSE, TRUE,  FALSE, TRUE, 'ม.80/1(1)'),
  (3, 'VAT-OUT-0-SVC-ABR','บริการในไทยใช้ในต่างประเทศ', 'VAT', 'OUTPUT', TRUE,  FALSE, TRUE,  FALSE, TRUE, 'ม.80/1(2)'),
  (3, 'EXEMPT-AGRI',      'พืชผลทางการเกษตร',          'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ก)'),
  (3, 'EXEMPT-LIVE',      'สัตว์มีชีวิต',              'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ข)'),
  (3, 'EXEMPT-FERT',      'ปุ๋ย',                      'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ค)'),
  (3, 'EXEMPT-FEED',      'อาหารสัตว์',                'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ง)'),
  (3, 'EXEMPT-VETMED',    'ยาเคมีสัตว์/พืช',           'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(จ)'),
  (3, 'EXEMPT-BOOK',      'หนังสือ นิตยสาร',           'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ฉ)'),
  (3, 'EXEMPT-EDU',       'การศึกษา',                  'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ช)'),
  (3, 'EXEMPT-MED',       'การแพทย์',                  'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ญ)')
ON CONFLICT (company_id, code) DO NOTHING;
