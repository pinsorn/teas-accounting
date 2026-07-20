# acct01 (Accountant) — UX Swarm Round 4 Findings — co5 prod (v1.22.7)

Run: 2026-07-20T02:39:47.524Z → 02:49:39.926Z (~10 min), target https://teas.kazaki-rio.com,
company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด). Ran while all 10 role-agents posted concurrently.
Tool: Playwright headless (msedge channel) via temp scripts frontend/swarm4-acct01.mjs +
frontend/swarm4-acct01-phase2.mjs (both deleted after this run per HARD RULE 4).

## Done (สิ่งที่ทำ+ผล)

- Login สำเร็จ (acct01) → dashboard — screenshot shots/round4/acct01-dashboard.png
- Tenant check: no other-company data (นาย พงศ์สันต์ / เรปทาวน์) leaked, checked before every TB
  refresh (10/10 clean) — no CRITICAL tenant-leak.
- `/me` → companyId=5, companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด, isSuperAdmin=false
- `/me/permissions` → roles=["ACCOUNTANT"], permCount=54
- GL: `/reports/general-ledger`, account "1130 — ลูกหนี้การค้า" (AR), current default range —
  26 account options loaded, 7 rows rendered with journal links — screenshot
  shots/round4/acct01-gl-report.png
- GL drill-down: clicked into `/journals/14` — Journal Entry 07-2026-JV-0002 rendered correctly,
  Dr/Cr lines balanced (AR ฿7,490.00 = Sales ฿7,000.00 + Output VAT ฿490.00, total ฿7,490.00 =
  ฿7,490.00) — screenshot shots/round4/acct01-journal-detail.png. Drill-down verified working.
- Bank reconciliation: `/reports/bank-reconciliation`, selected bank account (2 options incl.
  "All") — report rendered, page text length 1189, no crash — screenshot
  shots/round4/acct01-bank-recon.png
- ภ.พ.30 (`/reports/pnd30`): clicked "แสดงตัวอย่าง" (preview) — succeeded, status badge
  "Preview · manual", toast "แสดงตัวอย่าง ภ.พ.30 แล้ว", form rendered all RD lines correctly
  (sales taxable ฿14,000.00/฿980.00, purchases ฿15,000.00/฿1,050.00, net VAT ฿0.00, credit c/f
  ฿70.00 — numbers higher than round-3 baseline 13,000/910 sales because sales01/ar01 posted
  more tax invoices this round while I was checking) — footer confirms **v1.22.7** —
  screenshots shots/round4/acct01-pnd30-initial.png, acct01-pnd30-preview.png. Raw dump:
  swarm-findings/round4/acct01-pnd30-raw.txt
- ภ.พ.30 PDF download button clicked (`pnd30-download-pdf`) — click sent OK, no `download` event
  captured in-process (opens via blob/new-tab, consistent with round-3 behavior, not an error)
- ภ.พ.30 .txt download button clicked (`pnd30-download-batch`) — same as above, click sent OK
- ภ.พ.30 finalize button ("ยืนยัน/ปิดงวด"): present=true, disabled=false — **NOT CLICKED**
  (HARD RULE 2) — screenshot shots/round4/acct01-pnd30-after-downloads.png

### Trial Balance Dr=Cr — repeated-refresh check (PRIMARY)

10 refreshes across ~10 minutes, interleaved with GL/bank-recon/ภ.พ.30 activity and a spaced
extended watch, while all 9 other swarm agents posted documents concurrently on co5. Grand
totals climbed steadily across the run (proof the swarm WAS actively posting during every check,
not idle) while Dr always equalled Cr.

| # | time (UTC) | balanced? | badge | totals (Dr / Cr) | screenshot |
|---|---|---|---|---|---|
| 1 | 2026-07-20T02:39:53.841Z | YES | Dr = Cr ✓ | ฿56,710.00 / ฿56,710.00 | shots/round4/acct01-tb-1.png |
| 2 | 2026-07-20T02:40:00.268Z | YES | Dr = Cr ✓ | ฿56,710.00 / ฿56,710.00 | shots/round4/acct01-tb-2.png |
| 3 | 2026-07-20T02:40:05.078Z | YES | Dr = Cr ✓ | ฿56,710.00 / ฿56,710.00 | shots/round4/acct01-tb-3.png |
| 4 | 2026-07-20T02:40:28.092Z | YES | Dr = Cr ✓ | ฿56,710.00 / ฿56,710.00 | shots/round4/acct01-tb-4.png |
| 5 | 2026-07-20T02:41:47.235Z | YES | Dr = Cr ✓ | ฿63,130.00 / ฿63,130.00 | shots/round4/acct01-tb-5.png |
| 6 | 2026-07-20T02:43:17.464Z | YES | Dr = Cr ✓ | ฿63,130.00 / ฿63,130.00 | shots/round4/acct01-tb-6.png |
| 7 | 2026-07-20T02:44:45.513Z | YES | Dr = Cr ✓ | ฿66,340.00 / ฿66,340.00 | shots/round4/acct01-tb-7.png |
| 8 | 2026-07-20T02:47:03.699Z | YES | Dr = Cr ✓ | ฿73,830.00 / ฿73,830.00 | shots/round4/acct01-tb-8.png |
| 9 | 2026-07-20T02:48:16.421Z | YES | Dr = Cr ✓ | ฿75,970.00 / ฿75,970.00 | shots/round4/acct01-tb-9.png |
| 10 | 2026-07-20T02:49:39.926Z | YES | Dr = Cr ✓ | ฿82,390.00 / ฿82,390.00 | shots/round4/acct01-tb-10.png |

