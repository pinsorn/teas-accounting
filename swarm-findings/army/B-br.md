# B-br — Bank Reconciliation FULL flow, co5, prod v1.22.10

Driver: acct01 (`bank.reconcile` + `bank.statement.import` confirmed present — no admin01
fallback needed). Playwright headless (msedge channel), temp scripts
`frontend/army-B-br*.mjs` — all deleted after the run per hard rules. Raw log:
`swarm-findings/army/B-br-run-log.txt`. Screenshots: `swarm-findings/army/B-br-*.png`.

## Done
- [x] Part 1 — existing "Parsed" import (`kbiz-statement-co5-jul2026.csv`, id=1): full
      unmatch → suggest → confirm → unmatch → reconfirm → **reload** cycle, driven twice
      through the confirm/unmatch pair, state verified to survive a hard page reload.
- [x] Part 2 — NEW CSV import (KBiz variant), self-built synthetic fixture (id=2,
      `B-br-kbiz-new.csv`, 3 txns), matched against REAL existing co5 posted docs (no
      helper doc needed — co5 already had enough unclaimed Posted Receipts/PVs). Exercised
      suggest→confirm, suggest-but-leave-unconfirmed, and the empty-suggestions state.
- [x] Part 3 — K-Plus PDF variant: real sample FOUND in repo (gitignored, not committed —
      see Findings), attempted with the documented password. **Uncovered a real 500 bug**
      (see Finding 1) — not a BLOCKED-no-sample case.
- [x] Part 4 — reconciliation journal / tie-out report (`/reports/bank-reconciliation`):
      loaded, auto-selected the single bank account, difference badge + breakdown verified
      against the raw API math.
- [x] No tenant leak (co2/co3 data) observed on the acct01 dashboard.
- [x] Blast cap respected: 2 new imports (CSV + the rejected PDF attempt, which created
      NO row), 0 new helper docs (reused existing posted Receipts/PVs).

## Evidence

### Part 1 — existing import cycle (bank account #1, import #1)
Both statement lines started life already `Matched` (Line1 RCV00001 MoneyIn ฿7,490.00 ↔
Receipt `07-2026-RC-0001`; Line2 PAY00001 MoneyOut ฿10,700.00 ↔ PV `07-2026-PV-COGS-0001`
— these look like seed/demo data set up when the feature originally shipped). Drove the
full required cycle on Line1:
1. `B-br-04-line1-unmatched-baseline.png` — unmatch to reach a known Unmatched baseline
   (`ยกเลิกจับคู่` → `ConfirmActionDialog`/AlertDialog `alert-dialog-confirm` → `unmatchSuccess` toast).
2. `B-br-05-line1-suggest-modal.png` — suggest correctly re-surfaces the now-unclaimed
   Receipt `07-2026-RC-0001` (exact amount + same-day match).
3. `B-br-06-line1-confirmed.png` — confirmed, status → `Matched`.
4. `B-br-07-line1-unmatched-again.png` — unmatched a 2nd time.
5. `B-br-08-line1-reconfirmed.png` — suggest → confirm again, status → `Matched`.
6. `B-br-09-existing-after-reload.png` — **hard page reload**, both lines still read
   `Matched` with the unmatch action available — state genuinely persisted server-side,
   not a client cache artifact.

Minor script-timing note (not a product bug): the synchronous status read taken
immediately (500ms) after the 2nd unmatch's dialog closed still printed "Matched" once —
a stale React-Query read, not the DB truth: the very next step's `suggest-open` click
succeeded (only rendered on an Unmatched row), proving the unmatch had actually landed
by then. Future scripts should poll/wait for the row text to change rather than a fixed
timeout.

