# PROGRESS — local stack + hard test run (2026-08-15 night)

Ham handed this off to run autonomously overnight: stand the local stack up, then test it hard
through Chrome. Prod is intentionally down (server migration pending), so localhost is the only
target. Findings are appended here as they are found, so a dead session loses nothing.

**Ham's rule for this run:** test data is created **through the UI only** — never a direct SQL
INSERT. psql stays read-only (probing/verifying/counting). Memory: `test-data-via-ui-only`.

## Phase 1 — local stack: ✅ UP

| Piece | State | Evidence |
|---|---|---|
| PostgreSQL 18 | running | `S:\Program Files\PostgreSQL\18`, listening :5432, `accounting_dev` |
| API | running :5080 | `Application started`; login 200; `/system/info` → `2.2.2-alpha.0.3` |
| FE | starting :3000 | `corepack pnpm dev` |
| Attachments | `D:\teas-attachments` | `U:\_attachments` from appsettings does NOT exist — overridden via `FileStorage__StorageRoot` env, config untouched |

### Boot command that works (env does not persist between shells — repeat it verbatim)
```
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
FileStorage__StorageRoot='D:\teas-attachments' Database__SeedDemoData=true \
dotnet run --project backend/src/Accounting.Api
```

### What had to be fixed to get here (both would have blocked a naive start)
1. **`accounting_dev` held 24 duplicate `(company_id, doc_no)` rows** — vendor_invoices 8,
   tax_invoices 5, payment_vouchers 11. Migration `20260814041822_H1_CompanyWideDocNoUniqueIndexes`
   creates UNIQUE indexes on exactly those pairs, and `DbInitializer` runs before `app.Run()`, so the
   first boot would have thrown 23505 and **restart-looped**. The dev DB is disposable (it is not
   prod), so: `pg_dump -Fc` → `D:\teas-backups\accounting_dev_pre_reset.dump` (397K), then
   DROP + CREATE. Bonus: this exercised the full migration+seed path on an empty database, which is
   exactly what the server migration will have to do.
2. **`SeedDemoData=false` in `appsettings.Development.json`** → 20 DEMO scripts skipped → **0
   companies, 0 users**, no way to log in. Skipped demo scripts are deliberately NOT recorded in
   `sys.applied_sql_scripts` (DbInitializer.cs:144), so re-booting with `Database__SeedDemoData=true`
   applied them. Now 101/101 scripts, 17 migrations, 15 `%company_id_doc_no%` indexes.

### Seeded fixture on accounting_dev
- Companies: **1** Demo Company (VAT) · **2** บริษัท แมนนวล เดโม จำกัด (VAT) · **3** ร้านนอนแวต เดโม (non-VAT).
  These are **not** prod's co2/co5/co7 numbering — do not carry prod facts over.
- Users: `admin` (SUPER_ADMIN), `approver`, `ap_clerk`, `sales_staff`, `demo-*` (co2), `nonvat-admin` (co3),
  and the full `rbac_*` role set (accountant, ap_clerk, approver, ar_clerk, auditor, chief_accountant,
  company_admin, …) — the RBAC users make the permission lenses testable.
- Password for the seeded users: `Admin@1234` (from `130_seed_admin_and_customer.sql`, BCrypt wf=12).
- ⚠️ The `accounting` role has **`rolbypassrls = t`** (not superuser, but RLS is bypassed anyway), so
  **RLS defects are invisible on this stack** unless a probe explicitly `SET ROLE`s to a
  non-bypassing role. Memory: `rls-masked-by-superuser-tests`.

## Phase 2 — hard test plan (Chrome, localhost:3000)
Scoped up front so it does not sprawl. Regression the shipped R3 fixes, then attack the known-weak areas.

- [ ] **H4 — attachment authorization.** Download *and* delete must authorize against the parent
      document, not `sys.attachment.read` (granted to every role). Include the brand-image route
      (company logo / stamp / signature) that a first cut nearly 403'd for everyone.
- [ ] **H1 — document numbering.** `/number-gaps` detection; numbering is per company, not per branch
      (web UI = branch 0, API key = real branch). DB-level: a duplicate must now be refused.
- [ ] **F1 — ภ.พ.36** surfaces foreign-service payments cleared by a manual journal entry.
- [ ] **Known-untested:** FE convert buttons that now 403 for SALES_STAFF · year=3000 nonsense-filing
      bound · the 500 family.
- [ ] **Year-close deadlock (H10): REPRODUCE ONLY.** It needs an Opus design first — fixing it live
      would bypass the routing ladder on a known footgun.
- [ ] General break-it: authz walking (ids across companies), validation edges, money invariants.

## Live verification of the fixes (2026-08-16, against the restarted local stack)
Both fixes were replayed against a running API, not just proven by tests. The API-key used for the WP-1
replay is **the same key from the original exploit**, unchanged.

**WP-1 — the exploit is closed.**

