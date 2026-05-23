-- cont. 67 (non-VAT mode) — irrecoverable input-VAT expense account for ภ.พ.36
-- (ม.83/6) reverse-charge paid by a NON-VAT-registered service receiver: the VAT
-- must be remitted but can never be reclaimed (no ภ.พ.30), so it is a permanent
-- expense. Maps to GlAccountsOptions.IrrecoverableVatExpenseAccount. Additive +
-- idempotent. Column set matches seed 120 / 230 (no name_en/subtype columns).

INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_type, normal_balance,
     is_header, is_active, created_at)
VALUES
    (1, '5350', 'ภาษีซื้อขอคืนไม่ได้', 'EXPENSE', 'DR', FALSE, TRUE, now())
ON CONFLICT (company_id, account_code) DO NOTHING;