### Part 2 — new CSV import (KBiz variant)
Self-built a synthetic KBiz-format CSV (UTF-8 BOM, same 13-column/metadata-by-label
structure as `backend/tests/Accounting.Api.Tests/Bank/KBizCsvAdapterTests.cs`'s `GoodCsv`
fixture), account no. `123-4-56789-0` (co5's real dummy account, digit-matched), period
01/07–22/07/2026 (deliberately overlapping the existing import — see below), 3 txns:
- Line A: MoneyIn ฿2,140.00, 2026-07-19 → real target: existing Posted Receipt
  `07-2026-RC-0002` (co5 already had one, no helper doc needed).
- Line B: MoneyOut ฿3,210.00, 2026-07-19 → real target: existing Posted PV
  `07-2026-PV-COGS-0002`. Left **deliberately unconfirmed** to populate the
  reconciliation report's unmatched/outstanding sections for Part 4.
- Line C: MoneyIn ฿500.00, 2026-07-21 → **no matching doc anywhere** (edge case).

`B-br-10/11` — import modal + success (`import-count` 1→2, `B-br-kbiz-new.csv` listed
"Parsed", 3 รายการ). `B-br-12-new-import-lines-initial.png` — all 3 lines Unmatched.
`B-br-13/14` — suggest found Receipt `07-2026-RC-0002` exactly, confirmed → Matched.
`B-br-15-new-lineB-suggest-modal-left-unconfirmed.png` — suggest correctly found PV
`07-2026-PV-COGS-0002` (฿3,210.00); closed without confirming (by design).
`B-br-16-new-lineC-no-suggestions.png` — modal correctly shows the empty state
(`ไม่พบใบเสร็จรับเงิน/ใบสำคัญจ่ายที่ตรงกัน`) for the ฿500 line with no matching doc.

### Part 3 — K-Plus PDF variant (see Finding 1 — real bug, not BLOCKED)
Sample found at repo root: `STM_SA5476_01FEB26_08JUL26.pdf` (17-page real K-Plus/K PLUS
statement, password `06121996`, gitignored `STM_*.pdf` — never committed, per
`PROGRESS-cycle-b.md`/`PROGRESS-bank-reconciliation-b3.md`). Used exactly as the
dispatch instructed. `B-br-17-kplus-pdf-import-modal-filled.png` shows the file + masked
password entered. Result: **`B-br-18-kplus-pdf-import-result.png`** — a raw
"An unexpected error occurred." toast, not a clean domain error. Confirmed via direct API
probes (see Finding 1).

### Part 4 — reconciliation report
`B-br-19-reconciliation-report-autoselected.png` + raw API
(`GET /bank-accounts/1/reconciliation?from=2026-07-01&to=2026-07-22`):
```
statementClosingBalance: -570.00   (from the NEW CSV import — its period end 07-22 is the
                                     latest one <= `to`; the original import's period end
                                     07-31 is excluded by that same rule — correct per the
                                     documented "latest applicable import" design, not a bug)
glBalance:                8,090.00
depositsInTransitTotal:  14,980.00  (14 unmatched Posted receipts, unrelated pre-existing
                                     demo docs @ ฿1,070 + others — correctly EXCLUDES the
                                     now-matched Receipts #7/#8)
outstandingPaymentsTotal: 4,250.00  (2 items: our PV #7 ฿3,210 left unconfirmed + a
                                     pre-existing unmatched PV ฿1,040)
unmatchedLinesNet:       -2,710.00  (our 2 deliberately-left-unmatched statement lines:
                                     -3,210 + 500)
difference:               4,780.00  — verified by hand: -570 - (8090 - 14980 + 4250 - 2710) = 4780 ✓
```
Badge showed "มีรายการยังไม่กระทบยอด — ดูรายละเอียดด้านล่าง" (`diffUnreconciledBadge`,
round-5's added badge) since `difference != 0` and imports exist for the account.
Single-bank-account **autoselect** (round-5's other addition) fired correctly — no manual
picker interaction needed. **Yes, the diff explains itself**: every reconciling item
(both unmatched statement lines, both outstanding docs) is individually listed in the
report's three breakdown tables with description/date/amount, not just a lump sum.

## Findings

### Finding 1 — HIGH: K-Plus PDF import 500s on a real multi-page statement with the CORRECT password
`POST /api/proxy/bank-accounts/1/imports` with `STM_SA5476_01FEB26_08JUL26.pdf` +
password `06121996` (the actual correct password) returns:
```
HTTP 500  {"type":"urn:teas:error:internal_error","title":"internal_error","status":500,"detail":"An unexpected error occurred."}
```
This is a raw, unmapped exception (HARD RULE 3: any 500 = finding). **Isolated the bug to
the parse/assembly path, not password handling** — re-probed the same endpoint/file with
a wrong password and with no password at all; both correctly return a clean, designed
error:
```
HTTP 422  {"type":"urn:teas:error:bank.pdf_password", ..., "detail":"Could not open the statement PDF — check the password."}
```
So `KPlusPdfTextExtractor`'s password/decrypt handling works exactly as designed
(`PROGRESS-bank-reconciliation-b3.md` §Next item 2). The crash is somewhere further down
the pipe — most likely `KPlusPdfLineAssembler` (column/row derivation) or the
`BankStatementIntegrity`/account-mismatch check choking on this REAL statement's actual
layout, which the adapter's existing tests (`KPlusPdfLineAssemblerTests.cs`) only cover
with hand-built synthetic `PositionedWord` arrays — never a real 17-page PDF end-to-end.
`PROGRESS-bank-reconciliation-b3.md` already flagged one known soft edge (channel/detail
column boundary, said to be cosmetic-only) — this 500 suggests either that edge is not as
cosmetic as believed, or there's a separate unhandled case (e.g. a page/row shape B3's
synthetic fixtures didn't cover). Root cause not further dug into here (out of this leg's
browser-testing scope, no source reads/edits attempted beyond what's in this report).
**Repro:** login any co5 user with `bank.statement.import`, `POST
/api/proxy/bank-accounts/1/imports` multipart `file=STM_SA5476_01FEB26_08JUL26.pdf` (or
any real multi-page K PLUS export) + `password=06121996`. Screenshot:
`B-br-18-kplus-pdf-import-result.png`. Note: this sample is real personal bank data,
deliberately gitignored (`STM_*.pdf`) and never committed — do not commit it; a debugger
needs the same local file (already present at repo root, per Ham) or another real K PLUS
export to repro.

### Finding 2 — LOW / not a bug, worth noting
The reconciliation report's `statementClosingBalance` is sourced from "the LATEST import
whose `PeriodEnd <= to`" (by design, per code comment in
`BankReconciliationReportService.cs`). When two imports exist with different period ends
(our new CSV ends 07-22, the original ends 07-31) and `to` sits between them, the OLDER
(chronologically first-imported) statement's closing balance is silently ignored in favor
of whichever import's period happens to end earlier-but-still-≤-`to`. This is intentional
and documented, not a defect — flagging only because it could surprise a user comparing
against the wrong statement if they don't read the date range carefully. No action
requested.

## Unbuilt-vs-untested classification
Everything in scope for B-br is **built and testable** — suggest/confirm/unmatch/reload,
CSV import, PDF import (adapter code exists and IS wired/live), and the reconciliation
report are all real, shipped features. Finding 1 is a genuine **defect in a shipped
feature** (K-Plus PDF adapter), not an unbuilt gap.
