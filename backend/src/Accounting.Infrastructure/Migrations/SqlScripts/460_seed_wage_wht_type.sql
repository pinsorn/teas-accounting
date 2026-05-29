-- BP-01 (RV2) follow-up — wire the §17.3 default WHT for the WAGE expense category, which
-- seed 450 deliberately left NULL pending a tax-section decision.
--
-- ค่าจ้างแรงงาน paid to a NON-employee individual (a piece-rate / day-rate worker not on
-- payroll) is "รับจ้างทำของ" (work-for-hire labour, no materials supplied) = ประมวลรัษฎากร
-- ม.40(8), filed on ภ.ง.ด.3 (individual payee), withheld at 3%. Cross-checked against the
-- official RD ภ.ง.ด.3/53 booklet (ลำดับ 8: ค่าจ้างทำของ ม.40(7) จัดหาสัมภาระ / ม.40(8)
-- รับจ้างทำของ → 3%) and the ภ.ง.ด.3 form itself, whose income box wording is verbatim
-- ม.40 sub-sections (boxes 1–4 = 40(1)–(4); the catch-all box = ทำของ/โฆษณา/เช่า/ขนส่ง/
-- บริการ = 40(5)–(8)). The ภ.ง.ด.3 form per RD covers ONLY ม.40(5)–(8) for individuals,
-- which is why labour lands in 40(8), NOT 40(2). [JUDGMENT CALL — flagged for CPA review:
-- if a given WAGE payment is a pure service fee (ค่าจ้างทั่วไป) rather than รับจ้างทำของ it
-- would be ม.40(2); the per-line override on the PV lets a user reclassify. Default = 40(8).]
-- (Employees / PND1 monthly payroll progressive withholding remain out of scope — see SAL
-- note below + seed 220 header.)
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
    -- income_type_code '8' = ม.40(8) รับจ้างทำของ (work-for-hire labour). The value IS the
    -- ม.40 sub-section number (verbatim what the ภ.ง.ด.3 / 50ทวิ income box uses); the
    -- 50ทวิ PDF renders it as "ตามมาตรา 40(8)".
    (1, 'WAGE', 'ค่าจ้างแรงงาน', 'Labour wages (non-employee, individual)',
     '8', 'PND3', 0.03, TRUE, DATE '2020-01-01', NULL)
ON CONFLICT (company_id, code, effective_from) DO NOTHING;

UPDATE sys.expense_categories ec
   SET default_wht_type_id = (SELECT wht_type_id FROM tax.wht_types w
                               WHERE w.company_id = 1 AND w.code = 'WAGE' AND w.is_active)
 WHERE ec.company_id = 1
   AND ec.category_code = 'WAGE'
   AND ec.default_wht_type_id IS NULL
   AND EXISTS (SELECT 1 FROM tax.wht_types w
                WHERE w.company_id = 1 AND w.code = 'WAGE' AND w.is_active);
