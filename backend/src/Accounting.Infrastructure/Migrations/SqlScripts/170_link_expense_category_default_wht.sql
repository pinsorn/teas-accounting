-- Wire the demo SVC expense category to the demo SVC WHT type (3% / PND53) so a
-- Payment Voucher created from that category auto-fills the WHT type and can issue
-- its 50 ทวิ (CLAUDE.md §12.1). Idempotent UPDATE.

UPDATE sys.expense_categories ec
SET default_wht_type_id = wt.wht_type_id
FROM tax.wht_types wt
WHERE ec.company_id = 1 AND ec.category_code = 'SVC'
  AND wt.company_id = 1 AND wt.code = 'SVC'
  AND ec.default_wht_type_id IS DISTINCT FROM wt.wht_type_id;
