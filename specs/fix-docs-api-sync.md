# API docs sync — openapi.yaml + manual (docs-only, no behavior changes, no commits)

Scope: sync `docs/api/openapi.yaml` and `docs/manual/api/*.md` to the real route
surface in `backend/src/Accounting.Api/Endpoints/*.cs` + `Program.cs`. Full
two-way sync (add missing, fix stale/changed, remove routes that no longer
exist) — the "known-missing" list in the dispatch is a floor, not the ceiling.

## Method
1. Enumerated every `Map(Get|Post|Put|Delete|Patch)` call across all 40
   `Endpoints/*.cs` files + `Program.cs` direct mappings (`/system/info`,
   `/system/vat-threshold-status`, `/health`, OAuth routes — OAuth routes live
   in `OAuth/OAuthEndpoints.cs`, outside the named `Endpoints/*.cs` scope, and
   were left out of this pass; flagged below for a follow-up if wanted).
2. Diffed against `grep -n "^  /" docs/api/openapi.yaml` (150 existing path
   keys).
3. Confirmed every ADD against the actual endpoint file (file:line cited per
   family below).
4. Also reverse-diffed: any yaml path with no code backing → removed (stale).

## Yaml checklist (by family)

### A. Known-missing list from dispatch — verified
- [x] `GET /employees/lookup` — EmployeeEndpoints.cs:23. Added before `/employees`.
- [x] `GET /tax-filings/pp36/pdf` — TaxFilingEndpoints.cs:118. Added near pp01/pp09.
- [x] `DELETE /bank-accounts/{bankAccountId}/imports/{importId}` — StatementImportEndpoints.cs:46. Added with the rest of the Statement Imports family.
- [x] `PUT /fixed-assets/{id}` + fixed-asset lifecycle — FixedAssetEndpoints.cs (whole file, 13 routes incl. depreciation-runs). Added as a new family.
- [x] Expense-claims lifecycle (CRUD + submit/approve/pay/reject/cancel) — ExpenseClaimEndpoints.cs (whole file, 10 routes). Added as a new family.
- [x] Bank-account + statement-import routes — BankAccountEndpoints.cs (5) + StatementImportEndpoints.cs (4) + BankReconciliationEndpoints.cs (6). Added as new families.
- [x] Per-document `/{doc}/{id}/activity` routes (13, each gated by that doc's own read perm) — ActivityEndpoints.cs. Documented as ONE templated path (matches the yaml's own `/{docRoute}/{id}/mark-printed` precedent) enumerating the 13 docRoute values + the CN/DN/payroll-runs permission quirks.
- [x] Payroll filing artifact routes (pnd1/pnd1a/sso pdf+file, 50ทวิ) — **already fully documented** in yaml (lines 2652-2716). Verified via PayrollEndpoints.cs:87-151 — no gap. (Two adjacent payroll routes WERE missing though: `PUT .../deductions`, `GET .../sso-schedule` — added.)
- [x] Quotation/PO cancel-reject with ReasonBody + 500-char trim — SalesChainEndpoints.ReasonBody / PurchaseOrderEndpoints.ReasonBody already documented at existing `/quotations/{id}/reject`, `/quotations/{id}/cancel`, `/purchase-orders/{id}/cancel` (reason as free `type: string`, matching code — `RequireReason` truncation is a behavior detail already covered by the existing free-text schema, not a doc gap). No change needed.
- [ ] `billing_note.lines_not_reconciled` typed error on BN issue — not yet cross-checked against `/billing-notes/{id}/issue`'s documented error responses. SKIPPED (out of time budget this pass; existing entry already has a `409` `$ref: ErrorEnvelopeV1` which covers the shape generically).

