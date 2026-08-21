# Banking

บัญชีธนาคาร, การนำเข้า statement และการกระทบยอดธนาคาร.

Bank-account master data, statement import, and bank reconciliation matching + inline journal entries.

## Bank Accounts
Read gated by `bank.account.read`; write gated by `bank.account.manage`.
- `POST /bank-accounts` — create. **Auth:** `bank.account.manage`. → `201` `{ bank_account_id }`.
- `PUT /bank-accounts/{id}` — update. **Auth:** `bank.account.manage`. Path `id` (int). → `204`.
- `DELETE /bank-accounts/{id}` — soft-deactivate. **Auth:** `bank.account.manage`. → `204`.
- `GET /bank-accounts` — list. **Auth:** `bank.account.read`. Query: `includeInactive?` (bool). → `200`.
- `GET /bank-accounts/{id}` — detail. **Auth:** `bank.account.read`. → `200` / `404`.

## Statement Imports
Gated by `bank.statement_import`. Mounted at `/bank-accounts/{bankAccountId}/imports`.
- `POST /bank-accounts/{bankAccountId}/imports` — upload a statement (`multipart/form-data`, part `file`; CSV adapter live, a K-Plus PDF adapter reserves the optional `password` part). → `201` `{ statementImportId, ... }`.
- `GET /bank-accounts/{bankAccountId}/imports` — list imports for the account. → `200`.
- `GET /bank-accounts/{bankAccountId}/imports/{importId}/lines` — statement lines for one import. → `200`.
- `DELETE /bank-accounts/{bankAccountId}/imports/{importId}` — delete an import that was uploaded in error (remediation; same permission as create, not a separate one). → `204`.

## Bank Reconciliation
Matching routes are gated by `bank.reconcile` (suggestions are read-only but share the same gate as confirm/unmatch/journal/ignore per spec). Mounted at `/bank-accounts/{bankAccountId}/lines/{lineId}`.
- `GET /bank-accounts/{bankAccountId}/lines/{lineId}/suggestions` — suggested document matches for one statement line. → `200`.
- `POST /bank-accounts/{bankAccountId}/lines/{lineId}/match` — confirm a match. → `204`.
- `POST /bank-accounts/{bankAccountId}/lines/{lineId}/unmatch` — undo a match. → `204`.
- `POST /bank-accounts/{bankAccountId}/lines/{lineId}/journal` — create an inline journal entry for a line with no matching document (e.g. bank fees, interest). → `200`.
- `POST /bank-accounts/{bankAccountId}/lines/{lineId}/ignore` — mark the line ignored (won't need matching). → `204`.
- `POST /bank-accounts/{bankAccountId}/lines/{lineId}/unignore` — undo ignore. → `204`.
- `GET /bank-accounts/{bankAccountId}/reconciliation` — reconciliation report for a date range. **Auth:** `bank.report_read`. Query: `from`, `to`. → `200`.