| Call | Before | After |
|---|---|---|
| `create_invoice_draft(deliveryOrderId)` with a key lacking `sales.tax_invoice.create` | `{"id":2,…}` — a draft Tax Invoice | **`[mcp.forbidden] 'sales.tax_invoice.create' required to create this document.`** |
| `create_tax_invoice_draft` (control) | denied | denied, unchanged |
| `sales.tax_invoices` row count | grew | **unchanged at 2** — the guard runs before the mint, it does not roll back after |

**WP-2 — every 500 is now a typed 422, and the loose year window is intact.**

| Call | Before | After |
|---|---|---|
| `/reports/pnd30?year=2026&month=13` and `&month=0` | 500 + .NET text | **422 `tax_filing.bad_period`** |
| `/reports/vat-register?year=2026&month=13` | 500 | **422 `tax_filing.bad_period`** |
| `/reports/pnd30?year=2026&month=-88` (the aliasing case) | would have returned **200 with December 2025's data** | **422** |
| `/tax-filings/cit/profile?year=` 9999 / 99999 / 0 / -1 | 500 | **422 `tax_filing.bad_year`** |
| `/tax-filings/cit/profile?year=3000` (trap 1) | 200 | **200 — unchanged, as required** |
| `/reports/pnd30?year=2026&month=8` (normal) | 200 | 200, same figures |

The `month=-88` row is worth calling out: it was **not** in my defect report. The implementer noticed
that `year*100 + month` is only a faithful yyyymm encoding for a month in 1..12, so an out-of-band month
can alias onto a *different valid* period — `2026 * 100 + (-88) = 202512` — and would have returned
December 2025's VAT figures under a 200. Reusing the shared guard without a round-trip check would have
converted a loud 500 into a silent wrong answer, which on a VAT report is worse. They added the
round-trip check and a test for it unprompted.

**F4's separate identity is confirmed.** `/reports/output-vat-register?year=2026&month=13` still returns
500, with detail `"Required parameter …"` — a *missing-parameter binding* failure, exactly as the
correction above predicted, and deliberately out of WP-2's scope. Called correctly, the same route
behaves: `?period=202608` → 200 with real rows, `?period=202613` → 422 `tax_filing.bad_period`.

**Gates:** full suite **1233 passed / 0 failed / 14 skipped** (Api.Tests) + **188/188** (Domain).
The skip count matches the recorded baseline exactly, so the run is not a `TEAS_TEST_PG` fake-green.

### F8 — 🔴 MONEY: converting a Sales Order to a Delivery Order loses the discount, the tax code, and the link back to the order
**Severity: high (overbilling + a dead safety guard + a VAT-rate error). Found by the UX swarm, root cause traced and confirmed in the database by Fable.**

**What was observed.** Sales order `08-2026-SO-0003` line 2 was 2 × ฿1,250.00 less **15%** = ฿2,125.00.
Clicking สร้างใบส่งของ produced delivery order `08-2026-DO-0003` whose line 2 reads ฿2,500.00 — the
discount is simply gone. Confirmed straight from the tables, not from the screen:

| | qty | unit price | discount_percent | line_amount | tax_amount |
|---|---|---|---|---|---|
| `sales_order_lines` line 2 | 2 | 1,250.00 | **15.00** | **2,125.00** | 148.75 |
| `delivery_order_lines` line 2 | 2 | 1,250.00 | **0.00** | **2,500.00** | 175.00 |

The document total is overstated by ฿401.25 (฿375.00 of net plus ฿26.25 of VAT that is not owed), and a
delivery order cannot be edited once issued, so the chain is permanently inconsistent.

**Two further consequences the swarm did not see, found by reading the rows.**

- `delivery_order_lines.sales_order_line_id` is **NULL on every line** (checked on DO-0002 and DO-0003),
  so `sales_order_lines.delivered_quantity` stays **0.0000** even after a full delivery. The
  over-delivery guard in `SalesOrderDeliveryServices.cs:226-233` is inside
  `if (l.SalesOrderLineId is { } solId)`, so it **never executes**: a user can raise unlimited delivery
  orders against one sales order, each for the full quantity, and `do.over_delivered` can never fire.
- The tax code is hardcoded to `VAT7`. `SalesLineBackstop.Resolve` deliberately ignores the requested
  *rate* and derives it from the *code* — so overwriting the code overwrites the rate. A line the user
  entered as **"0% (ยกเว้น/ส่งออก)"**, which the line editor offers, comes back charged at 7% on the
  delivery order.

**Root cause — the API shape, not a careless frontend.** `createDelivery()` in
`frontend/app/(dashboard)/sales-orders/[id]/page.tsx:56-77` sends `salesOrderLineId: null`,
`discountPercent: 0`, `taxCodeId: 1`, `taxCode: vatMode ? 'VAT7' : 'VAT0'`. That is not laziness: the
sales-order detail endpoint hands the frontend a `ChainLineDto` (`frontend/lib/types.ts:1061-1065`)
containing only `lineNo, productId, productCode, descriptionTh, quantity, uomText, unitPrice, lineAmount,
taxAmount, totalAmount`. There is **no `lineId`, no `discountPercent`, no `taxCode`** in it — the
frontend hardcodes those three values because it is never told them. The receiving service is correct:
`CreateDeliveryOrderAsync` honours `DiscountPercent`, links `SalesOrderLineId`, and enforces the
over-delivery guard whenever the request actually carries them.

