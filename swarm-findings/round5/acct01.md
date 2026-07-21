# acct01 (Accountant) — UX Swarm ROUND 5 Findings — co5 prod v1.22.9

Run: 2026-07-21T16:31:06Z → 16:32:04Z (phase 1, ~1 min) + 16:34:34Z → 16:41:23Z (phase 2,
spaced re-checks, ~7 min incl. a transient re-login retry) — target https://teas.kazaki-rio.com,
company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด), concurrent with the other 9 swarm agents posting
docs on the same company (their screenshots — chief01/ar01/sales01/purch01/tax01/admin01 —
observed landing in shots/round5/ during my run, confirming real concurrency).

Tool: Playwright headless (msedge channel) via temp scripts `frontend/swarm5-acct01.mjs` +
`frontend/swarm5-acct01-phase2.mjs` (both deleted after this run per HARD RULE 4).

## Done (สิ่งที่ทำ+ผล)

- Login สำเร็จ (acct01) → dashboard — screenshot `shots/round5/acct01-dashboard.png`.
- `/me` → companyId=**5**, companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด, isSuperAdmin=false,
  `allowedCompanies` = **only** co5 — checked at start AND end of the run, both clean.
  **No cross-tenant leak** (HARD RULE 1 held).
- `/me/permissions` → roles=["ACCOUNTANT"], permCount=**58** (round4 baseline was 54 — grew
  +4, consistent with WP2 grant expansion; see Findings for one related behavior delta).
- **General Ledger**: `/reports/general-ledger`, account "1130 — ลูกหนี้การค้า" (AR) resolved
  from typed code, 31 rows rendered — screenshot `shots/round5/acct01-gl-report.png`. Drilled
  into `/journals/14` — rendered correctly, no crash — screenshot
  `shots/round5/acct01-journal-detail.png`.
- **Bank reconciliation**: `/reports/bank-reconciliation` — 2 bank options (incl. "All"),
  **auto-selected the sole real account** ("ธนาคารกสิกรไทย — 123-4-56789-0") with no manual
  pick needed, difference tile showed the red "ผลต่าง ฿3,210.00" with the explanatory badge
  **"มีรายการยังไม่กระทบยอด — ดูรายละเอียดด้านล่าง"** — screenshot
  `shots/round5/acct01-bank-recon.png`. (Bonus corroboration of chief01's WP3 primary
  assertion — auto-select + explanatory badge both confirmed live from a second role.)
- **ภ.พ.30** (`/reports/pnd30`): clicked "แสดงตัวอย่าง" (preview) — succeeded, status badge
  "Preview · manual", toast "แสดงตัวอย่าง ภ.พ.30 แล้ว", full form rendered (sales taxable
  ฿26,000.00/฿1,820.00, purchases ฿17,050.00/฿1,193.50, net VAT payable ฿626.50 — numbers
  higher than round-4 baseline, consistent with more docs posted since) — footer confirms
  **v1.22.9** — screenshots `shots/round5/acct01-pnd30-initial.png`,
  `acct01-pnd30-preview.png`. Raw DOM text dump: `swarm-findings/round5/acct01-pnd30-raw.txt`.
- ภ.พ.30 finalize button ("ยืนยัน/ปิดงวด"): **NOT present in the DOM at all** this round for
  ACCOUNTANT (confirmed both via Playwright locator count=0 and via the raw text dump —
  the only occurrence of "ยืนยัน/ปิดงวด" anywhere on the page is inside the deadline warning
  sentence, not a button). **Not clicked** either way (HARD RULE 2). See Findings — this is a
  behavior change from round 3/4 where the button was present+enabled for this role.

### Trial Balance Dr=Cr — repeated-refresh check (PRIMARY assertion)

