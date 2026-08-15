# PROGRESS — Balance Sheet + Sub-ledger suite — ✅ COMPLETE 2026-07-08

Merged to main via PR #56 (2e7999c). Feature suite done:
1. Balance Sheet — FE page + get_balance_sheet MCP tool (commit e3b4aa6)
2. AR Aging — page + REST + get_ar_aging MCP tool
3. Customer Statement — page + REST + get_customer_statement MCP tool
4. Vendor Ledger — page + REST + get_vendor_ledger MCP tool
5. Reconciliation block (subledger vs GL control account, GlAccountsOptions-driven, differences surfaced) on all three sub-ledger surfaces (commit 9095868)

Verification: full suite 681/0/8 (baseline skips); Tier-2 Opus money-lens APPROVE (nits applied); Tier-3 gate green; CI backend+frontend pass; Fable full-diff review done.

## Released + deployed
- v1.14.0 tagged (PR #57) and DEPLOYED to production 2026-07-08 ~15:5x: API `1.14.0+7a8ab51` DEPLOY_OK, FE overlay FE_DEPLOY_OK, E2E public-domain probes all green (login 200, new API routes 401 via proxy, FE pages 307→login, public pdf garbage 404). No DB changes shipped; no rollback triggered. Scripts: publish/deploy-api-v1140.sh, publish/deploy-fe-v1140.sh.

## Follow-up candidates (not committed to)
- CSV export on ar-aging (ap-aging has one), docType i18n labels in statement/ledger tables, AmountPaid-semantics doc note.
- Next-cycle gap analysis (2026-07-08): Tier-1 = bank reconciliation (CSV-first) / recurring invoices / expense claims; Tier-2 = fixed assets+depreciation, year-end closing JE, inventory; quick wins = period-close UI, ar-aging CSV.
