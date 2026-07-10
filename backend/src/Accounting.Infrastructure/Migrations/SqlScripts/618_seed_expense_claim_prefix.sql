-- Expense Claims (specs/expense-claims.md open question 5) — 'EX' document prefix. Cosmetic
-- (UI + gap-view only); NumberSequenceService self-seeds sys.number_sequences on first use.
-- sys.document_prefixes is plain metadata (no RLS) — a bare INSERT is fine, mirrors
-- 482_seed_payroll_prefix_and_accounts.sql / 100_seed_document_prefixes.sql.

INSERT INTO sys.document_prefixes
    (prefix_code, document_type, description_th, description_en, requires_etax, is_fiscal_doc, is_expense, is_active, created_at)
VALUES
    ('EX', 'EXPENSE_CLAIM', 'ใบเบิกค่าใช้จ่าย', 'Expense Claim', FALSE, TRUE, TRUE, TRUE, NOW())
ON CONFLICT (prefix_code) DO NOTHING;
