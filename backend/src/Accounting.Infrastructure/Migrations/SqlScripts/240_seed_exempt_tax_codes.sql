-- Sprint 9 B2 — default VAT tax-code set for company 1 (Reptify exempt/zero-rated use case).
-- R-Q3 applied: category is DERIVED from is_exempt/is_zero_rated (no category column);
-- only legal_ref is new. Spec listed master.tax_codes(name_en, rate) — the real schema
-- is tax.tax_codes with NO name_en and rate held in tax.tax_rates (not a scalar). Seed
-- adapted to the actual columns (mechanism note → Report-Backend14). Standard taxable
-- VAT7 / VAT-IN7 added so the ภ.พ.30 sales/purchase category join is complete.
-- Idempotent: ON CONFLICT (company_id, code) re-syncs the category + legal_ref.

INSERT INTO tax.tax_codes
  (company_id, code, name_th, tax_type, direction,
   is_recoverable, is_exempt, is_zero_rated, is_reverse_charge, is_active, legal_ref)
VALUES
  -- Standard taxable VAT 7% (ม.80) — for ภ.พ.30 taxable categorisation join
  (1, 'VAT7',             'ภาษีขาย 7%',                'VAT', 'OUTPUT', TRUE,  FALSE, FALSE, FALSE, TRUE, 'ม.80'),
  (1, 'VAT-IN7',          'ภาษีซื้อ 7%',               'VAT', 'INPUT',  TRUE,  FALSE, FALSE, FALSE, TRUE, 'ม.80'),
  -- Zero-rated (ม.80/1)
  (1, 'VAT-OUT-0-EXP',    'ส่งออก',                    'VAT', 'OUTPUT', TRUE,  FALSE, TRUE,  FALSE, TRUE, 'ม.80/1(1)'),
  (1, 'VAT-OUT-0-SVC-ABR','บริการในไทยใช้ในต่างประเทศ', 'VAT', 'OUTPUT', TRUE,  FALSE, TRUE,  FALSE, TRUE, 'ม.80/1(2)'),
  -- Exempt (ม.81)
  (1, 'EXEMPT-AGRI',      'พืชผลทางการเกษตร',          'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ก)'),
  (1, 'EXEMPT-LIVE',      'สัตว์มีชีวิต',              'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ข)'),
  (1, 'EXEMPT-FERT',      'ปุ๋ย',                      'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ค)'),
  (1, 'EXEMPT-FEED',      'อาหารสัตว์',                'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ง)'),
  (1, 'EXEMPT-VETMED',    'ยาเคมีสัตว์/พืช',           'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(จ)'),
  (1, 'EXEMPT-BOOK',      'หนังสือ นิตยสาร',           'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ฉ)'),
  (1, 'EXEMPT-EDU',       'การศึกษา',                  'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ช)'),
  (1, 'EXEMPT-MED',       'การแพทย์',                  'VAT', 'OUTPUT', FALSE, TRUE,  FALSE, FALSE, TRUE, 'ม.81(1)(ญ)')
ON CONFLICT (company_id, code) DO UPDATE
  SET is_exempt     = EXCLUDED.is_exempt,
      is_zero_rated = EXCLUDED.is_zero_rated,
      legal_ref     = EXCLUDED.legal_ref,
      is_active     = EXCLUDED.is_active;
