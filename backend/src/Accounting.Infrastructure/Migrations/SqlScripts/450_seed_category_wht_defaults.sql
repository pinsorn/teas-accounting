-- BP-01 (RV2) follow-up — wire the §17.3 default ภาษีหัก ณ ที่จ่าย (withholding) type onto the
-- company-1 expense categories, so the Payment Voucher picker PRE-FILLS the right WHT type (the
-- user can still override per line — this is a convenience default, not a forced value).
--
-- Only the UNAMBIGUOUS mappings are set (category concept AND §17.3 rate both match an existing
-- tax.wht_types row from seed 220). Idempotent: resolves wht_type_id by code at run time.
--
-- Mapping (expense_categories.category_code -> tax.wht_types.code, with §17.3 rate):
--   RENT  -> RENT  (ค่าเช่า 5%)
--   PROF  -> PROF  (ค่าวิชาชีพอิสระ 3%)
--   LEGAL -> PROF  (ทนาย/บัญชี = วิชาชีพอิสระ 3%)
--   MARK  -> ADS   (ค่าโฆษณา 2%)
--   INTR  -> INT   (ดอกเบี้ย 1%)
--   IT    -> SVC   (ค่าบริการ นิติบุคคล 3%)
--
-- LEFT NULL on purpose (flagged for Ham — needs a decision, not a guess):
--   WAGE  — §17.3 "ค่าจ้างแรงงาน 3%"; closest is wht_types CONTRACT "ค่าจ้างทำของ/รับเหมา" 3%,
--           but ค่าจ้างแรงงาน (labour) ≠ จ้างทำของ (piecework/contract) → ambiguous, not set.
--   SAL   — payroll ภ.ง.ด.1; seed 220 intentionally excludes a PND1 wht_type → no row to map.
--   All other categories (§17.3 shows "—" for WHT) carry no withholding → stay NULL.

UPDATE sys.expense_categories ec
   SET default_wht_type_id = (SELECT wht_type_id FROM tax.wht_types w
                               WHERE w.company_id = 1 AND w.code = m.wht_code AND w.is_active)
  FROM (VALUES
        ('RENT',  'RENT'),
        ('PROF',  'PROF'),
        ('LEGAL', 'PROF'),
        ('MARK',  'ADS'),
        ('INTR',  'INT'),
        ('IT',    'SVC')
       ) AS m(cat_code, wht_code)
 WHERE ec.company_id = 1
   AND ec.category_code = m.cat_code
   AND ec.default_wht_type_id IS NULL
   AND EXISTS (SELECT 1 FROM tax.wht_types w
                WHERE w.company_id = 1 AND w.code = m.wht_code AND w.is_active);
