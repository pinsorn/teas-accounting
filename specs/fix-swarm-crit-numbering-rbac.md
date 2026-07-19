# Fix: swarm CRIT findings — doc-number drift (CRIT-1) + TAX_OFFICER filing grant (CRIT-2)

Source: 10-role UX swarm on co5 (commit 26768c4, swarm-findings/*.md). Ham: "ร่างวิธีการแก้ไขมาเลย".
Design authored by Fable (footgun/money-path per CLAUDE.md §Routing #4 — Fable owns design + review +
ef/gate/commit; a worker types code FROM this spec). Two independent CRITs, shippable together or split.

═══════════════════════════════════════════════════════════════════════════════════════════
## CRIT-1 — document numbering collides under concurrency (23505 duplicate key)

### Confirmed root cause (evidence, not theory)
- `NumberSequenceService.NextAsync` is ALREADY concurrency-safe: single-statement
  `INSERT … ON CONFLICT (company,branch,prefix,sub,year,month) DO UPDATE SET current_value+1
  RETURNING` — Postgres serializes concurrent callers on the row lock; two callers can NEVER read
  the same value. So the bug is NOT a missing lock.
- The 23505s (`ix_journal_entries_company_id_doc_no`, `ix_purchase_orders_company_id_doc_no`) are
  **sequence drift**: `sys.number_sequences.current_value` for a bucket sits BELOW `MAX(doc_no)`
  already present in the target table, so the atomic counter hands out an already-used number.
- Prod evidence (co5, 2026-07-19): after the swarm healed, JV seq=12 == 12 contiguous JV rows;
  PO seq=2 == max PO-0002. During the swarm the counters lagged and every allocation ≤ existing max
  threw 23505. Stack = plain `NpgsqlExecutionStrategy` (no EF retry) failing at the doc's own
  SaveChanges — not a retry-with-stale-number bug.
- **Why it self-heals + why single-user hides it:** PO/JE allocation runs with NO ambient
  transaction (PurchaseOrderService H8 comment), so the UPSERT auto-commits the +1 EVEN when the
  following document SaveChanges fails. Each failed attempt still walks current_value up by 1, so
  after enough attempts it passes max and the next succeeds. One user retrying limps through; 10
  users concurrently turn the lag window into a burst of visible 500s. (sales01 saw QT-send fail
  4/4 while the counter was still climbing.)
- Drift ORIGIN (how current_value fell behind max in the first place — the thing to also close):
  doc_no rows that entered the tables without advancing the matching sequence bucket. Candidates in
  this repo's history — raw-SQL company/demo seeds (memory: seed-cos-bypass-createasync), the
  migration-squash + teas_test resets (memory: migration-squash-teas-test-reset), and any
  pre-NumberSequenceService historical rows (co5's stray 07-2026-PO-0001). Not yet code-audited;
  see task 1a.

### Fix — three layers (all required; smallest diff that closes it for good)

**[x] 1a. AUDIT the drift sources (do FIRST, informs 1c).** Grep every write to a `doc_no` column that
does NOT go through `INumberSequenceService.NextAsync` (seeders, importers, year-end closing JE,
reversal JE, payroll settlement JE, statement-import matched docs). Each one that assigns a doc_no
directly is a drift source. Record the list in this spec. If any is a live code path (not just a
one-off seed), route it through NextAsync or make it bump the sequence in the same tx.

**[x] 1b. RECONCILE current prod + a guard for future drift (SQL, new SqlScript — DB backup SOP).**
One idempotent reconcile that lifts every sequence bucket to the true max across all doc tables:
```
-- for each (company,branch,prefix,period) set current_value = GREATEST(current_value, max_doc_seq)
-- where max_doc_seq = the numeric tail of MAX(doc_no) across the doc table that owns that prefix.
```
Runs at API startup like other SqlScripts (idempotent; safe to re-run). This heals co5 AND any other
tenant silently carrying drift right now. NOTE: this is the money/footgun bit — Fable writes/reviews
this SQL personally, runs it against a teas_test copy first, and it runs under the NOBYPASSRLS app
role (memory: v1.22.0 died on 625 running as superuser — pin per-company or make it RLS-safe).

**[x] 1c. RETRY GUARD in the post/approve services (the durable belt-and-braces).** Even with 1a+1b, a
brand-new bucket's first allocations can still race a manual/seeded insert. Wrap the number-allocate
+ document-SaveChanges of each POST/approve in a bounded retry (e.g. 5 attempts): on a 23505 against
a doc_no unique index, re-call NextAsync (the counter has already advanced past the collision) and
retry. This neutralises BOTH residual drift and any genuine race, converts the 500 into a
transparent success, and is the shortest change that makes the guarantee hold without redesigning the
sequence. Put it in ONE shared helper (`INumberSequenceService` extension or a small
`NumberedDocumentWriter`) and call it from Quotation send, TI post, RC post, VI post, PV post,
PO approve, CN/DN post, expense post, payroll post — every allocation site. Cap the retry; on
exhaustion surface a clean domain error, not a raw 500.

### Tests (must actually reproduce concurrency — this is why CI missed it)
- Unit/integration: seed a sequence bucket DELIBERATELY BEHIND max (current_value = max-3), then post
  a document; assert it succeeds (retry guard climbs the counter) and doc_no == max+1, no 23505 leaks.
- Concurrency test: fire N parallel posts against one bucket on teas_test; assert N distinct
  contiguous doc_nos, zero 23505, zero gaps beyond the allowed burn. (Run isolated — never overlap
  the Tier-3 gate/other test-DB writers, memory: parallel test-DB crash.)
- Reconcile SQL test: insert rows with doc_no ahead of current_value, run reconcile, assert
  current_value == max for every bucket; assert idempotent on 2nd run.
- RLS: run the reconcile + a post as SET ROLE teas (non-superuser) to catch NOBYPASSRLS 42501 that
  superuser test connections mask (memory: rls-masked-by-superuser-tests).

### Routing / blast radius
- Footgun + money-path + schema-adjacent (SqlScript) → **Fable co-authors design (this file) + owns
  the reconcile SQL + the ef/gate/commit + Tier-2 review**. Implementation of the retry helper +
  wiring = Codex at current quota crunch (footgun→Codex, never a cheaper Claude), or Sonnet after
  reset; Opus reviews the concurrency + SQL. NEVER skip the money-formula/invariant review section.
- Deploy: API changes + new SqlScript → full deploy, mandatory DB backup, verify applied_sql_scripts
  count increments by exactly the new script count (not "unchanged" like the last two releases).

═══════════════════════════════════════════════════════════════════════════════════════════
## CRIT-2 — TAX_OFFICER cannot run ภ.พ.30 at all (role-seed grant gap)

### Confirmed root cause (tax01 root-caused to source)
- `/reports/pnd30` preview + PDF + `.txt` export 403 for tax01 despite the role holding
  `tax.pnd30.read`. The endpoints in `TaxFilingEndpoints.cs` are actually gated on
  **`tax.filing.preview`**, which `530_seed_rbac_grant_reconcile.sql` never grants to `TAX_OFFICER`.
  Pure role-seed gap; the VAT numbers themselves tie out (tax-summary matches the July baseline).

### [x] Fix
- Grant `tax.filing.preview` (and audit the sibling filing perms `tax.filing.*` the pnd1/3/53/36/54
  endpoints check — tax01 + audit01 both saw filing pages that a Tax Officer/Auditor arguably should
  read) to `TAX_OFFICER` in the RBAC seed. Insert the CODE first if not present, THEN the grant —
  grant scripts silently no-op if the perm code is inserted by a later-numbered SQL
  (memory: rbac-seed-ordering-footgun). New SqlScript, idempotent.
- Decide grant scope deliberately (Fable, small judgement call): Tax Officer clearly needs
  filing.preview + the .txt/PDF export. Does it need filing.finalize/close-period? Almost certainly
  NOT (that's a separate SoD line — keep finalize on Chief/Admin). Grant read/preview/export only.
- Test: `RbacAuthMapTests` / `RbacMatrixTests` — assert TAX_OFFICER resolves `tax.filing.preview`;
  run with TEAS_REPO_ROOT set (memory: teas-repo-root-rbac-tests) or they throw locate-root, not a
  real failure. Add a case asserting TAX_OFFICER can hit the pnd30 preview endpoint (403→200).

### Routing
- Security-adjacent (RBAC grant) but low blast radius + proven in-repo seed pattern → Sonnet
  implements from this spec after reset, Opus or Fable reviews the grant scope + ordering. New
  SqlScript → DB backup at deploy.

═══════════════════════════════════════════════════════════════════════════════════════════
## Not in this spec (HIGH/MED, next batches — noted so scope is explicit)
- HIGH FE route-gating: 16 `/new` forms + CN/DN nav button + /period-close render for unauthorized
  roles (silent-403). ONE shared route-guard fix, own spec (biggest UX blast radius, no data breach —
  backend enforces; purch01+audit01 confirmed the POST is blocked). Next after these CRITs.
- HIGH report cutoff TB/BS(as-of) vs P&L(full-month incl future payroll) — no warning.
- HIGH approver has no working inbox; pending-agent-approvals widget 403s → false "all clear".
- HIGH Auditor missing read perms on ~10 modules → "no data" vs "no access" ambiguity.
- MED: AP-aging no tie banner; api-keys renders past deny + React #418; bank-recon diff no badge;
  payroll create button for Company Admin; review-context 403s on doc detail; EN error toast.

═══════════════════════════════════════════════════════════════════════════════════════════
## IMPLEMENTATION CONTRACT (confirmed facts — build exactly to these)

### Audit result (1a — DONE by Fable, no code change needed)
Every runtime doc_no assignment goes through `INumberSequenceService.NextAsync` (verified:
GlPostingService JV ×2, JournalService, PurchaseOrderService.Approve, TaxInvoice/Receipt/
VendorInvoice/PaymentVoucher/Quotation/SalesOrder-Delivery/BillingNote/TaxAdjustmentNote/
ExpenseClaim/FixedAsset services). NO SqlScript INSERTs a doc_no directly (400_seed explicitly
avoids it — comment: seeding docs "would not consume number_sequences → breaks trial-balance").
⇒ **Drift is not from an ongoing code leak; it is residual data state** (historical rows / teas_test
& migration-squash resets / a bucket whose counter was never advanced). So 1b (heal) + 1c (guard)
are the whole fix — do NOT hunt for a code path to patch.

### doc_no format (confirmed, DocumentNumber.cs)
`MM-YYYY-PREFIX[-SUB]-NNNN` — month 2d, year 4d, PREFIX = [A-Z]{2,5}, optional SUB =
alphanumeric hyphen-joined (BU and/or category, e.g. PV → "BU01-RENT"), seq = trailing 4–6 digits.
The tail sequence = `(regexp_match(doc_no,'-(\d{4,6})$'))[1]::int`.

### number_sequences (confirmed) — sys.number_sequences
cols: company_id, branch_id, prefix_code, sub_prefix (''=none), period_year, period_month (short),
current_value, last_issued_at. Unique: (company,branch,prefix,sub,year,month). Latest SqlScript=625.

### 1b — 626_reconcile_number_sequences.sql (Fable writes + reviews this one personally)
Idempotent, runs at startup, RLS-safe under the NOBYPASSRLS app role (no superuser assumption —
memory: v1.22.0 625 death). Algorithm:
- CTE `docs` = UNION ALL of (company_id, branch_id, doc_no) from every table that owns a doc_no:
  gl.journal_entries, purchase.purchase_orders, purchase.vendor_invoices, purchase.payment_vouchers,
  sales.tax_invoices, sales.receipts, sales.quotations, sales.sales_orders, sales.delivery_orders,
  sales.invoices, sales.billing_notes, sales.tax_adjustment_notes, payroll.payroll_runs,
  expense.expense_claims, fixedasset.fixed_assets (VERIFY each schema.table name + that it has
  branch_id; if a table lacks branch_id use the doc's branch or 0 consistent with how NextAsync was
  called for it — check the service). WHERE doc_no IS NOT NULL.
- parse each: month=substr(1,2)::int, year=substr(4,4)::int, seq=trailing digits,
  middle = text between "MM-YYYY-" and the final "-NNNN"; prefix = split_part(middle,'-',1),
  sub = middle with the leading "prefix-" stripped (''=none). Mirror DocumentNumber's grouping EXACTLY
  (prefix is first token; everything else is sub).
- aggregate max(seq) per (company,branch,prefix,sub,year,month).
- `INSERT … ON CONFLICT (…) DO UPDATE SET current_value = GREATEST(number_sequences.current_value,
  EXCLUDED.current_value)` — only ever RAISE the counter, never lower it. Insert missing buckets.
- Must be safe to re-run (idempotent) and touch only rows that are behind.

### 1c — retry guard (worker types, Fable reviews)
Add a bounded helper (e.g. `NumberedDocumentWriter.AllocateAndSaveAsync(alloc, save, ct)` or an
extension) that: calls the allocation (NextAsync) + document SaveChanges; on
`DbUpdateException` whose inner `PostgresException.SqlState == "23505"` AND ConstraintName matches a
`*_doc_no` unique index, re-invoke NextAsync (counter has advanced) and retry, max 5 attempts; on
exhaustion throw a clean DomainException ("doc.number_alloc_exhausted"), never a raw 500. Wire it
into every allocation site listed in the audit above (one call each — keep the diff mechanical).
Preserve the existing no-ambient-transaction semantics (don't wrap in a new tx that changes commit
behaviour); the retry re-reads the sequence which is the point.

### 627_seed_tax_officer_filing_grant.sql (worker types, Fable/Opus reviews scope)
Grant `tax.filing.preview` to role TAX_OFFICER (code-first, then grant — memory:
rbac-seed-ordering-footgun). Preview/export scope ONLY; do NOT grant finalize/close-period. Audit
sibling `tax.filing.*` perms the pnd1/3/53 endpoints check and grant the read/preview ones a Tax
Officer needs; leave finalize on Chief/Admin. Idempotent.

### [x] Gates
- `dotnet test` full suite: Domain.Tests 148/148 passed, 0 skipped. Api.Tests 905 passed / 1 failed /
  8 skipped / 914 total. The 1 failure (Pnd50FilingServiceTests ladder-mismatch) is PRE-EXISTING
  shared-teas_test flakiness, not a regression — file touches zero CIT/Pnd50 code, and re-running
  that file ALONE passes clean (7/7). Matches troubles-wiki's documented "single, DIFFERENT test
  fails each full run" class verbatim (names Pnd50 ladder mismatch as an example). All 8 skips are
  pre-existing (5 VisualEmit/Diagnostic raster-review helpers + PermissionLookupRlsTests, which skips
  because the local `accounting` TEAS_TEST_PG login lacks CREATEROLE to provision `teas_rls_test` —
  documented troubles-wiki entry, unrelated to this diff). None of my new tests skipped anywhere.
  New tests (all in Accounting.Api.Tests, ran + passed both in isolation and inside the full run):
  drift-behind-then-post succeeds + doc_no==max+1 (`NumberSequenceRetryGuardTests.
  Drift_behind_max_then_post_succeeds_and_lands_on_max_plus_one`); N-parallel-post distinct
  contiguous zero-23505 (`...N_parallel_posts_produce_distinct_contiguous_doc_numbers_with_zero_23505`,
  N=8 — proves concurrency-safety, NOT a drift-collision repro, see below); naive-vs-guarded
  drift repro (`...Naive_allocate_then_save_reproduces_23505_but_the_retry_helper_recovers` — THIS is
  the one that reproduces the pre-fix 23505 then shows the guard recovering, both assertions in one
  test); reconcile lifts-to-max + idempotent on 2nd run under REAL RLS
  (`NumberSequenceReconcileScriptTests.Script626_lifts_a_drifted_bucket_to_true_max_and_is_idempotent_under_RLS`);
  RbacAuthMap resolves TAX_OFFICER→tax.filing.preview (+read, NOT finalize) + a live HTTP 403→non-403
  probe (`TaxOfficerFilingGrantTests`, both methods).
- Reconcile ran under `SET ROLE pg_database_owner` (NOT literally `SET ROLE teas` — that role doesn't
  exist in this repo; `teas_rls_test` is the closer literal match but SKIPs here for the CREATEROLE
  reason above, so I used the repo's own established substitute for exactly this situation —
  troubles-wiki "New RLS test SKIPs..." names `pg_database_owner` as the fix). Ran the ACTUAL
  626_reconcile_number_sequences.sql file contents (not a paraphrase) under FORCE ROW LEVEL SECURITY,
  non-superuser, twice (idempotency check) — both runs clean, zero 42501.
- applied_sql_scripts count +2 at deploy (626,627) — DEPLOY-time check, out of my scope as implementer
  (dev/test only); flagging for whoever runs the deploy.
- Blast radius: 1 helper (NumberedDocumentWriter) + 17 allocation call sites across 14 files (the
  contract's "~10" undercounted — the full audit list in 1a's own comment enumerates GlPostingService
  JV×2, JournalService, PurchaseOrderService, TaxInvoice/Receipt/VendorInvoice/PaymentVoucher(+its
  WHT-cert sub-allocation)/Quotation/SalesOrder+DeliveryOrder/BillingNote/TaxAdjustmentNote/
  ExpenseClaim/FixedAsset/PayrollRun — that actually enumerates to 17, not ~10; wired all of them,
  full scope), 2 SqlScripts, 1 RBAC seed, 6 new tests. NO change to money formulas in GlPostingService
  (JE amounts) — only the alloc wrapper, confirmed by diff review (git diff --stat, no line inside a
  Debit/Credit/amount computation touched).

## Attempt log
- 2026-07-19 ~19:1x design drafted (Fable) from swarm findings + prod pm2 log + DB drift query +
  numbering-code read. Root causes CONFIRMED, not hypothesised.
- 2026-07-19 ~19:3x (post quota-reset) 1a audit DONE (no code leak), doc_no/number_sequences/latest-
  script confirmed, implementation contract written. Dispatching Sonnet impl + Opus review; Fable
  owns 626 reconcile SQL review + final diff + gate + commit.
- 2026-07-19 ~23:0x (Sonnet implementer) CRIT-1 + CRIT-2 fully implemented:
  `NumberedDocumentWriter.AllocateAndSaveAsync` (bounded 5-attempt retry helper, EF Core
  AutoSavepointsEnabled default confirmed to keep an ambient H8-tx healthy across a failed attempt —
  no manual savepoint code needed) wired into all 17 doc_no allocation sites;
  626_reconcile_number_sequences.sql (14 verified tables + wht_certificates, curly-brace-free per the
  EF ExecuteSqlRawAsync/string.Format footgun — caught by the test suite itself, not by inspection,
  when `{4,6}` regex quantifiers in a comment AND a WHERE clause both broke DbInitializer-style
  script application); 627_seed_tax_officer_filing_grant.sql (template-then-resync pattern mirroring
  530, looped per-company under RLS — 530 itself does NOT loop, a pre-existing latent risk flagged
  but out of THIS spec's scope, not touched). Two real bugs found and fixed during testing (not by
  static review): (1) ReceiptService's split into two SaveChanges wrote cash_received AFTER the
  Draft→Posted flip, violating 570_receipt_immutability_rls.sql's freeze list (23514) — fixed by
  setting CashReceived before the retry-guarded save. (2) The `if (entity.Status == PreState)`
  first-vs-retry heuristic silently skipped the domain Mark* guard (and its exception) whenever an
  entity was ALREADY in the wrong state for an unrelated reason (e.g. Pay-on-Draft expected
  `expense_claim.not_approved`, got a downstream GL error instead) — redesigned
  `AllocateAndSaveAsync` to pass an explicit `isFirstAttempt` bool instead of inferring it from
  entity state; all 17 call sites updated. Full suite: 148/148 Domain.Tests, 905/914 Api.Tests
  (1 pre-existing flaky failure confirmed via isolated re-run, 8 pre-existing skips, 0 new skips).
  Table-list surprises: contract's "sales.invoices" doesn't exist (real table is sales.tax_invoices,
  already covered) — omitted; ADDED tax.wht_certificates (WHERE direction='P') — PaymentVoucherService
  allocates its WT-NNNN doc_no through the identical NextAsync path, carries the same drift risk, was
  missing from the contract's draft list. Did not commit (per dispatch contract — orchestrator commits).