**Why this needs a design, not a patch.** `ChainLineDto` is the shared line shape across the document
chain, so widening it touches every mapper that produces one and every consumer that reads one, and the
same "thin DTO → frontend invents values on convert" pattern may exist on the other conversion paths
(quotation→SO, DO→TI, billing note→TI, PO→VI). Fixing only the sales-order screen would leave siblings
broken. Routing: Opus design first.

### F9 — the payment-voucher preview overstates what will leave the bank, by exactly the withholding tax
**Severity: medium (display only — the saved document and the ledger are correct). One line, one file.**

Creating a payment voucher for a service invoice with 3% withholding (10,000.00 + 700.00 VAT, WHT 300.00),
the right-hand **LIVE PREVIEW** — the panel styled to look like the printed voucher — showed
**Grand Total ฿11,000.00** and **จ่ายสุทธิ ฿10,700.00**. The form's own totals box, on the same screen a few
centimetres away, showed the correct ฿10,700.00 and ฿10,400.00. Two panels disagreeing by exactly the WHT
amount, with the more official-looking one wrong: an accountant who reads the preview before approving a
transfer believes ฿10,700 is leaving the bank when ฿10,400 will.

**The renderer is right and the caller is wrong.** `PaperFoot.tsx:34-39` documents the contract, and it is
the opposite of what the page assumes: when `summary.wht` is set, **`summary.total` is the NET**
(จ่ายสุทธิ), and Grand is derived as `total + wht`. The comment even records that this was inverted once
before and that `PaperFootPlan.cs` is the single source of truth, pinned by `PaperFoot.test.ts` against a
shared backend fixture. `payment-vouchers/new/page.tsx:319` passes `total: subtotal + vat` — the *grand*
total — so the renderer faithfully computes Grand = 10,700 + 300 = 11,000 and Net = 10,700.

**The correct value is already sitting on the same page.** Line 187 computes
`const net = selfWithhold ? subtotal + vat : subtotal + vat - wht`, which is exactly the net the contract
wants in all three cases (no WHT, normal WHT, self-withhold — where `wht` is passed as null and the vendor
is paid in full). Passing `net` satisfies the contract without any new arithmetic.

**Scope is one file.** Of the ten screens that hand a `summary` to the paper renderer, the payment-voucher
create page is the only one that passes a `wht` alongside a `total`, so no sibling shares the defect. The
receipt create page passes no `wht` at all (`receipts/new/page.tsx:382`), which means a receipt with
withholding simply omits the WHT row rather than showing a wrong number — a lesser, separate gap worth
noting but not this defect.

### F10 — a 50 ทวิ certificate is issued with an all-zero payer tax ID and no warning
The WHT certificate auto-generated from the payment voucher (`08-2026-WT-0001`) computes correctly —
แบบ ภ.ง.ด.53, income type 8 ค่าบริการ, ฿10,000.00 × 3.00% = ฿300.00, and it correctly picks the
juristic-person form because the vendor is นิติบุคคล. But the ผู้หักภาษี block prints Demo Company's own
tax ID as **`0-0000-00000-00-0 · สาขา 00000`**, because the company profile has no tax ID configured, and
the document is marked บันทึกแล้ว with no warning.

A 50 ทวิ with an all-zeros payer identity cannot substantiate the vendor's withholding credit, so the
system silently produces a document that fails its legal purpose. The empty profile is demo data, not a
defect; the defect is that issuance proceeds silently. This is the same shape as the v2.0.0 WP-3/WP-5
guards, which refuse to produce a filing when the identity behind it is unusable — that precedent argues
this should refuse or at least warn rather than print zeros.

### Two more UX findings from the swarm, lower severity
- **Validation errors hide the real reason.** Saving a customer with a tax ID that fails its checksum
  shows only a red `เกิดข้อผิดพลาด` toast with no field highlighted, while the API returned the precise
  `{"field":"taxId","messages":["Invalid Thai Tax ID (13 digits + checksum)."]}`. On the vendor form the
  first submit shows only `เลขผู้เสียภาษีต้องมี 13 หลัก` — *must have 13 digits* — on an input that
  plainly has 13 digits; the real checksum message appears only on a second submit. An accountant copying
  a tax ID off a business card is told to recount digits when the digits are fine.
- **A tax note is injected into an issued document the preparer never saw.** Quotation `08-2026-QT-0001`
  printed `หมายเหตุ: ลูกค้านิติบุคคลหัก ณ ที่จ่าย 3% เฉพาะส่วนบริการ` although the create form's หมายเหตุ box was
  left empty. Wording with tax consequences goes out under the company's name without the preparer being
  able to see or edit it while drafting, and the guidance does not apply to every transaction.

