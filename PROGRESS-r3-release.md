# PROGRESS — R3 release (the round after v2.0.0)

Updated 2026-08-14 ~00:45. **5-hour quota 79%** at the last reading, 7-day 51%. Nothing is lost:
everything verified is committed; only the H1 work sits uncommitted in the working tree.

## Committed on main, NOT yet released
| commit | what |
|---|---|
| `18f6fcc` | **F1** — ภ.พ.36 surfaces foreign-service payments it would otherwise miss entirely |
| `0381d60` | **H4** — attachment download AND delete authorize against the parent document |
| `3d535b6` | H1 WP-0 ticked with the prod probe evidence |
| `679ec54` | STATUS refreshed |

## In the working tree, uncommitted — H1 WP-1/2/3/3b
35 files (cap was 32; the +3 is accepted and explained in the spec's attempt log). Full suite green at
**1206 passed / 0 failed / 14 skipped**; `tsc` 0; vitest 67/67; i18n parity 2020/2020.

**Tier-2 REJECTed on one must-fix and I sent three fixes to the warm worker:**
1. `frontend/app/(dashboard)/page.tsx:52` — `gaps.data?.duplicates.length` guards `data` but not
   `duplicates`, so during the deploy window (frontend out, API not yet restarted, or mid-restart
   applying `634`/`635`) the old response shape makes the **dashboard white-screen for every user**.
   One character: `duplicates?.length`.
2. `634`'s `seq_str::int` — inherited from `626`. An 11+ digit trailing run overflows int4, the
   transaction rolls back, the script is never recorded, and it retries on every boot: the
   permanently-un-deployable mode. `613` was bitten by exactly this. Must be bounded **without curly
   braces** (F20 — the loader runs the file through `string.Format`).
3. The spec's blast-cap header still says 32; the accepted actual is 35.

## What Fable verified personally
- **The reconcile arithmetic is a true MAX, not a sum or last-writer.** `634` groups by
  `(company, year, month, middle)` with `branch_id` dropped, takes `MAX(seq)` over the **documents**
  (not the counters), and `ON CONFLICT … GREATEST(existing, EXCLUDED)` so it can only ever lift.
  Idempotent. Taking the max over documents is the safer direction: a counter ahead of its documents
  yields a gap, never a duplicate.
- It pins `app.company_id` per company transaction-locally, mirroring `626` — without that, RLS returns
  zero rows at boot and the whole script silently does nothing.
- **A deploy-probe correction I owe myself:** `634` does NOT delete the old per-branch counter rows, it
  only lifts branch 0. So a probe asserting "one counter per number space" would FAIL on a correct
  deploy. The right probe is: **the branch-0 row is ≥ every other branch's row for the same
  (company, prefix, sub, year, month)**.

## Deliberately NOT in this release
- **WP-4, the unique indexes.** Shipping them while co2 still holds `07-2026-RC-LAB-0001` twice would
  make the release permanently un-deployable — a failed EF migration is not recorded and retries on
  every boot. Gated on the remediation decision below.
- The 500 family · conversion routes checking the wrong scope · the year-close deadlock · the year=3000
  bound. Not designed yet; cramming them in now trades quality for speed.

## Waiting on Ham / a CPA — none of it blocks code
- **The co2 duplicate receipt.** Two POSTED receipts share `07-2026-RC-LAB-0001` (฿3,000 / ฿18,000).
  Both real tenants are **non-VAT**, so this is a ใบเสร็จรับเงิน, not a ใบกำกับภาษี — ม.86/4 and the
  filed-ภ.พ.30 framing in the spec's §9 do **not** apply. The question to actually ask is in the spec's
  §6-RESULTS.
- E2 (an employee paying an overseas provider — researched, likely DOES keep the company liable) and E3
  (confirmation of the ภ.พ.36 PV-only rule).

## Resume order
1. Take the worker's three fixes; re-run `tsc`, vitest and the reconcile-script tests only.
2. Read the H1 diff, commit it.
3. Release: version bump (minor → **2.1.0**), build, deploy with a script modelled on `publish/v2.0.0/`
   — reusing its hard-won probe lessons (grep the ARTIFACT with `strings -a -el`, never an HTTP status,
   since this app authenticates before it routes; anchor on artifacts not words; carry `.next/cache`
   forward or the font fetch rate-limits).
4. Add the branch-0-is-max probe above to the API deploy script.
5. Tier-4 through the browser: the number-gaps page must now show the co2 duplicate and **no** green
   compliant shield.

---

## UPDATE ~01:20 — the three Tier-2 fixes are IN the tree, verified by Fable in source
- `page.tsx:52` now `gaps.data?.duplicates?.length ?? 0`.
- `634` carries **two** guards: `CASE WHEN length(seq_str) <= 9 THEN seq_str::int ELSE NULL END`
  and `WHERE seq IS NOT NULL` in the buckets CTE. Brace scan 0. Spec header now says 35.

**The worker overruled my instruction and was right.** I said "bigint + an 18-char cap, like `613`".
It pointed out `sys.number_sequences.current_value` is plain `int`, so a bigint intermediate only
*relocates* the overflow to the INSERT's implicit bigint→int cast. It used `length(seq_str) <= 9`
instead — the largest length for which every possible value is inside int4. Simpler and actually
correct. Record the reasoning, not just the outcome: **`613`'s bigint fix is safe only because its
target is a synthetic `generate_series` value; copying it to a column-bound write does not transfer.**

**It also found a second failure of the same class while verifying**: a bucket whose rows are ALL
excluded by the guard yields `MAX(seq) = NULL`, and `current_value` is NOT NULL → `23502` → the same
never-recorded / retries-every-boot mode, different SQLSTATE. RED-proved both guards separately.

**Disclosed self-inflicted incident, resolved:** its first draft seeded the garbage row as `POSTED`,
which `020_journal_immutability.sql` makes permanently unfixable (UPDATE and DELETE both blocked). 4
rows landed in the shared `teas_test` and broke the pre-existing `626` test, which scans every company
unfiltered. It disabled the two triggers, deleted exactly those 4 rows, re-enabled, and verified counts
before and after — then fixed the root cause by seeding `DRAFT` instead (the reconcile's `raw_docs` CTE
has no status filter, so the script still picks it up, but the test's own cleanup works). Acceptable on
`teas_test`; would NOT be on prod.

**`626` carries both of the same unguarded risks** — out of scope this round ("do not edit 626"), on the
fix-later list with: the duplicates-vs-gaps `docType` predicate mismatch, `jsdom@30`'s Node floor vs the
repo's `engines: >=20`, and CI never running vitest at all (so the compliance-control test T11 has zero
automated enforcement).

## Where this stands right now
Full suite re-running over the final tree. Everything else is verified. **Next action on resume:
read the suite result, then commit H1 and continue to the release.** Do not re-plan; the resume order
above still stands.

---

# 🔻 DUPLICATE CLEANUP — Ham authorised it ("Backup เอกสารไว้ แล้วล้างแม่งเลย", 2026-08-14 ~01:10)

## Backup: DONE, verified, on the prod box
`~/backups/h1-dupes/` — full `pg_dump` (`teas-pre-dupe-cleanup-20260814-011100.sql.gz`, 359,452 bytes,
`gunzip -t` verified) **plus** an all-columns CSV export of every duplicate row:
`dupe_co2_receipts.csv` (2 rows) · `dupe_co5_ti.csv` (13) · `dupe_co5_rc.csv` (11) ·
`dupe_co5_vi.csv` (10) · `dupe_co5_cn.csv` (2).

## NOT executed tonight, deliberately
The deletion needs the immutability triggers disabled while it runs. At 89% of the 5-hour quota, being
cut off mid-operation would leave **immutability triggers switched off on production** — the worst
reachable state. The backup is read-only and was safe to take; the deletion waits for a fresh window.

## ⚠️ READ THIS BEFORE DELETING — two things that are not obvious
**1. A posted receipt has a journal entry behind it.** Deleting the receipt row alone leaves an orphaned
JE; deleting both changes the trial balance. Decide which before running anything, and check what
`sales.receipts` → `gl.journal_entries` actually links through. This is the part that makes "just delete
the row" not a one-liner.

**2. For co2 the two rows are two REAL, DIFFERENT transactions** — ฿3,000 (receipt_id 3, branch 2,
2026-07-12) and ฿18,000 (receipt_id 21, branch 0, 2026-07-20) — that merely collided on a number.
Deleting either removes a genuine posted transaction from the books. **Renumbering the later one
reaches the identical end state** (zero duplicate `(company_id, doc_no)` pairs, so WP-4's index applies)
**without losing a transaction**, and the triggers have to come off either way.
Ham said "ล้าง", so delete is the instruction and the backup makes it recoverable — but surface this
choice to him before executing, because it costs nothing to ask and a deleted posted document cannot be
un-deleted from the app.
co5's 10 duplicates carry no such concern: it is a test playground already slated for wipe+reseed.

## Runbook when the window is fresh
1. Re-verify the backup is still present and `gunzip -t` clean **before touching anything**.
2. Re-run §6 Q1 — confirm the duplicate set is still exactly the 11 rows recorded in §6-RESULTS. If it
   has changed, STOP: something minted a new one and that is a different problem.
3. Decide the co2 question above (delete vs renumber) and the JE question. Write the decision down here
   before running the statement.
4. `ALTER TABLE ... DISABLE TRIGGER` → the change → `ENABLE TRIGGER` → **verify `tgenabled='O'` on every
   trigger you touched**, in the same session. Never end the session between disable and re-enable.
5. Re-run Q1: expect zero rows.
6. Only then is WP-4 unblocked. Ship it in its own release, after v2.1.0 — the migration must never meet
   a duplicate.

## Resume protocol if this dies mid-run (CLAUDE.md's irreversible-write rule)
The resumed session **re-establishes what was already written before writing anything**: check trigger
state first (`SELECT tgname, tgenabled FROM pg_trigger WHERE ...`), then re-run Q1 to see what is already
gone. Do not re-run the delete blind — it is not idempotent against a partially-completed run.

---

# ✅ v2.1.0 IS LIVE — released and verified end to end (2026-08-14 ~01:45)

API and FE both deployed. **10/10 API probes and 4/4 FE probes pass.**

## Tier-4 browser leg — PASSED on the live site
`/number-gaps` on co2 Repttown shows a **red** "พบเลขเอกสารซ้ำ — ต้องตรวจสอบทันที (1)" banner and a
table row `sales.receipts | 07-2026-RC-LAB-0001 | copies 2 | branches 0, 2` — **no green compliant
shield**. The API returns `hasDuplicates: true` with the branch ids that prove the two-channel
mechanism. co7, which has no duplicates, still shows the green shield. Footer reads `TEAS · v2.1.0`.
That is exactly what WP-3b existed for: the control now sees a live breach instead of asserting
compliance over it. Screenshot: `Z:\temp\claude-chrome-screenshots-0OSZqs\screenshot-1786646944394-1.jpg`

## Two probe bugs cost one rollback — both mine, both now in troubles-wiki
The first attempt rolled back on three FAILs, **none of which were code**:
1. `strings -a -el` on a method NAME. Literals live in the UTF-16LE #US heap; identifiers live in the
   UTF-8 #Strings heap. Measured: `ResolveParentAsync` utf8=1, utf16=0.
2. `public.applied_sql_scripts` does not exist — it is `sys.`. The query errored and yielded an empty
   string, which the probe compared against `"1"` and failed with a blank: `count=`. **An empty result
   is not zero**, and the v1.28.0/v2.0.0 scripts have carried the same wrong schema all along, unnoticed
   because it was informational there.
3. Verified afterwards that `634` and `635` had in fact applied cleanly at 01:41:35 during that very boot.

This is the second probe-caused rollback in two releases. Both times the release was fine and the gate
was wrong. The auto-rollback did its job and nothing was damaged either time — but a gate that cannot
tell success from failure is worse than no gate, because it looks like verification.

## What shipped
`18f6fcc` F1 ภ.พ.36 payment detection · `0381d60` H4 attachment download/delete guard ·
`ca820f5` H1 company-wide numbering + reconcile + duplicate detection.

## Still open, unchanged by this release
- **WP-4 (the unique indexes)** — cannot ship while the 11 duplicates exist. Backup is taken
  (`~/backups/h1-dupes/`, full dump + all-columns CSVs). Ham authorised the cleanup; the runbook and
  its two non-obvious risks are recorded above.
- The 500 family · conversion routes checking the wrong scope · the year-close deadlock · the
  year=3000 bound.
- CPA: E2 (employee-paid overseas service — researched, likely DOES keep the company liable) and E3.

---

# ✅ DUPLICATE CLEANUP DONE — renumbered, not deleted (2026-08-14 ~10:20). Ham chose "เปลี่ยนเลข".

**All 11 duplicates resolved. Every document preserved — nothing was deleted.**
Q1 across all 15 doc-carrying tables now returns **0 rows**, with Q0's blindness control passing first
(`postgres`, 48 tax-invoice rows visible) so the zero is real and not RLS blindness.

## What was done
Each duplicate pair kept the EARLIER document's number; the later one was renumbered to the next free
number in its own `(company, prefix, sub, year, month)` space, and its journal entry's `reference` and
`description` were updated to match. The sequence counters were lifted so the next allocation cannot
land on a renumbered document.

| company | table | id | was | now | JE |
|---|---|---|---|---|---|
| **co2 (live)** | receipts | 21 | `07-2026-RC-LAB-0001` | **`07-2026-RC-LAB-0003`** | 103 |
| co5 | tax_invoices | 11–14 | `TI-0001..0004` | `TI-0026..0029` | 68, 70, 71, 74 |
| co5 | receipts | 10, 11 | `RC-0001, RC-0002` | `RC-0017, RC-0018` | 69, 72 |
| co5 | vendor_invoices | 11–13 | `VI-0001..0003` | `VI-0008..0010` | 73, 92, 112 |
| co5 | tax_adjustment_notes | 5 | `CN-0001` | `CN-0002` | 232 |

co2 afterwards: receipt 3 = `0001` ฿3,000 · receipt 21 = `0003` ฿18,000 · receipt 22 = `0002` ฿7,200.
Both transactions intact, and no numbering gap (0001/0002/0003 are contiguous).
The co2 report now returns `duplicates: 0, gaps: 0`.

## How it was made safe
- **The doc_no lives in more places than the document.** `gl.journal_entries.reference` AND
  `.description` carry a copy — found by sweeping every column named `doc_no`/`reference`/etc. Renaming
  the document alone would have left the ledger pointing at a number that no longer identifies it.
  `gl.journal_lines.reference` was checked and holds none.
- **Immutability triggers really do block this** — confirmed by a dry run inside a rolled-back
  transaction: `fn_enforce_receipt_immutability` raises "Cannot modify critical fields of posted
  Receipt". So the triggers had to come off.
- **Everything ran in ONE transaction per company**: disable → update → re-enable → commit. A failure
  anywhere rolls back the `ALTER TABLE` too, so the triggers can never be left off — which is exactly
  why this waited for a fresh quota window instead of running at 89%.
- **A collision guard raised before any write.** There is no unique index yet (that is WP-4), so a
  target number that was already in use would have silently minted a NEW duplicate rather than failing.
  A `DO` block asserted all ten targets were free and would have aborted the transaction.
- Trigger state verified `O` on all five tables after each commit.
- Backup taken beforehand: `~/backups/h1-dupes/` (full dump + all-columns CSVs of every affected row).

## WP-4 is now UNBLOCKED
Zero duplicates means the unique indexes can ship without the migration meeting a row it cannot index.
That is the next release, on its own.

---

# WP-4 (unique indexes) — code-complete, Tier-2 APPROVED, one test fix in flight

## Tier-2 sharpened the risk I had understated
I had been calling a failed migration "the release becomes permanently un-deployable". Tier-2 read
`Program.cs:441` and corrected it: `DbInitializer.InitializeAsync` is awaited **unguarded, before
`app.Run()`**. So a `CREATE UNIQUE INDEX` that raises `23505` means **the API never starts and
restart-loops — a production outage**, not merely a blocked release. The schema itself is safe (the
migration transaction rolls back atomically); the service is not. Rollback path is redeploying the
previous artifacts, NOT `Down()`, because the app will not be up to run it.

**Four preconditions at deploy time, now conditions of the approval:**
1. Confirm WP-1 + SqlScript `634` are actually live on prod — the "no new duplicates can be minted"
   argument rests entirely on the allocator being company-wide. **(Verified: v2.1.0 is live and its
   deploy probe asserted `sqlscript_634_applied`.)**
2. Re-run Q0 **then** Q1 immediately before deploy — not the 10:20 run. Q0 first, always: under the app
   role Q1 reads clean regardless.
3. Preflight the DROP targets — `DropIndex` emits no `IF EXISTS`, so a name mismatch is the same outage
   through a different door. Expect exactly the seven `%branch_id_doc_no` names.
4. DB backup taken.

## What Fable verified independently
Ran the **index's own predicate** against prod on all seven tables: **0 violations each**. The migration
will build. Tables hold tens of rows, and migrations run before the host serves, so the drop/create
window is not reachable by a concurrent write.

Tier-2 also produced better evidence than the implementer's for the seam I was most worried about:
`ix_journal_entries_company_id_doc_no` is a **shape-identical partial unique index**, and
`NumberedDocumentWriter`'s self-heal has been healing collisions on it in production since July. So
Postgres demonstrably reports a name containing `doc_no` for exactly this index shape.

## The two failing tests are the index doing its job
Full suite over WP-4: **1207 / 2 / 14**. Both failures are `NumberGapReportDuplicatesTests`, and both die
at the *fixture*: `23505 … "ix_tax_invoices_company_id_doc_no"`. Those WP-3 tests seed a duplicate so the
detection report has something to find — and **all fifteen doc-carrying tables now carry a UNIQUE index
on `(company_id, doc_no)`** (verified against the live DB), so no such row can be inserted anywhere.

That reframes the report: post-WP-4 it is defence-in-depth for a duplicate that **predates** the index or
appears if one is ever dropped. The fixture must therefore build that condition the only way it can now
exist — with the constraint temporarily absent, inside a transaction that is **rolled back**, since
Postgres DDL is transactional and a crash still restores the index. Dispatched with an explicit
instruction not to "fix" it by deleting the tests or weakening them, because that would silently remove
the only coverage proving the control can detect anything.

Also folded in Tier-2's fix-later: T7's drift setup never asserts rows-affected, so a WHERE that matched
nothing would leave the counter untouched, let the next post allocate naturally, and pass **green with
the self-heal never exercised**.
