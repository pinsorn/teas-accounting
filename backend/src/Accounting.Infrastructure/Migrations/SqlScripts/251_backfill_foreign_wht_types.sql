-- 251 — backfill foreign-payee WHT types (ภ.ง.ด.54 / ม.70) for companies created before
-- FOR-SVC / FOR-ROYAL were added to the master defaults. Seed 250 only seeded company 1;
-- MasterDataServices.DefaultWhtTypes covers companies created since, but earlier companies
-- (e.g. the manual-demo co2) have no ภ.ง.ด.54 income type, so the ม.70 routing has nothing to
-- pick. Insert the two ม.70 types for every company that already has WHT types but lacks a
-- PND54 one. Additive + idempotent (unique key = company_id, code, effective_from).
INSERT INTO tax.wht_types
    (company_id, code, name_th, name_en, income_type_code, form_type, rate,
     is_active, effective_from, effective_to)
SELECT c.company_id, v.code, v.name_th, v.name_en, v.income_type_code, v.form_type, v.rate,
       TRUE, DATE '2020-01-01', NULL
FROM (SELECT DISTINCT company_id FROM tax.wht_types) AS c
CROSS JOIN (VALUES
    ('FOR-SVC'::text,   'ค่าบริการ ต่างประเทศ'::text, 'Foreign service'::text, '8'::text, 'PND54'::text, 0.15::numeric),
    ('FOR-ROYAL'::text, 'ค่าสิทธิ ต่างประเทศ'::text,  'Foreign royalty'::text, '3'::text, 'PND54'::text, 0.15::numeric)
) AS v(code, name_th, name_en, income_type_code, form_type, rate)
WHERE NOT EXISTS (
    SELECT 1 FROM tax.wht_types w
    WHERE w.company_id = c.company_id AND w.form_type = 'PND54'
)
ON CONFLICT (company_id, code, effective_from) DO NOTHING;