## Ledger audit — the books tie out
An independent auditor pulled the actual journal entries for company 1 and reconciled them against the
documents the swarm created through the UI. **Verdict: the books tie out.** This is the result Ham asked
for, so the evidence matters as much as the verdict:

- **All 8 posted journal entries balance**, and each header's `total_debit`/`total_credit` equals the sum
  of its own lines. A header disagreeing with its lines would have been severe even if both sides balanced;
  none did.
- **Trial balance 32,724.12 debit = 32,724.12 credit**, and the API's figure reconciles to an independent
  SQL sum of `gl.journal_lines` to the satang.
- **Subledgers reconcile to their control accounts with difference 0.0000** on both sides — AR
  1,783.34 and AP 0.00, cross-checked against the control-account balances computed from the ledger.
- **VAT rounds correctly on both deliberately fractional cases**: 999.99 × 7% = 69.9993 → stored 70.00,
  and 333.33 × 7% = 23.3331 → stored 23.33. The output-VAT register total (335.42) equals account 2151's
  balance exactly; input VAT (770.00) equals account 1170's.
- **Nothing posts twice.** This system issues both a ใบแจ้งหนี้ and a ใบกำกับภาษี for the same sale, so a
  double count was the obvious risk. `billing_notes.journal_entry_id` is NULL for both invoices and no
  journal references an `IV-*`, `DO-*`, `SO-*` or `QT-*` number — revenue and AR are recognised exactly
  once, by the tax invoice.
- **The credit note behaves correctly against an already-settled invoice.** Posted after the receipt had
  cleared AR, it drives that customer's balance to **−356.66**, a refund owed — not a silent zeroing and
  not an error. The AR aging report and the customer statement both show it and still reconcile.
- **The WHT payable account holds exactly 300.00**, matching the certificate, on a base of the pre-VAT
  10,000.00 rather than 10,700.00.
- **The F8 discount error never reached the books.** Not merely absent — *structurally impossible*:
  `sales.delivery_orders` has no `journal_entry_id` column at all, and no journal entry references
  `08-2026-DO-0003`. The overstatement is contained to the document layer.

### F11 — the tax invoice's header discount field stays zero while its line carries the discount
`sales.tax_invoices` for TI-0002 stores `subtotal_amount = 3,124.99` (already net of the line discount),
`discount_amount = 0.0000`, `taxable_amount = 3,124.99` — while line 2 carries `discount_percent = 15.00`
and `discount_amount = 375.00`. Every downstream total is correct, so this is not an arithmetic error;
the risk is that anything reading the header rollup (a printed document, an export, a report, a future
integration) reports a discount of zero on a document that gave one. Worth deciding deliberately: either
populate the rollup or document that the header field is unused.

### F12 — the profit-and-loss endpoint returns zeros by default, disagreeing with every shipped caller
`GET /reports/profit-loss?from=2026-08-01&to=2026-08-31` returns `revenue 0, expense 0, netProfit 0` for a
period whose trial balance shows 32,724.12 of movement, because journal lines carry no business unit and
the endpoint excludes untagged activity unless `includeUnspecified=true`.