### B. Additional gaps found (full sweep) — verified + added
- [x] Purchase Orders full family (was only `/{id}/paper` + api/v1 pdf) — PurchaseOrderEndpoints.cs (13 routes incl. outstanding-po, ap-aging already present).
- [x] Business Units — BusinessUnitEndpoints.cs (6 routes incl. company-setting GET/PUT).
- [x] Journals — JournalEndpoints.cs (4 routes incl. /manual).
- [x] Periods — PeriodEndpoints.cs (6 routes: month close/reopen/status, year close/reopen/status).
- [x] Attachments — AttachmentEndpoints.cs (5 routes).
- [x] Master data: Branches, Vendors, Chart of Accounts, Document Prefixes, Tax Codes — MasterEndpoints.cs (Companies sub-family already documented at existing `/companies`, `/companies/{id}` — left alone).
- [x] WHT Types CRUD + change-rate (reactivate already documented) — WhtTypeEndpoints.cs.
- [x] Document Cross-Refs (3 typed routes) + `/documents/purchase-chain` (`/documents/chain` already documented) — DocumentCrossRefEndpoints.cs.
- [x] RBAC user signature + profile — RbacAdminEndpoints.cs:96,111.
- [x] Reports family: trial-balance, profit-loss, sales-summary, general-ledger (+accounts, +export), ar-aging (+export), customer-statement, vendor-ledger, pending-agent-approvals, wht-receivable-register/aging/missing-cert, vat-register, pnd30(GET) — ReportEndpoints.cs. (`/reports/output-vat-register`, `/reports/input-vat-register`, `/reports/ap-aging` already documented — left alone.)
- [x] Auth: `POST /auth/refresh` — AuthEndpoints.cs:108 (`/auth/login`, `/auth/switch-company/{id}` already documented).
- [x] Company Profile: `/registered-address`, `/logo`, `/stamp` — CompanyProfileEndpoints.cs:48,89,107 (`/`, `/soft`, `/company-info`, `/hard` already documented).
- [x] Receipts: bare `GET /{id}`, `POST /{id}/wht-cert`, `POST /wht-base-suggest` — ReceiptEndpoints.cs:39,57,63 (create/post/list/pdf/paper already documented).
- [x] Sales chain gaps: `DELETE /quotations/{id}`, `POST /quotations/{id}/create-tax-invoice`, `GET /sales-orders` (list), `GET+PUT /sales-orders/{id}`, `POST /sales-orders/{id}/delivery-orders/full`, `POST /sales-orders/{id}/create-invoice`, `GET /delivery-orders` (list), `GET /delivery-orders/{id}` (bare) — SalesChainEndpoints.cs.
- [x] Tax Filings gaps: bare `POST /tax-filings/pnd30` (real submit route — see stale-fix below), `POST /tax-filings/pnd2`, `POST /tax-filings/pnd3`, `POST /tax-filings/pnd53`, `POST /tax-filings/pnd54`, `POST /tax-filings/pnd36`, `GET /tax-filings/pnd53/batch-file`, `GET /tax-filings/pnd3/batch-file`, `GET /tax-filings/pnd2/batch-file`, bare `GET /tax-filings` (list) — TaxFilingEndpoints.cs.
- [x] e-Tax: `GET /etax/submissions?tax_invoice_id=` — EtaxEndpoints.cs:19 (replaces the stale `/tax-invoices/{id}/etax-status`, see below).
- [x] `GET /public/pdf` (anonymous, token-gated) — PublicPdfEndpoints.cs:21.
- [x] `POST /admin/nonvat-ar-backfill` — AdminBackfillEndpoints.cs:21.
- [x] `POST /system/setup/instance-keys` — InstanceSetupEndpoints.cs:54.
- [x] `POST /system/setup/bootstrap-admin` — BootstrapAdminEndpoints.cs:40 (`/system/setup/status` already documented; bootstrap-admin was only referenced in prose, no actual path block).
- [x] MCP tool table: `list_employees` (master.employee.lookup or manage) — TeasMcpTools.cs:1903. Added row.

