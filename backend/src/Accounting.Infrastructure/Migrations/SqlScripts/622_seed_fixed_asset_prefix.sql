-- Fixed Assets (specs/fixed-assets.md §8.4) — 'FA' document prefix, the asset MASTER doc
-- only (assigned at Activate). Every GL JE for depreciation/disposal stays "JV"-numbered
-- by GlPostingService.PostManualEntryAsync's BuildAndPostAsync — not a fiscal doc, not an
-- expense doc. sys.document_prefixes is plain metadata (no RLS) — a bare INSERT is fine,
-- mirrors 618_seed_expense_claim_prefix.sql / 100_seed_document_prefixes.sql.

INSERT INTO sys.document_prefixes
    (prefix_code, document_type, description_th, description_en, requires_etax, is_fiscal_doc, is_expense, is_active, created_at)
VALUES
    ('FA', 'FIXED_ASSET', 'สินทรัพย์ถาวร', 'Fixed Asset', FALSE, FALSE, FALSE, TRUE, NOW())
ON CONFLICT (prefix_code) DO NOTHING;
