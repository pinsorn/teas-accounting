-- Populate company_id=1 structured registered address (the RD forms need each part in its own
-- box). Mirrors the legacy free-text line "1 อาคารเดโม ชั้น 1 ถนนสาทร" (seed 420). Idempotent —
-- only fills when the structured columns are still empty, so it never clobbers real edits.
UPDATE master.company_profile
SET reg_building = 'อาคารเดโม',
    reg_floor    = '1',
    reg_house_no = '1',
    reg_street   = 'สาทร'
WHERE company_id = 1
  AND COALESCE(reg_building, reg_house_no, reg_street, '') = '';
