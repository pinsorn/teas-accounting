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

**1a. AUDIT the drift sources (do FIRST, informs 1c).** Grep every write to a `doc_no` column that
does NOT go through `INumberSequenceService.NextAsync` (seeders, importers, year-end closing JE,
reversal JE, payroll settlement JE, statement-import matched docs). Each one that assigns a doc_no
directly is a drift source. Record the list in this spec. If any is a live code path (not just a
one-off seed), route it through NextAsync or make it bump the sequence in the same tx.

**1b. RECONCILE current prod + a guard for future drift (SQL, new SqlScript — DB backup SOP).**
One idempotent reconcile that lifts every sequence bucket to the true max across all doc tables:
```
-- for each (company,branch,prefix,period) set current_value = GREATEST(current_value, max_doc_seq)
-- where max_doc_seq = the numeric tail of MAX(doc_no) across the doc table that owns that prefix.
```
Runs at API startup like other SqlScripts (idempotent; safe to re-run). This heals co5 AND any other
tenant silently carrying drift right now. NOTE: this is the money/footgun bit — Fable writes/reviews
this SQL personally, runs it against a teas_test copy first, and it runs under the NOBYPASSRLS app
role (memory: v1.22.0 died on 625 running as superuser — pin per-company or make it RLS-safe).

**1c. RETRY GUARD in the post/approve services (the durable belt-and-braces).** Even with 1a+1b, a
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

### Fix
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

## Attempt log
- 2026-07-19 ~19:1x design drafted (Fable) from swarm findings + prod pm2 log + DB drift query +
  numbering-code read. Root causes CONFIRMED, not hypothesised. Implementation pending quota reset.