### C. Stale entries removed (route doesn't exist in code — "no route documented that doesn't exist" gate)
- [x] `GET /customers/search` — CustomerEndpoints.cs has only POST/PUT/GET{id}/GET(list); no `/search` route exists. Removed (yaml called it "legacy", but there's no code backing it at all).
- [x] `GET /tax-invoices/{id}/etax-status` — TaxInvoiceEndpoints.cs has no such route; real e-Tax status query is `GET /etax/submissions?tax_invoice_id=`. Replaced.
- [x] `POST /reports/pnd30/submit` — no such route; real route is `POST /tax-filings/pnd30?period=&mode=`. Replaced.
- [x] `GET /reports/pnd30/file` — no such route; `GET /tax-filings/pnd30/pdf` and `GET /tax-filings/pnd30/batch-file` already separately documented and correct. Removed (pure duplicate/stale).

### D. Explicitly out of scope this pass
- OAuth dynamic-client-registration routes (`/.well-known/oauth-protected-resource`, `/oauth/authorize`, `/oauth/register` in `OAuth/OAuthEndpoints.cs`) — outside the named `Endpoints/*.cs` scope; yaml's OAuth2 security scheme already says "Reserved for Phase 2" which underclaims what's actually live for MCP. Flagged, not fixed (would need its own review of whether this is meant to be public API surface).

### E. Second sweep (automated code→yaml diff script, `route_diff.py`) — additional gaps found + fixed
Wrote a regex-based scanner over all `Endpoints/*.cs` (MapGroup prefix + Map* suffix →
full path, normalized `{id:long}`→`{id}`) and diffed against parsed yaml paths/methods.
Caught what manual reading missed:
- [x] `GET /reports/outstanding-po` — PurchaseOrderEndpoints.cs:83. Added near ap-aging.
- [x] `GET /system/vat-threshold-status` — Program.cs:552. Added near /system/info.
- [x] `POST /payment-vouchers/{id}/cancel`, `POST /payment-vouchers/{id}/vendor-invoice` — PaymentVoucherEndpoints.cs:40,49. Added.
- [x] `DELETE /tax-adjustment-notes/{id}` — TaxAdjustmentNoteEndpoints.cs:36. Added.
- [x] `GET /wht-certificates`, `GET /wht-certificates/{id}` — WhtCertificateEndpoints.cs:18,22 (only /pdf was documented). Added.
- [x] `POST /auth/login` — AuthEndpoints.cs:17 (only referenced in prose before, no path block). Added.
- [x] `GET /customers/{id}`, `PUT /customers/{id}` — CustomerEndpoints.cs:33,48 (no path block existed). Added.
- [x] `GET /products/{id}`, `PUT /products/{id}`, `POST /products/{id}/deactivate` — ProductEndpoints.cs. Added.
- [x] `POST /expense-categories`, `PUT /expense-categories/{id}` — MasterEndpoints.cs (only GET list was documented). Added.
- Script false positives (verified against source, not real gaps): `/{route}/{{id}}/activity` and `/{route}/{{id}}/mark-printed` are C# string-interpolation artifacts of the templated routes already documented as `/{docRoute}/{id}/activity` / `/{docRoute}/{id}/mark-printed`; `GET /expense-categories/{id}` was a false positive from a variable-name collision in MasterEndpoints.cs (multiple private methods reuse the local name `g` for different `MapGroup` calls — the script's whole-file dict lookup misattributes routes across sub-methods that share the var name; confirmed by direct code read that `MapExpenseCategories` has no GET /{id}).
- **Found and fixed one YAML syntax bug introduced by this session's own edit**: an unquoted `description:` scalar containing `{ mfa_required: true }` broke the parser (`ScannerError: mapping values are not allowed here`) — plain YAML scalars can't contain `: ` (colon-space) unquoted. Fixed by quoting the string. Caught immediately by the parse gate, not shipped.
- Final state: 275 top-level paths (from 150), zero duplicate path keys, yaml parses clean.

## Manual checklist (docs/manual/api/*.md)
- [x] Read index.md + all 9 section files in full.
- [x] Employee lookup + payroll-data gating note — payroll.md, new "Name-only lookup" subsection under Employees; notes the MCP `list_employees` scope too (master.employee.lookup, master.employee.manage back-compat) since MCP isn't mentioned anywhere else in the manual (no other MCP note existed to update).
- [x] Bank rec import/delete — new file `banking.md` (Bank Accounts, Statement Imports incl. DELETE, Bank Reconciliation); index.md category table updated.
- [x] Expense claims — new file `expense-claims.md`; index.md updated.
- [x] Fixed assets + depreciation day-proration — new file `fixed-assets.md` with an explicit "Day-proration" subsection (verified against FixedAssetService.cs's actual first-month-fraction / trimmed-final-charge logic, not assumed); index.md updated.
- [x] Filing payer-tax-ID refusal behavior — tax-filings.md intro, new paragraph (verified against `PayerTaxIdRules.cs` — `filing.payer_tax_id_missing`, blank-or-all-zero check, used by Pnd1/Sso/Pnd50/Pnd51/VatRegForm/WhtFilingService).
- [x] Activity-per-document permissions — sales.md's Activity section FIXED: was stale ("All share `report.audit.read`"), now correctly says each docType uses that document's own read permission, with the R3 fix history and the CN/DN/payroll-runs quirks noted.
- [x] MCP notes — N/A, grepped `-i mcp` across all of `docs/manual/api/*.md`: zero matches, nothing to update (the one MCP-relevant fact, `list_employees`'s scope, was folded into the payroll.md employee-lookup note above instead, since there's no dedicated MCP section to touch).
- [x] Also fixed while reading (real headline-capability gaps found, not in the original list): rbac-admin.md (POST create user + active/password/signature/profile were entirely undocumented), auth-and-identity.md (`POST /auth/refresh` missing), sales.md (quotation create-tax-invoice, SO detail GET/PUT, SO delivery-orders/full, SO create-invoice), purchases.md (PO reopen + paper, PV cancel — both real actions with no doc trace), reports.md (STALE paths `/purchase-orders/reports/ap-aging` and `/purchase-orders/reports/outstanding-po` — real routes are bare `/reports/ap-aging` and `/reports/outstanding-po`; also added the previously-undocumented GL/subledger section), tax-filings.md (ภ.ง.ด.2 was missing entirely, pp36/pdf missing), master-data.md (company-profile `/company-info` + `/stamp` missing, `/tax-codes` missing entirely).

## Gates
- [x] `python -c "import yaml,sys;yaml.safe_load(open('docs/api/openapi.yaml',encoding='utf-8'))"` — parses clean (run repeatedly through the session; final run after all edits: OK, 275 paths).
- [x] Every added route verified against its endpoint file:line — cited inline in yaml comments/descriptions and in this spec's tables above.
- [x] No route documented that doesn't exist — section C stale removals + zero duplicate-key check (`grep uniq -d` on all `^  /` path lines) + the automated `route_diff.py` reverse-scan (section E) all clean.
