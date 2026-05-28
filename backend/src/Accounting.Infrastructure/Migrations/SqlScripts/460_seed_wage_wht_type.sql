-- BP-01 (RV2) follow-up — wire the §17.3 default WHT for the WAGE expense category, which
-- seed 450 deliberately left NULL pending a tax-section decision. Resolution per Ham:
-- ค่าจ้างแรงงาน paid to a NON-employee individual (e.g. a piece-rate / day-rate worker who
-- is not on payroll) is assessable income under ประมวลรัษฎากร ม.40(2) "ค่าจ้าง", reported on
-- ภ.ง.ด.3 (individual payee), withheld at 3% per §17.3. (Employees / PND1 monthly payroll
-- progressive withholding remain out of scope — see SAL note below + seed 220 header.)
--
-- Two steps, both idempotent:
--   1) Insert the WAGE row into tax.wht_types if absent  (ON CONFLICT DO NOTHING).
--   2) UPDATE sys.expense_categories.default_wht_type_id for category_code='WAGE' to point
--      at the resolved tax.wht_types.wht_type_id, but ONLY if still NULL (so a manual override
--      by an admin isn't clobbered on every startup).
--
-- SAL (เงินเดือนพนักงาน, ภ.ง.ด.1) intentionally stays NULL. PND1 is monthly payroll with a
-- progressive rate computed from the employee's annual income — a different subsystem (a
-- per-employee withholding calc + monthly filing), not a flat per-line default. Will land
-- when payroll lands.

INSERT INTO tax.wht_types
    (company_id, code, name_th, name_en, income_type_code, form_type, rate,
     is_active, effective_from, effective_to)
VALUES
    -- income_type_code '2' = ค่าจ้าง/ค่าธรรมเนียม line on ภ.ง.ด.3 (form line code, NOT the
    -- ม.40 section verbatim — matches the existing seed-220 convention; the 50ทวิ PDF prints
    -- this string directly as the form section).
    (1, 'WAGE', 'ค่าจ้างแรงงาน', 'Labour wages (non-employee, individual)',
     '2', 'PND3', 0.03, TRUE, DATE '2020-01-01', NULL)
ON CONFLICT (company_id, code, effective_from) DO NOTHING;

UPDATE sys.expense_categories ec
   SET default_wht_type_id = (SELECT wht_type_id FROM tax.wht_types w
                               WHERE w.company_id = 1 AND w.code = 'WAGE' AND w.is_active)
 WHERE ec.company_id = 1
   AND ec.category_code = 'WAGE'
   AND ec.default_wht_type_id IS NULL
   AND EXISTS (SELECT 1 FROM tax.wht_types w
                WHERE w.company_id = 1 AND w.code = 'WAGE' AND w.is_active);