Severity is limited by who actually calls it: the P&L screen defaults its toggle to **true**
(`reports/profit-loss/page.tsx:21`), and the MCP tool defaults to **true**
(`TeasMcpTools.cs:1077`, whose own description says the report "covers ALL revenue/expense unless you
explicitly exclude untagged docs"). So no shipped consumer hits the trap — but the raw endpoint's default
contradicts both of them, and any new integration written against the documented API gets a report showing
no profit and no loss on a company that traded. The cheap fix is to flip the endpoint default to match its
two consumers.

## Known limitation of this environment — RLS is NOT exercised locally
Both Postgres roles on this server (`postgres` and `accounting`) carry **`rolbypassrls = t`**, so every
row-level-security policy is skipped for the application's own connection. The policies exist and look
right — `sales.tax_invoices` carries `company_isolation` as
`company_id = NULLIF(current_setting('app.company_id', true),'')::integer` — but nothing here proves they
work, because nothing here runs under them.

The cross-company 404s recorded below are therefore evidence of **application-level** tenant scoping, not
of RLS. Production runs a NOBYPASSRLS role (that is why seeds have historically failed there with 42501),
so an RLS regression would be invisible in this run and visible only after deploy. Worth folding into the
server-migration plan: give the new box a non-bypassing app role and re-run this pass against it.
Related memory: `rls-masked-by-superuser-tests`.

## Findings

### F1 — a fresh install that enables demo data on a *later* boot gets tenants with no roles at all
**Severity: medium (dev/demo path; prod's own tenant-creation path is unaffected).**

Reproduced twice, then fixed by re-seeding in the right order — so the mechanism is confirmed, not inferred.

`510_per_company_roles_reconcile.sql` materialises the per-company role catalogue by looping over
`master.companies` **once**, at the moment the script runs (lines 109-116), and it is tracked in
`sys.applied_sql_scripts` like every other script, so it never runs again. The demo companies are
created by DEMO scripts (120 co1, 400 co2, 440 co3) that are skipped entirely while
`Database:SeedDemoData=false`. So booting once without demo data and then flipping the flag on
produces this state:

- `sys.roles` holds exactly one row — the system-global `SUPER_ADMIN`. No per-company roles for co1/co2/co3.
- `sys.user_roles` holds 6 rows: only the four super-admins. The 20 seeded `rbac_*` role users, plus
  `ap_clerk` and `sales_staff`, have **no role at all**.
- Those users cannot log in: `POST /auth/login` returns **401 `auth.no_company_assignment`**. Only
  super-admins can get in, and RBAC is untestable because no non-super-admin can authenticate.

Booting once against an empty database with `SeedDemoData=true` produces the correct state (11 roles per
company, 33 `user_roles`, `rbac_accountant` logs in 200), because 510 then runs after the demo companies
exist. Real tenants are safe either way: `CompanyService.CreateAsync` calls `sys.seed_company_roles`
directly (510 line 82), so a company created through the app always gets its roles.

**Why it matters beyond the demo seed:** the fragility is that 510 is a one-shot fan-out whose
correctness depends on every company already existing when it runs. Anything that creates a company by
raw SQL after 510 has been recorded — a future seed script, a restored partial dump, a data import for
the server migration — silently produces a tenant whose users cannot log in. The same shape as the
already-known `seed-cos-bypass-createasync-taxcodes` footgun.

**Suggested fix (not applied — needs a design decision):** make the reconcile self-healing rather than
one-shot; e.g. have the DEMO company scripts call `sys.seed_company_roles(<id>)` themselves right after
inserting the company, which is idempotent by construction and keeps the fix local to the seeds.

### F5 — 🔴 SECURITY: an API key can mint a Tax Invoice it has no scope for (MCP `create_invoice_draft`)
**Severity: high. Exploited live on the local stack — this is a demonstration, not a code reading.**

An API key is the credential you hand to an external agent precisely so you can bound what it may do.
This tool lets a key step outside its bound.

**What was proven.** Through the UI I created an API key on co1 (VAT-registered) with exactly three
scopes — `sales.billing_note.manage`, `sales.sales_order.manage`, `sales.billing_note.read` — and
deliberately **not** `sales.tax_invoice.create`. Then, over MCP with only that key:

- Control: `create_tax_invoice_draft` → **denied**, `"Access forbidden: This tool requires authorization."` — correct.
- Exploit: `create_invoice_draft(deliveryOrderId: 1)` → **succeeded**, returning
  `{"id":2,"approvalUrl":"http://localhost:3000/tax-invoices/2?action=approve"}`.
- Database confirms it: `sales.tax_invoices` now holds row **id=2, status DRAFT, ฿5,350.00**, and
  `sales.billing_notes` is still **empty** — so the key produced the document type it was refused, not
  the one it was granted.

**Mechanism.** `TeasMcpTools.cs:693` gates the tool with `[Authorize(Policy = BillingNoteManage)]` only,
then branches on company VAT mode (`:710-719`): a VAT-registered company routes into
`tiSvc.CreateFromDeliveryOrderAsync` / `CreateFromSalesOrderAsync`, which mint a **Tax Invoice**.
`TaxInvoiceService` performs no permission check of its own — only `IsAuthenticated` and
`EnsureVatRegisteredAsync`. The sibling tool `create_tax_invoice_draft` correctly requires
`TaxInvoiceCreate`, which is what makes the inconsistency unambiguous rather than a judgement call.

**Why the api-key surface makes this worse than the HTTP twin.** `PermissionHandler`
(`Authorization/PermissionRequirement.cs:17-29`) authorizes an API key against the key's **own scopes
CSV**, not against any user's roles — a key never gets the super-admin bypass. So the escalation does
not depend on which user created the key or what that user may do: the scope list on the key *is* the
security boundary, and this tool crosses it. The equivalent HTTP routes were tightened in `91e5147`
("converting a document now requires permission to create the target, not just read the source"); the
MCP surface never inherited that fix. It is mirror drift, not a deliberate difference.

**Impact is worse than "just a draft" — the whole chain was verified live.** The draft carries no
document number, and the key cannot post it, so no number is burned from the legal series. But the draft
cannot be removed either, and it blocks the accounting period:

1. There is **no DELETE route for tax invoices anywhere in the API** — the OpenAPI document lists only
   `post`, `get`, `/post`, `/pdf`, `/paper`, `/xml`, `/resend`, `/mark-printed`, `/activity`. A live
   `DELETE /tax-invoices/1` returns **405**.
2. The UI offers no delete either: on `/tax-invoices/2` the only action is **บันทึก (Post)**, and the
   list row shows just "ดูรายละเอียด". The app does label it — the draft renders as
   **"รออนุมัติ (agent)"** — but labelling it is not removing it.
3. Closing the month then fails: `POST /periods/2026/8/close` → **422 `period.draft_present`**,
   *"Cannot close period — draft fiscal documents still exist. Post or void them first."*
4. And month close is a precondition for the year: `POST /periods/2026/close-year` → **422
   `year.periods_not_closed`**.

So a key that was never granted tax-invoice rights can plant an un-deletable object whose only exit is
to **post it** — turning the unauthorised agent action into a real, numbered tax document — or to leave
the company unable to close that period. Neither is an acceptable choice to hand an accountant.

**Fix shape:** the tool needs a runtime target-permission check on the VAT branch, mirroring
`SalesChainEndpoints.cs:114-127` — a static attribute cannot express it, because which document gets
minted depends on the company's VAT mode. Do NOT simply add `TaxInvoiceCreate` to the attribute: that
would break the non-VAT branch, where the tool legitimately mints a billing note.

**Fixed and reviewed (2026-08-16).** Implemented per `specs/fix-local-hard-test-findings.md` WP-1 and
reviewed by a fresh Opus reviewer at Tier 2: **APPROVE-WITH-NITS**, no finding with a constructible
failure scenario. Two things the review established that are worth keeping:

- The mirror the spec asked for would have been **wrong**, and the implementer was right to deviate.
  `SalesChainEndpoints` resolves grants through `IPermissionLookup.LoadAsync(tenant.UserId, …)`, but
  `AmbientTenantContext.UserId` (`:59-61`) reads only `NameIdentifier`/`sub`, and
  `ApiKeyAuthentication.cs:49-64` emits neither — so for every API key it is null, and a literal port
  would have queried user 0 and denied *every* key, including properly scoped ones. Re-running the
  tool's own `mcpperm:` policy through `IAuthorizationService` lands on the same `PermissionHandler`
  that the static `[Authorize]` attributes use, which reads the key's scopes CSV. Correct for both
  principal kinds.
- The fix also covers the **OAuth Bearer** transport, not just `X-Api-Key`: `McpPrincipalFactory.cs:44`
  puts the same scopes CSV on a Bearer principal, and `McpBearerClaimsTransform.cs:29-38` rejects any
  Bearer principal that is not an api-key principal, so both land in the same branch.

**Two follow-ups the review surfaced, neither blocking:**

- **Release note (N2):** this is a behaviour change for keys already in the field. Any existing MCP key
  on a VAT-registered company scoped `sales.billing_note.manage` without `sales.tax_invoice.create` now
  gets `[mcp.forbidden]` on a call that previously returned a draft. That is the point of the fix, but
  those keys need re-scoping and the release note must say so.
- **Backlog (N3):** `create_receipt_draft` (`TeasMcpTools.cs:509-521`) reads a tax invoice's status and
  amounts in settlement mode while gated only on `sales.receipt.create`, with no `sales.tax_invoice.read`.
  A *read* under a neighbouring scope, not a mint under the wrong one, and the document it creates always
  matches its policy — pre-existing, lesser, worth a look but not this release.

**Cleanup state:** the draft Tax Invoice id=2 was left in place on co1 as evidence.

**Why the test suite could never have caught this.** While fixing it, the implementer found that the
existing test `Mcp_create_invoice_draft_is_polymorphic_and_wraps_the_delivery_required_guard` minted its
VAT-company key with `billing_note.manage` / `sales_order.manage` / `delivery_order.manage` and **not**
`tax_invoice.create`, then asserted the call succeeds and returns a `tax-invoices` link. The suite was
therefore *asserting the vulnerable behaviour as correct* — a green run was evidence the hole was open,
not closed. The fix required editing that test to add the missing scope so it keeps exercising the guard
it actually targets. This is the kind of defect a test suite structurally cannot report, and it is the
argument for exactly this sort of live adversarial pass against a running system.

### F2 — the VAT report endpoints return a raw 500 (and leak the .NET exception text) for month 13 or 0
**Severity: medium. Same defect class v2.0.0's WP-6 fixed for ภ.ง.ด.50/51 — this service was missed.**

Verified live against localhost as an authenticated user:

| Request | Result |
|---|---|
| `GET /reports/pnd30?year=2026&month=13` | **500** `internal_error` — `"Year, Month, and Day parameters describe an un-representable DateTime."` |
| `GET /reports/pnd30?year=2026&month=0` | **500**, same message |
| `GET /reports/vat-register?year=2026&month=13` | **500** |
| `GET /reports/output-vat-register?year=2026&month=13` | **500 — but for a different reason, see the correction below** |
| `GET /reports/input-vat-register?year=2026&month=13` | **500 — same** |

> **Correction (2026-08-16), found while implementing the fix.** The last two rows do not belong to this
> defect. `input-vat-register` and `output-vat-register` bind a single required `[FromQuery] int period`
> (yyyymm), not `year`/`month` — `TaxFilingEndpoints.cs:273-281` — so my probe omitted a required
> parameter and the 500 I recorded was ASP.NET's *missing-parameter* failure, not the `DateOnly` crash.
> `OutputVatRegisterAsync` has in fact validated its period since commit `ce1f6fe`
> (`TaxFilingService.cs:177` calls `TaxFilingPeriod.MonthRange`), and `InputVatRegisterAsync` builds no
> `DateOnly` at all. The genuine instances of *this* defect are `/reports/pnd30` and
> `/reports/vat-register`, which do take `year`/`month` (`ReportEndpoints.cs:33-42`).
>
> The missing-parameter 500 is real but is **F4's class** — a binding failure surfacing as 500 instead of
> 400 — and is recorded there, not here. Verified live after the fix; see the verification section.

**Root cause.** There are two `MonthRange` helpers and only one of them validates:

- `TaxFilingPeriod.MonthRange(int period)` — `ProportionalInputVatService.cs:40-48` — checks
  `m is < 1 or > 12 || y < 2000 || y > 9999` and throws the typed
  `DomainException("tax_filing.bad_period")`, which the pipeline maps to **422**.
- `VatReportService.MonthRange(int year, int month)` — `Reports/VatReportService.cs:91-93` — is a
  two-line expression that constructs `new DateOnly(year, month, DateTime.DaysInMonth(year, month))`
  with **no validation at all**, so an out-of-range month raises `ArgumentOutOfRangeException`, which
  nothing maps, and the generic handler returns 500 with the framework's own message.

The irony is documented in the repo itself: the comment at `ProportionalInputVatService.cs:59` describes
WP-6 fixing exactly this shape ("pnd50/pnd51 threw an unmapped `ArgumentOutOfRangeException` (500) for a
nonsense CE year"). `VatReportService` has the same shape and was not swept.

**Reachability.** The web UI picks the month from a dropdown, so a normal user cannot send 13 — the
exposure is the API-key / MCP surface and any direct caller. Two things are still wrong regardless of
who can reach it: the error contract says a bad request is a typed 4xx, and the response body hands the
caller internal implementation detail.

**Fix shape:** reuse the existing typed guard rather than writing a new one — validate before building
the `DateOnly`, throw the same `DomainException` family the sibling helper already throws.

### F3 — year=3000 is accepted, and that is deliberate (verified, NOT a new defect)
The work queue lists "year=3000 nonsense-filing bound" as open. Live behaviour: `pnd50/preview?year=3000`
→ **200** with `periodStart 3000-01-01`; 9999, 0 and -1 → **422 `tax_filing.bad_year`**; 1990 → 422. The
accepted window is `2000 ≤ year < 9999` (`ProportionalInputVatService.cs:74-79`).

That looseness is **intentional and documented in the code**: the comment above `EnsureYear` says the
ceiling is kept high because tests use years up to ~7499 as "definitely fake" sentinels to dodge
collisions on the shared, never-reset `teas_test` database, and the floor is kept low so a legitimate
late filing for an old year is never refused (the R1 lesson that a guard which turns a wrong answer into
an impossible one has broken a real capability).

So tightening this bound is not a one-line change: it invalidates the sentinel years those tests rely on.
Whoever picks the item up needs to re-home the sentinels first, or the bound will keep being reverted.

### F6 — the convert buttons are rendered without checking the permission the backend now demands
**Severity: low-medium (UX dead end, not a security hole — the backend refuses correctly).**

The backend half is right, verified live as `rbac_sales_staff` (holds delivery-order / sales-order /
billing-note manage, but not `sales.tax_invoice.create`):

- `POST /delivery-orders/1/create-ti` → **403**
- `POST /sales-orders/1/create-invoice` → **403** with the precise detail
  `"'sales.tax_invoice.create' required to create this document."`
- and the same user reads `GET /delivery-orders/1` fine (**200**), so this is purely about the write.

The frontend half never asks. I checked the four detail pages myself: `sales-orders/[id]/page.tsx`,
`delivery-orders/[id]/page.tsx` and `invoices/[id]/page.tsx` contain **no `hasScope` call at all**, and
`quotations/[id]/page.tsx` imports `useHasScope` (line 42) and uses it for the *send* button (line 109)
but not for `q-convert`. Every convert button is gated on document status alone. A user without the
target-create grant is therefore offered a button whose only possible outcome is an error toast.

**FIXED (2026-08-16) — Ham chose disabled-with-a-reason over hiding.** All five buttons now render,
disabled, wrapped in a tooltip naming the permission the backend will demand. Verified in the browser on
the running stack, in both directions:

- As `rbac_sales_staff` on an Issued invoice, `bn-create-ti` renders **disabled** with the tooltip
  *"ต้องมีสิทธิ์ sales.tax_invoice.create จึงจะทำรายการนี้ได้ — กรุณาติดต่อผู้ดูแลระบบ"*, while the
  neighbouring ยกเลิก button stays live. Screenshot captured.
- The same user's `do-create-invoice` — a permission they **do** hold — renders enabled with no tooltip,
  so the gate does not over-block.
- As the super-admin `admin`, `bn-create-ti` renders enabled with no tooltip, matching the backend's
  super-admin bypass.

This deliberately diverges from the "hide, not disable" convention in `PermissionGate.tsx:6-8`. The
reasoning is recorded in that file: hiding was security-by-absence, which was never real security, and
since `91e5147` the backend hard-403s — so the only thing hiding achieved was teaching the user nothing.
Every other call site keeps the hide behaviour.

Two implementation traps worth remembering, both confirmed live rather than assumed: a `disabled` button
gets `pointer-events: none` in Chromium and never fires hover, so the tooltip must live on a wrapping
element; and `useHasScope` collapses "still loading" and "denied" into the same `false`, which would
flash a false "you lack permission" on every page load — hence the new `useScopeState` companion hook
returning `{allowed, pending}`.

**Honest limit on what I originally reproduced.** I could not make the button *appear* for a user who would then be
refused, because document state hid it in both fixtures: `do-create-ti` additionally requires
`status === 'Delivered' && !isCombinedWithTi && taxInvoiceId == null`, and my delivery order came back
`isCombinedWithTi: true`; `so-create-invoice` only renders for a service-only sales order, and co1 seeds
no service products, so every free-text line is treated as goods. So the dead end is **latent and
code-confirmed rather than screen-confirmed** — it needs a delivery order that is not combined with a
tax invoice, or a service-only sales order, to become visible. I am reporting it at that strength
deliberately.

### F4 — an out-of-int year returns 500 from model binding
`GET /tax-filings/pnd50/preview?year=99999999999` → **500** `"Failed to bind parameter \"int year\""`.
Same contract complaint as F2 (a malformed query parameter is a 400, not a 500), lower severity: it is
framework-level binding rather than app code, and no query in range can trigger it.

### F7 — the year-close "deadlock" has an exit, and it works
The work queue calls H10 the next big item and describes three rules locking each other: close needs
depreciation, depreciation needs the period open, reopen needs it closed. Probed live on co1, the first
two hold and **the third does not** — monthly reopen is built and works:

| Step | Result |
|---|---|
| `POST /periods/2026/close-year` with months open | 422 `year.periods_not_closed`, listing every open month |
| `POST /periods/2026/7/close` (empty month) | **200** |
| `POST /depreciation-runs {2026,7}` into the now-closed month | 422 `period.closed` — *"Reopen the period or correct doc_date."* |
| `POST /periods/2026/7/reopen` | **204 — the exit exists** |
| `GET /periods/2026/7/status` after reopen | `{"open":true}` |
| `POST /periods/2026/8/close` (month holding a draft) | 422 `period.draft_present` |

So the interlock is real but not a true deadlock on a healthy tenant: close the month, discover
depreciation was missed, reopen, run it, close again. `troubles-wiki.md` already records that O14 added
this route, and this run confirms it end to end.

**What that means for the H10 design:** do not design an escape hatch that already exists. The open
question is why co5's FY2026 is stuck *specifically* — the wiki's neighbouring note about a company that
"cannot be year-closed on corrupt data" points at data state, not a missing route. The designer should
start by asking which of these four refusals co5 actually hits, because the remedy for
`period.draft_present` (an undeletable draft — see F5) is completely different from the remedy for a
depreciation gap.

## Verified working (negative results worth keeping)
Things that were attacked and held. These cost as much to establish as the findings and stop a future
session re-investigating them.

- **Cross-company isolation.** A co3 user requesting co1's tax invoice gets **404**, not 403 — it does
  not even leak that the id exists. Listing co1 attachments as a co3 user returns `{"items":[]}`.
- **H4 attachment authorisation (v2.1.0) holds.** For the attachment on delivery order 2:
  admin 200 · `rbac_sales_staff` 200 (holds `sales.delivery_order.manage`) · **`rbac_auditor` 403**
  (does not) · `rbac_ar_clerk` 200 · both co3 users **404**. The parent-permission inheritance is doing
  exactly what it was written to do, and a non-existent attachment id returns 404 rather than an error.
- **Posted documents are immutable.** `DELETE` and `PUT` on a posted tax invoice both return **405** —
  the routes do not exist at all, which is stronger than a runtime check.
- **H1 numbering (v2.1.0/v2.2.0) is structurally sound.** `NumberSequenceService.cs:63` binds
  `branch_id` to the literal `0` on every allocation ("0 now means company-wide"), so the login channel
  can no longer fork the counter; the seven `(company_id, doc_no)` unique indexes are present in the
  fresh database (15 matching index rows). Documents created by two different users in sequence numbered
  `SO-0001` then `SO-0002` with no collision, and `/reports/number-gaps` reports
  `hasGaps:false, hasDuplicates:false` on clean data.
- **The money path is correct end to end.** A tax invoice posted through the UI for 2 × ฿1,000 produced
  subtotal ฿2,000 / VAT ฿140 / total ฿2,140, and behind it journal entry `08-2026-JV-0001`:
  Dr 1130 ลูกหนี้การค้า ฿2,140 · Cr 4000 รายได้จากการขาย ฿2,000 · Cr 2151 ภาษีขายค้างจ่าย ฿140 —
  balanced, `total_debit = total_credit`, header and lines agreeing.
