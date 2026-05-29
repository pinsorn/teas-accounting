-- Sprint 9 C1 — foreign-payee WHT types (ภ.ง.ด.54, 15%), carried from the
-- Sprint 8.6/8.7 deferral. WhtFormType enum gained `Pnd54` this sprint (C1).
-- Additive + idempotent; unique key = (company_id, code, effective_from).
-- Kept in sync with CompanyService.CreateAsync DefaultWhtTypes.

INSERT INTO tax.wht_types
    (company_id, code, name_th, name_en, income_type_code, form_type, rate,
     is_active, effective_from, effective_to)
VALUES
    (1, 'FOR-SVC',   'ค่าบริการ ต่างประเทศ', 'Foreign service', '8', 'PND54', 0.15, TRUE, DATE '2020-01-01', NULL),
    (1, 'FOR-ROYAL', 'ค่าสิทธิ ต่างประเทศ',  'Foreign royalty', '3', 'PND54', 0.15, TRUE, DATE '2020-01-01', NULL)
ON CONFLICT (company_id, code, effective_from) DO NOTHING;