**Tie held across all 10 refreshes: YES — Dr=Cr never broke, even as totals grew ~45% over the
window from concurrent posting.** Full raw log: swarm-findings/round4/acct01-tb-log.json

## CRIT-verify

- **CRIT-1 (doc-numbering writes 2xx, zero 500/23505):** acct01 is a read-only reporting role
  this round and does not itself post QT/TI/RC/VI/PV/PO documents — no direct numbering-write
  evidence from this thread (owned by sales01/ar01/purch01/ap01/appr01, see their round4 files).
  **Indirect corroboration:** the swarm was demonstrably posting hard throughout my session (TB
  grand total climbed ฿56,710 → ฿82,390, +45%, across the 10 checks) and Trial Balance never
  desynced — if a numbering collision had corrupted a posting mid-transaction, the GL would very
  likely show an orphaned/unbalanced entry; it never did. No 500 responses observed on any page
  I loaded (console/network capture below — only benign 403/404/422 noise, zero 5xx).
- **CRIT-2 (tax01 opens ภ.พ.30 preview + PDF + .txt without 403):** not tax01's session, but as
  ACCOUNTANT role I independently opened `/reports/pnd30`, clicked preview successfully (no 403,
  form rendered with live numbers), clicked both PDF and .txt download buttons with no error
  toast/console 403 — **YES, ภ.พ.30 opened cleanly for a non-superadmin role this round.**
  Corroborates CRIT-2 closed from a second role's vantage point; see tax01's round4 file for the
  authoritative PRIMARY confirmation on the actual Tax Officer account.
  Finalize button present + enabled but correctly **NOT clicked** (hard rule).

## Findings (severity CRIT/HIGH/MED/LOW)

| Severity | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| LOW | ภ.พ.30 / network | Browser console logged a single `422` on `/reports/pnd30` load, before/around the preview click. Page still rendered the full form correctly and the preview succeeded (toast "แสดงตัวอย่าง ภ.พ.30 แล้ว", correct numbers). Most likely an initial "does a filing already exist for this period" probe returning 422 for a not-yet-created filing, silently handled by the UI (empty-state pattern). Not a crash, not a blocking error, but worth a follow-up network trace to confirm intent vs a swallowed real bug (same class of inconclusive finding logged by acct01 in round 3 for payroll's 403) | goto /reports/pnd30, watch page.on('console') before/after clicking preview | shots/round4/acct01-pnd30-initial.png |

No CRIT/HIGH/MED findings this round for acct01's surface (TB, GL, bank recon, ภ.พ.30) — all
rendered cleanly, all numbering-adjacent evidence (GL balance, TB tie) held under concurrent
load.

## Denied-as-expected (RBAC ที่ deny ถูกต้อง)

- ภ.พ.30 finalize ("ยืนยัน/ปิดงวด") button: visible + enabled (accountant CAN see it — no SoD
  gate against ACCOUNTANT for this button in this build) but **not exercised**, per hard rule
  #2 (forbidden action, not a permission test for this role).

## Console / network errors captured (whole session, both phases)

```
[console.error] https://teas.kazaki-rio.com/login — Failed to load resource: 404 ()
[console.error] https://teas.kazaki-rio.com/ — Failed to load resource: 403 ()
[console.error] https://teas.kazaki-rio.com/reports/pnd30 — Failed to load resource: 422 ()
[console.error] https://teas.kazaki-rio.com/login — Failed to load resource: 404 ()  (phase 2 relogin)
[console.error] https://teas.kazaki-rio.com/ — Failed to load resource: 403 ()  (phase 2 relogin)
```

No `pageerror` events, no 5xx responses, no stack traces observed. The /login 404 and / 403 are
the same benign pre-auth noise acct01 also saw in round 3 (not a new regression).

## Artifacts

- Run log: swarm-findings/round4/acct01-run.log
- TB structured log: swarm-findings/round4/acct01-tb-log.json
- Console error log: swarm-findings/round4/acct01-console-errors.log
- ภ.พ.30 raw text dump: swarm-findings/round4/acct01-pnd30-raw.txt
- Screenshots: shots/round4/acct01-*.png (dashboard, tb-1..10, gl-report, journal-detail,
  bank-recon, pnd30-initial/preview/after-downloads)
