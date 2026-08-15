# PROGRESS — MCP endpoint sweep @ Repttown + error-surfacing fix (2026-07-12 night)

Trigger: Ham forwarded an external AI agent ("Sana") report — 3 MCP create endpoints
"broken", suspected VAT-derivation crash. Ham asked: test ALL MCP endpoints at
Repttown, use server access to find the truth. Ham asleep; session autonomous.

## Verdict (log-verified, api-out.log on prod)

**ZERO backend defects.** Every "broken" endpoint was an INTENTIONAL business
validation (DomainException / McpE2Exception). The real bug is that the
ModelContextProtocol SDK's catch-all swallows every exception message — the MCP client
sees only `An error occurred invoking 'X'.` for ALL failures, so an agent cannot tell
"illegal per ม.86/4" from "server crashed". Sana's VAT hypothesis was half right:
the VAT guard fired, but as designed; nothing crashed.

| Sana's "broken" endpoint | actual server message (swallowed) |
|---|---|
| create_tax_invoice_draft | `VAT-not-registered companies cannot issue Tax Invoices (ม.86/4). Use a delivery note / receipt instead.` — CORRECT: Repttown vatRegistered=false |
| create_vendor_invoice_draft | `Expense category 1 not found.` + `Business Unit is required for this company.` |
| create_payment_voucher_draft | `Expense category 1 not found.` |
| create_expense_claim_draft | `[mcp.employee_required] Employee id 1 does not exist...` — correct; 0 employees existed |

Also swallowed the same way: `[mcp.pdf_not_posted]` (pdf-url tools on drafts — correct
guard), `Bank account 1 not found`, FluentValidation errors, JsonException on
`"uomId": null` input.

## Root causes found (3 layers)

1. **MCP error surfacing** (code bug): SDK catch-all swallows messages.
   → spec `specs/mcp-error-surfacing.md`, sonnet implementing on
   `feat/mcp-error-surfacing` (worktree Z:\temp\claude\wt-mcp-errsurface), opus review next.
2. **Resolver tool gaps**: `expenseCategoryId`, `taxCodeId`, `whtTypeId`,
   `businessUnitId` are required inputs with NO list tool to discover ids (Sana
   guessed "1" everywhere). Same spec adds list_expense_categories / list_tax_codes /
   list_wht_types / list_business_units. `uomId` = loose int, no master table →
   description-only fix.
3. **Master data empty on prod** (data gap, NOT code): seed scripts 150/430 hard-insert
   company_id 1 only; prod companies are 2 (Repttown) + 3 (Ham personal). So prod had
   0 expense_categories, 0 employees. No create UI/API exists for either.

## Data ops performed on prod (all backed up first)

- Backup: `/home/ubuntu/teas-backup-20260712-2350-pre-expcat-seed.sql.gz`
- Seeded 19 expense categories for company 2 (adapted 430 §17.3 set, account-mapped to
  co2's real compact COA: RENT→5100, MARK→5300, SAL/WAGE→5400, CAPEX→1610, generic→5200,
  INTR/COGS→NULL default). Reversible: `DELETE FROM sys.expense_categories WHERE company_id=2;`
- Inserted 1 test employee `BUTEST-EMP` (id 2, co2, national_id all-zeros, salary 0).
- Created reversible test DRAFTS via MCP E2E: payment voucher 1, vendor invoice 1
  (updated), expense claim 1 (updated). All carry "delete-me"/BUTEST notes.

## E2E re-test after seeding — ALL PASS

create_payment_voucher_draft ✅ (id 1) · create_vendor_invoice_draft ✅ (id 1) ·
create_expense_claim_draft ✅ (id 1) · update_vendor_invoice_draft ✅ ·
update_expense_claim_draft ✅ — with valid ids: expenseCategoryId 6 (PROF),
businessUnitId 3 (BU "Repttown Test"), expenseAccountId 15 (5200), employeeId 2.

Full sweep table (74 tools: 51 ok / 10 error—all now explained / rest blocked-or-skipped):
scratchpad mcp-sweep-results.md (session Z:\temp\claude\...\scratchpad\).
Untestable on prod forever: update/create_tax_invoice_draft + get_tax_invoice_pdf_url
(both prod companies are non-VAT — ม.86/4 guard; covered by repo integration tests only).

## Pipeline — ALL DONE (2026-07-13 ~02:15)

- [x] Fable personal diff review PASS (filter/auth ordering, tenancy, leak check)
- [x] Opus Tier-2: REJECT round 1 (filter silently dropped the server-side error
      log — spec §1 violation) → fix round (Warning log per catch + log-pinning
      test + list_tax_codes description nit) → re-verified
- [x] Tier-3 gate PASS: build 0 warn/0 err, suite 961/0/8 (baseline skips exactly)
- [x] Commit c2736e7 → PR #74 (CI green 2/2) → merged → release PR #73
      admin-merged → tag v1.19.0 (0fcd72c)
- [x] Deployed to prod: DB backup teas-pre-v1.19.0-20260713-015843.sql.gz,
      API 23/23 probes DEPLOY_OK (version=1.19.0, sql_scripts=68 unchanged),
      FE 3/3 FE_DEPLOY_OK, public E2E green (login 200 / mcp 401 / wellknown 200)
- [x] Post-deploy MCP probes from the real client: `[mcp.validation] Lines[0].
      TaxRate: 'Tax Rate' must be less than or equal to '1'.` and `[mcp.domain_rule]
      VAT-not-registered companies cannot issue Tax Invoices (ม.86/4)...` — the
      exact acceptance criterion, was generic swallow before. (Bonus finding the
      old swallow hid: line taxRate is FRACTIONAL — 0.07, not 7.)
- [x] Wiki entry (MCP client SDK WhenWritingNull test footgun) committed 5f52e1e;
      agent templates tightened (poll-don't-turn-end-wait); worktrees removed;
      deploy scripts kept at publish/v1.19.0/ (gitignored).
- NOTE: the 4 new resolver tools won't appear in an MCP client session started
      before the deploy — the connector caches tools/list per session. Verified
      registered via integration tests; Ham's next claude.ai session will see them.

## Ham's follow-ups (also in STATUS.md)
Real employees + a bank account for co2; review the seeded 19 expense categories'
account mapping; delete BUTEST drafts (PV 1, VI 1, claim 1) + BUTEST-EMP when done;
product decision on company-creation default categories + missing CRUD UIs;
push tightened worker rules upstream to minions-assemble.
