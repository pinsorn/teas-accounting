-- Sprint 8.6 — expand tax.wht_types to the 13 standard domestic types for the
-- demo company. Additive + idempotent. SALARY/PND1 (payroll) and foreign PND54
-- intentionally excluded (Answer-Sana-Backend11 §9 scope cut; WhtFormType enum =
-- Pnd3/Pnd53/Pnd1 only). Existing RENT/SVC/ADS (seed 120) are kept as-is — NOT
-- renamed (Question-Backend12 R-B2: would break seed 170 + AP-side Sprint 5/6).
--
-- effective_from defaults to 2020-01-01 (added by AddARWhtSupport migration,
-- safe backfill before any real txn). Unique key = (company_id, code,
-- effective_from), so ON CONFLICT skips the 3 existing rows + the new 10.

INSERT INTO tax.wht_types
    (company_id, code, name_th, name_en, income_type_code, form_type, rate,
     is_active, effective_from, effective_to)
VALUES
    (1, 'RENT',      'ค่าเช่า',                       'Rental',                '5', 'PND3',  0.05,   TRUE, DATE '2020-01-01', NULL),
    (1, 'SVC',       'ค่าบริการ (นิติบุคคล)',          'Service (corporate)',   '8', 'PND53', 0.03,   TRUE, DATE '2020-01-01', NULL),
    (1, 'ADS',       'ค่าโฆษณา',                      'Advertising',           '8', 'PND53', 0.02,   TRUE, DATE '2020-01-01', NULL),
    (1, 'SVC-IND',   'ค่าบริการ (บุคคลธรรมดา)',        'Service (individual)',  '8', 'PND3',  0.03,   TRUE, DATE '2020-01-01', NULL),
    (1, 'PROF',      'ค่าวิชาชีพอิสระ',                'Professional fee',      '6', 'PND53', 0.03,   TRUE, DATE '2020-01-01', NULL),
    (1, 'TRANS',     'ค่าขนส่ง',                      'Transport',             '8', 'PND53', 0.01,   TRUE, DATE '2020-01-01', NULL),
    (1, 'COMM',      'ค่านายหน้า / คอมมิชชั่น',         'Commission',            '2', 'PND53', 0.03,   TRUE, DATE '2020-01-01', NULL),
    (1, 'ROYAL',     'ค่าสิทธิ',                      'Royalty',               '3', 'PND53', 0.03,   TRUE, DATE '2020-01-01', NULL),
    (1, 'INT',       'ดอกเบี้ย',                      'Interest',              '4', 'PND53', 0.01,   TRUE, DATE '2020-01-01', NULL),
    (1, 'PRIZE',     'รางวัล / ส่วนลดส่งเสริมการขาย',   'Prize / incentive',     '8', 'PND53', 0.05,   TRUE, DATE '2020-01-01', NULL),
    (1, 'AGRI',      'ค่าซื้อพืชผลเกษตร',              'Agricultural produce',  '8', 'PND53', 0.0075, TRUE, DATE '2020-01-01', NULL),
    (1, 'ENTERTAIN', 'ค่าจ้างนักแสดง / บันเทิง',        'Entertainer fee',       '8', 'PND53', 0.05,   TRUE, DATE '2020-01-01', NULL),
    (1, 'CONTRACT',  'ค่าจ้างทำของ / รับเหมา',          'Contract work',         '7', 'PND53', 0.03,   TRUE, DATE '2020-01-01', NULL)
ON CONFLICT (company_id, code, effective_from) DO NOTHING;
