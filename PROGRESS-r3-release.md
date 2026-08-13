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
