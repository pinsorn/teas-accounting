-- ภ.ง.ด.2 interest-to-individual WHT type, for EVERY existing company.
-- New companies get it via DefaultWhtTypes (MasterDataServices.cs).
-- Rate 15% = ประมวลรัษฎากร ม.50(2) — ม.40(4) ดอกเบี้ย paid to a บุคคลธรรมดา.
-- tax.wht_types is a G1 (never-bypassable) FORCE-RLS table: pin app.company_id per company.
-- Do NOT use a bare cross-company INSERT — startup runs with app.company_id UNSET under the
-- NOBYPASSRLS `teas` role and every row would 42501 (v1.22.0 + v1.24.0, both rolled back).
-- teas_test connects as superuser and cannot catch this. Mirrors 621/631.
DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);
        INSERT INTO tax.wht_types
            (company_id, code, name_th, name_en, income_type_code, pnd2_income_code,
             form_type, rate, is_active, effective_from, effective_to)
        SELECT c.company_id, 'INT-IND', 'ดอกเบี้ยจ่าย (บุคคลธรรมดา)', 'Interest paid (individual)',
               '4', '2', 'PND2', 0.15, TRUE, DATE '2020-01-01', NULL
        WHERE NOT EXISTS (
            SELECT 1 FROM tax.wht_types w
            WHERE w.company_id = c.company_id AND w.code = 'INT-IND')
        ON CONFLICT (company_id, code, effective_from) DO NOTHING;
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;
