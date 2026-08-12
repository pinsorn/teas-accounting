# PROGRESS — R1 ledger integrity (the break-it fix round, release 1 of 4)

Checkpoint written 2026-08-12 at 5-hour quota 87% (block 95, resets in ~1h). 7-day is 19% — fine,
Ham's full-stop rule is not in play. Nothing is lost: the two in-flight work packages live in the
**uncommitted working tree**, which survives a pause.

Spec: `specs/fix-breakit-r1-ledger-integrity.md` (amended twice today — read the ⚠️ blocks in §3.3 and §3.5,
they override everything below them). Plan: `PLAN-fix-breakit-v1271.md`. Findings: `VERDICT-breakit-v1271.md`.

## Committed and done

| WP | commit | what |
|---|---|---|
| WP-1 | `e750780` | non-VAT invoices accrue Dr 1130 / Cr 4000 at issue; receipt settles AR instead of re-recognising revenue |
| WP-2 | `2eb61c3` | `/admin/nonvat-ar-backfill?mode=preview\|apply` — corrects pre-fix history; preview output is the accountant's deliverable |
| WP-3 | `7eaa81a` | sub-satang rejected at `JournalEntry.MarkPosted`, the seam every posting path shares |
| — | `f34fe00` | payroll test fixture: hire-date default decoupled from the shrinking fresh-year pool |
| — | `d59aaa7` | WP-6 added (legacy-data audit) + it gates the R1 deploy |
| — | `a2e9508` | §3.3 amended — the expense-account rule had authorized the bug it was meant to close |
| — | `97ace1c` | §3.5 amended — the payroll period-END ceiling removed; it broke arrears pay and caught nothing |
| — | `84fcb1e` | STATUS.md refreshed |

## In flight — UNCOMMITTED, in the working tree

**WP-4 — expense-claim account type + fixable categories.** Round 3 code complete, Opus REJECTed twice.
- Claim-line rule is now an **allowlist of exactly one account**: Asset permitted only when it IS the capex
  category's own `DefaultExpenseAccountId`.
- Category-master path additionally **denylists** the company's configured cash/bank/AR/input-VAT/WHT role
  accounts plus every `bank_accounts.GlCashAccountId` — closing the "admin poisons the category" variant.
- Re-validation added at Submit and Approve (not only Pay), and **cancel is now allowed from Approved** so an
  invalidated claim has an exit.
- **STATE WHEN PAUSED: it had just been given the test-DB all-clear and was running its evidence pass.**
  The mandatory item is the RED proof for `CAPEX_category_override_to_BANK_is_rejected_the_P0_this_WP_exists_to_close`
  against round-1's rule. **Do not commit WP-4 without that RED→GREEN.**

**WP-5 — payroll period + pay-date guard.** Code complete, waiting on the test DB.
- `EnsureOpenAsync(PayDate)` is the real guard, called first in both `PostAsync` and `PayAsync`; two distinct
  error codes (a closed month names the O14 reopen route, a never-opened future month must not).
- Floor only (`PayDate` before the period start is refused). **No period-end ceiling** — see the §3.5 amendment.
- `Pnd1_filings_follow_payment_date_not_period` drives the real service again; T19 now proves **arrears pay works**
  (December period paid 5 January) alongside pre-payday posting.
- **Needs: my ALL-CLEAR on the test DB, then T18–T20 + the full `PayrollRunServiceTests` class.**

## Resume order
1. Whichever worker holds `teas_test`, let it finish; then ALL-CLEAR the other. **One test runner at a time** —
   a concurrent run has crashed the test host before.
2. Get WP-4's RED→GREEN, read its diff, run the full suite, commit.
3. Same for WP-5.
4. **Then R1 code is complete (5/5) — but do NOT deploy.** Run WP-6 first (below).

## 🔴 The deploy gate — do not skip this
`tools/audit-subsatang.sql` is READ-ONLY and must run **on prod** before R1 ships. WP-3's guard is correct, but on
a company already holding >2dp data it turns silent wrongness into a **hard dead-end**: year-end close/reopen,
paying an already-posted payroll run, and WP-2's own backfill all re-post STORED amounts and would be refused,
with advice ("restate in satang") that is impossible on immutable history. co5/co7 are known polluted; **Repttown
uses all four pollution paths and must be assumed polluted until measured.** WP-4's amendment adds a second audit
need: `expense_claim_lines` whose resolved account the new rule would now refuse.

## Known-dirty test environment (cost five false alarms today)
`teas_test` has years of accumulated state. Failures seen and diagnosed this session: 41 poisoned fixture
employees (4-dp salary, never deactivated), a fresh-year pool that has drifted below 2020, a `pk_companies` id
collision, and two suite failures proven pre-existing. **A full reset is the real remedy and is queued before R2.**
To prove a red test is not yours: run it at HEAD in a throwaway worktree (`git worktree add <tmp> HEAD --detach`).

## Still open beyond R1
R2 (compliance filings) / R3 (guards + doc numbering) / R4 (documents, reports, LOW cluster) — none designed yet.
The three document-lifecycle features from 2026-08-06 wait on Ham's 5 answers in
`specs/doc-lifecycle-cancel-reissue-backdate.md` §6; **feature C (delete the "customer has paid" button) must ship
WITH R1** — R1 turns `MarkSettledAsync` into an active hole. The Repttown amended-ภ.ง.ด.50 track runs in parallel
and is a human task; the 1.5%/month surcharge is accruing.