9 refreshes total across ~10 minutes (2 phases — phase 1 tight/interleaved with
GL/bank-recon/pnd30 activity, phase 2 spaced ~100s apart to extend the observation window
further into the swarm's posting run), while the other 9 swarm agents posted documents
concurrently on co5.

| # | time (UTC) | balanced? | badge | totals (Dr / Cr) | screenshot |
|---|---|---|---|---|---|
| 1 | 2026-07-21T16:31:14.079Z | YES | Dr = Cr ✓ | ฿83,513.50 / ฿83,513.50 | shots/round5/acct01-tb-1.png |
| 2 | 2026-07-21T16:31:18.916Z | YES | Dr = Cr ✓ | ฿83,513.50 / ฿83,513.50 | shots/round5/acct01-tb-2.png |
| 3 | 2026-07-21T16:31:32.753Z | YES | Dr = Cr ✓ | ฿83,513.50 / ฿83,513.50 | shots/round5/acct01-tb-3.png |
| 4 | 2026-07-21T16:31:43.580Z | YES | Dr = Cr ✓ | ฿83,513.50 / ฿83,513.50 | shots/round5/acct01-tb-4.png |
| 5 | 2026-07-21T16:31:52.797Z | YES | Dr = Cr ✓ | ฿83,513.50 / ฿83,513.50 | shots/round5/acct01-tb-5.png |
| 6 | 2026-07-21T16:32:03.680Z | YES | Dr = Cr ✓ | ฿83,513.50 / ฿83,513.50 | shots/round5/acct01-tb-6.png |
| 7 | 2026-07-21T16:37:44.940Z | YES | Dr = Cr ✓ | ฿83,513.50 / ฿83,513.50 | shots/round5/acct01-tb-7.png |
| 8 | 2026-07-21T16:39:31.795Z | YES | Dr = Cr ✓ | ฿89,933.50 / ฿89,933.50 | shots/round5/acct01-tb-8.png |
| 9 | 2026-07-21T16:41:22.522Z | YES | Dr = Cr ✓ | ฿89,933.50 / ฿89,933.50 | shots/round5/acct01-tb-9.png |

**Tie held across all 9 refreshes: YES — Dr=Cr never broke.** Grand total jumped
฿83,513.50 → ฿89,933.50 (+฿6,420, +7.7%) between checks #7 and #8 — direct proof new
concurrent postings landed mid-run — and the tie held through and after that jump. Full raw
log: `swarm-findings/round5/acct01-tb-log.json`.

## Fix-verify (explicit, per WP)

- **acct01 PRIMARY (TB Dr=Cr holds under concurrent load): CLOSED.** 9/9 checks balanced,
  including across an observed +7.7% grand-total jump from concurrent swarm posting (see
  table above). No 500s, no unbalanced badge, no crash on any TB/GL/bank-recon/pnd30 read
  this round.
- **WP3 (chief01's primary, bonus-corroborated here as a second role):** bank-reconciliation
  auto-selects the sole bank account (confirmed: `auto-selected value="1"` with no manual
  pick) **and** shows the explanatory badge on a nonzero difference
  ("มีรายการยังไม่กระทบยอด — ดูรายละเอียดด้านล่าง") — **CLOSED**, matches chief01's expected
  fix. (Did not check the TB/BS/P&L date-basis header text explicitly this round — chief01
  owns that verification; screenshot `acct01-tb-1.png` does show "ข้อมูล ณ วันที่ 21/07/2569"
  under the Trial Balance title, which is consistent with the date-basis label fix being live.)
- **CRIT-1 (doc-numbering writes 2xx, zero 500/23505):** acct01 is read-only this round and
  posts no numbering-write docs directly (owned by sales01/ar01/purch01/ap01/appr01). **Strong
  indirect corroboration**: the swarm was demonstrably posting hard during my session (TB
  grand total jumped +7.7% between two consecutive checks 107s apart) and the Trial Balance
  never desynced — a numbering collision that corrupted a posting mid-transaction would very
  likely show up as an unbalanced/orphaned entry; it never did. Zero 5xx responses observed on
  any page/API call this entire run (console + response listeners both empty of 5xx).
- **CRIT-2 (tax01's primary, re-verified here from ACCOUNTANT):** ภ.พ.30 preview opened
  cleanly, no 403, full numbers rendered, footer confirms v1.22.9. **YES, still closed** from
  this second role's vantage point. Finalize button not present (see Findings) — correctly
  not exercised either way per HARD RULE 2.

## Regressions

- None found on acct01's surface (TB, GL, bank-recon, ภ.พ.30 preview). Zero 5xx, zero
  unbalanced TB, zero crashes across 9 spaced checks and all report drill-downs.

## Findings (severity CRIT/HIGH/MED/LOW)

| Severity | Area | Symptom | Repro | Evidence |
|---|---|---|---|---|
| LOW (observation, likely intentional) | ภ.พ.30 finalize button / RBAC scope | For ACCOUNTANT (acct01), the "ยืนยัน/ปิดงวด" (finalize) button is now **absent from the DOM entirely** (`<PermissionGate scope="tax.filing.finalize">` no longer renders it for this role). In round 3 and round 4 (v1.22.6/v1.22.7) the same role saw this button **present and enabled**. permCount for ACCOUNTANT also grew 54→58 this round, so this looks like a deliberate SoD tightening (accountant can no longer self-finalize a tax filing) bundled into the same permission-grant batch that added the WP2 module grants — not a break, since preview/PDF/.txt access is unaffected and my mission never required clicking finalize. Flagging only because it's an observable behavior delta from the round3/4 baseline that Fable's consolidation should confirm was an intended part of WP1-6 (vs. an accidental over-restriction that might also affect a role that legitimately needs `tax.filing.finalize`). | Login as acct01, go to `/reports/pnd30`, run preview, inspect DOM/button locators for "ยืนยัน" — count=0 (raw dump confirms: only the deadline-warning sentence contains that Thai string, no `<button>`) | `swarm-findings/round5/acct01-pnd30-raw.txt`; `shots/round5/acct01-pnd30-preview.png` (no finalize button visible in the action row) |

No CRIT/HIGH/MED findings this round for acct01's surface — TB tie held under real concurrent
load (including a caught mid-run jump), GL/bank-recon/pnd30 all rendered cleanly, no tenant
leak, zero 5xx.

## Denied-as-expected (RBAC ที่ deny ถูกต้อง)

- N/A — no deny surface was in this role's mission this round (read-only TB/GL/bank-recon +
  pnd30 preview). The finalize-button absence above is logged as a Finding/observation, not a
  deny-test, since verifying *why* it disappeared is outside acct01's assigned scope.

## Console / network errors captured (whole session, both phases)

```
[console.error] https://teas.kazaki-rio.com/login — Failed to load resource: 404 ()
[console.error] https://teas.kazaki-rio.com/ — Failed to load resource: 403 ()
[console.error] https://teas.kazaki-rio.com/login — Failed to load resource: 404 ()  (phase 2 first attempt)
[console.error] https://teas.kazaki-rio.com/login — Failed to load resource: 404 ()  (phase 2 retry)
[console.error] https://teas.kazaki-rio.com/ — Failed to load resource: 403 ()  (phase 2 retry)
```

No `pageerror` events, no 5xx responses (checked via a `response` listener on every request,
not just console), no stack traces observed. The `/login` 404 and `/` 403 are the same benign
pre-auth noise acct01 also saw in rounds 3 and 4 (not a new regression). One transient
`page.waitForURL: Timeout 15000ms exceeded` occurred on the phase-2 re-login's *first*
attempt — root cause was simply prod being slower under concurrent 10-agent load (a manual
retry with the same credentials resolved in ~5.1s once isolated); raised the phase-2 script's
login timeout to 30s and it succeeded cleanly on retry, so this is a script-tuning note, not a
product bug.

## Artifacts

- Run log: `swarm-findings/round5/acct01-run.log`
- TB structured log: `swarm-findings/round5/acct01-tb-log.json`
- Console error log: `swarm-findings/round5/acct01-console-errors.log`
- ภ.พ.30 raw DOM text dump: `swarm-findings/round5/acct01-pnd30-raw.txt`
- Screenshots: `shots/round5/acct01-*.png` (dashboard, tb-1..9, gl-report, journal-detail,
  bank-recon, pnd30-initial/preview)

## Cleanup

- Temp scripts `frontend/swarm5-acct01.mjs` and `frontend/swarm5-acct01-phase2.mjs` — both
  deleted after this run per HARD RULE 4.
- No git/repo edits, no build, no `ยืนยัน`/finalize clicks, no master-data create/edit/delete.
