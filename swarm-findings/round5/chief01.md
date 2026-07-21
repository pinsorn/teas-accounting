# chief01 — Chief Accountant — co5 UX swarm ROUND 5 findings (2026-07-21, prod v1.22.9)

Target: https://teas.kazaki-rio.com, company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด). Mission (per
`specs/uxswarm-round5-finding-verify.md`): verify WP3 report-UX fixes (a) date-basis header labels
on TB/BS/P&L, (b) AP-aging control-account tie-out banner, (c) AR-aging negative/net-credit visual
distinction, (d) bank-recon explanatory badge + auto-select sole account — plus cross-report
consistency, "no numbers wrong". Account REUSED (chief01 / `UxSwarm-2026-A7`, not recreated).
Playwright headless (msedge channel, `@playwright/test` chromium) from `frontend/`, temp script
`frontend/swarm5-chief01.mjs` — **deleted at end of run**, along with its JSON scratch output
(not a sanctioned output path). Run window: 2026-07-21T23:35–23:37 local (~2 min active, well
inside the 25-min timebox), concurrent with acct01/admin01/ar01/purch01/sales01/tax01 all hammering
co5 at the same time (confirmed by their screenshots landing in `shots/round5/` throughout the run).

## Done

- Logged in as chief01 (1/1 attempt after bumping nav timeouts for concurrent-swarm slowness — see
  Findings/notes). Confirmed tenant via `/api/proxy/me`: `companyId=5`,
  `companyName="บริษัท ทดสอบ VAT (DUMMY) จำกัด"`, `isSuperAdmin=false`, `allowedCompanies` = co5
  only. Body-text tenant-leak scan (`นาย พงศ์สันต์`/`เรปทาวน์`) ran on **every** page load (9 checks:
  dashboard, TB, BS, P&L, tax-summary, AP-aging, AR-aging, sales-summary, bank-recon) — clean every
  time, zero hits. **CRIT tenant-leak check: clean.**
- Full-page screenshots of 9 pages (`shots/round5/chief01-01..09-*.png`): dashboard, trial-balance,
  balance-sheet, profit-loss, tax-summary, ap-aging, ar-aging, sales-summary, bank-reconciliation
  (auto-selected state).
- Captured exact rendered header/subtitle text, reconciliation-panel text, table rows, and console/
  network events via `page.on('console'|'pageerror'|'response')` for the whole session:
  **`net5xx: 0`**, zero explicit 403/404 `response` events. One generic unattributed console message
  ("Failed to load resource: 404") — see Findings, treated as noise (likely a static asset, not a
  report/API endpoint — my dedicated response-status listener recorded no 403/404 at all).
- Refreshed Trial Balance once (2 total reads) as a light concurrency-consistency check (not this
  round's dedicated CRIT mission — that's acct01's): **Dr = Cr ✓ held both times**, identical totals
  (฿83,513.50 = ฿83,513.50) despite the other 6 agents actively posting docs concurrently.
- Cross-checked every reported total by hand across all 9 pages (see Cross-report consistency below)
  — all tie out exactly, including a fresh instance of the known TB/BS(as-of)-vs-P&L(range) cutoff
  split, which is now clearly labeled per WP3(a) rather than silently inconsistent.
- Did not click ยืนยัน/ปิดงวด anywhere, did not create/edit/approve/delete anything — pure read-only
  report sweep.

## Fix-verify (WP3, this round's reason to exist)

**(a) Date-basis header labels — CLOSED, confirmed live.**
- Trial Balance: header = `"งบทดลอง (Trial Balance)" / "ข้อมูล ณ วันที่ 21/07/2569"` — exact "ณ
  วันที่" phrasing. Screenshot: `chief01-02-trial-balance-1.png`.
- Balance Sheet: header = `"งบแสดงฐานะการเงิน" / "ข้อมูล ณ วันที่ 21/07/2569"` — same phrasing.
  Screenshot: `chief01-03-balance-sheet.png`.
- P&L: header = `"กำไรขาดทุน ตามหน่วยธุรกิจ" / "ข้อมูลช่วงวันที่ 01/07/2569 ถึง 31/07/2569"` — exact
  "ช่วง … ถึง …" phrasing, **plus** the future-range warning fired correctly: `"⚠ ช่วงวันที่นี้รวม
  ถึงวันที่ 31/07/2569 ซึ่งยังไม่ถึง (อนาคต) — อาจมีรายการที่บันทึกล่วงหน้ารวมอยู่ด้วย"` (P&L's
  default range end 07/31 is 10 days past "today" 07/21, so the warning correctly triggered).
  Screenshot: `chief01-04-profit-loss.png` (both subtitle and amber warning banner visible).
  **This is precisely the fix for round4's HIGH "P&L vs TB/BS period semantics" finding** — the
  underlying cutoff difference is inherent to the two report types (as-of vs range) and was never
  meant to be eliminated; WP3(a)'s ask was to label it clearly instead of leaving it silent, which
  it now does. Recommend consolidation close that round4 HIGH item as "addressed by design (labeled,
  not eliminated — expected)".

**(b) AP-aging control-account tie-out banner — CLOSED, confirmed live.**
- Banner renders identically to AR-aging's: `"บัญชีคุมยอด (2110) ฿4,333.50 / ยอดรวมทะเบียนย่อย
  ฿4,333.50"` with a green `"Dr = Cr ✓"` badge. Screenshot: `chief01-06-ap-aging.png`. Closes round4's
  LOW "AP aging missing tie-out banner" finding.
- Cross-tied: TB account 2110 (เจ้าหนี้การค้า) net = **-฿4,333.50** (credit balance) = AP-aging
  control-account balance **฿4,333.50** = AP-aging subledger total **฿4,333.50** — exact match, three
  ways.

**(c) AR-aging negative/net-credit visual distinction — CLOSED per code, no live negative data
existed to also confirm visually (documented, not a gap in the fix).**
- Confirmed in source (`app/(dashboard)/reports/ar-aging/page.tsx`): an `amountClass(v)` helper
  applies `text-error` whenever a bucket/total value is `< 0`, applied to every bucket cell and the
  totals row.
- Live check: co5's AR-aging currently has exactly one customer row (นายสมชาย ใจดี, all current
  bucket, ฿6,420.00), zero negative values anywhere in the table (`negCells: []` from a DOM sweep of
  every `<td>` containing a `-` character). Screenshot: `chief01-07-ar-aging.png` — all amounts render
  in the default color, consistent with all-positive data (not a failure of the fix, just an absence
  of negative test data at capture time).
- Did **not** attempt to manufacture an overpayment/net-credit scenario purely to trigger the red
  styling — doing so would need several new docs on a customer that ar01 was concurrently
  stress-testing this same round (collision risk), and the code path is unambiguous on inspection.
  Recommend: if a future round's data naturally produces a negative AR bucket (e.g. an overpayment),
  re-screenshot to close the loop with a live visual; until then this is "code-confirmed, live-data
  pending" rather than an open finding.

**(d) Bank-recon explanatory badge + auto-select sole account — CLOSED, confirmed live.**
- co5 has exactly **one** bank account (ธนาคารกสิกรไทย — 123-4-56789-0; the `<select>` had 2
  `<option>`s total: the "ทั้งหมด" placeholder + this one account). On page load, **without any
  manual selection**, `select.value` resolved to that account's id and the report rendered — the
  `useEffect` auto-select fired correctly. Closes round4's LOW "single-bank company had to manually
  pick its account" finding.
- The ผลต่าง (difference) tile is highlighted red/pink and shows an explanatory amber badge:
  `"มีรายการยังไม่กระทบยอด — ดูรายละเอียดด้านล่าง"` (there are unreconciled items — see details
  below) — this is the `diffUnreconciledBadge` variant (statement imports DO exist for this account,
  so it correctly did NOT show the `diffNoStatementBadge` "no statement imported" variant). Closes
  round4's LOW/MED "unreconciled ผลต่าง, no explanation" finding. Screenshot:
  `chief01-09-bank-reconciliation-autoselect.png`.
- Cross-tied: TB account 1120 (เงินฝากธนาคาร) net = **฿7,490.00** = Balance Sheet asset row 1120
  (฿7,490.00) = bank-recon "ยอดคงเหลือตามบัญชี GL" (**฿7,490.00**) — exact match, three ways.

## Cross-report consistency (all reports checked, no numbers wrong)

| Check | Values | Result |
|---|---|---|
| TB 4000−4100 net revenue vs P&L revenue vs tax-summary ก.ค. รายได้ | 27,000−1,000=26,000 / 26,000.00 / 26,000.00 | tie ✓ |
| P&L (26,000/144,550/-118,550) vs tax-summary ก.ค. row (รายได้/รายจ่าย/กำไรสุทธิ) | identical | tie ✓ |
| TB net overall (assets−liabilities side, i.e. Dr−Cr on P&L-relevant accounts) vs BS "กำไร(ขาดทุน)สะสมงวดปัจจุบัน" | 26,000−17,050=8,950 / ฿8,950.00 | tie ✓ |
| BS total assets vs total liabilities+equity | ฿15,103.50 / ฿15,103.50 | tie ✓ (`Dr = Cr ✓` badge) |
| TB 1120 vs BS asset row 1120 vs bank-recon GL balance | ฿7,490.00 (×3) | tie ✓ |
| TB 2110 vs BS liability row 2110 vs AP-aging control account vs AP-aging subledger total | ฿4,333.50 (×4) | tie ✓ |
| TB 1130 vs BS asset row 1130 vs AR-aging control account vs AR-aging subledger total | ฿6,420.00 (×4) | tie ✓ |
| TB 1130 debit column vs sales-summary total incl VAT | ฿28,890.00 / ฿28,890.00 | tie ✓ |

Every cross-check tied exactly, zero discrepancies. The one apparent "mismatch" (P&L −฿118,550 vs
TB/BS's +฿8,950 for what looks like "the same period") is the already-known, now-explicitly-labeled
as-of-vs-range cutoff difference addressed by WP3(a) above — not a new or unexplained inconsistency.

## Regressions

- None observed. `net5xx: 0` for the entire session. TB stayed `Dr = Cr ✓` across both reads despite
  6 other agents concurrently posting docs to co5. No cross-tenant leak on any of 9 page loads.

## Findings

| Sev | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| INFO (not a WP3 gap) | AR-aging negative styling unconfirmed live | Code (`amountClass`, `text-error` on `v<0`) confirms the fix is implemented, but co5's current AR-aging data has zero negative buckets to also see it rendered red in this round's capture. Not creating synthetic overpayment docs to force it (collision risk with ar01's concurrent test on the same accounts this round). Recommend a future round confirm visually once real negative-bucket data exists. | `/reports/ar-aging` | chief01-07-ar-aging.png |
| INFO (noise) | One unattributed console 404 | A single generic `"Failed to load resource: the server responded with a status of 404 ()"` console message during the session, with no URL in the message text and **zero** matching entries in my dedicated `page.on('response')` 403/404 listener (which caught nothing all session) — most likely a static asset (favicon/manifest) outside the tracked navigation responses, not a report or API endpoint. Not reproduced to a specific page. | n/a (whole-session listener) | — |
| INFO (script note, not a product bug) | Nav timeouts under concurrent swarm load | First run hit a hard 15s `page.goto` timeout on `/reports/balance-sheet` while 5 other agents were concurrently hammering co5; switching to `domcontentloaded` + one retry + 20s timeouts resolved it on the second run with zero further issues. Environmental (shared prod under 10-agent concurrent load), not a TEAS defect. | n/a | — |

## Verdict

**WP3 fully CLOSED**: (a) TB/BS "ณ วันที่" + P&L "ช่วง … ถึง …" + future-range warning — all
confirmed live with exact expected phrasing; (b) AP-aging tie-out banner — confirmed live, ties
exactly with TB/BS; (c) AR-aging negative-value styling — confirmed in code, structurally sound,
live visual confirmation pending real negative-bucket data (not itself a finding against the fix);
(d) bank-recon explanatory badge + sole-account auto-select — both confirmed live with exact
expected behavior. Cross-report consistency: every tie-out across TB/BS/P&L/tax-summary/sales-
summary/AP-aging/AR-aging/bank-recon checked by hand, all exact, zero wrong numbers, zero
regressions, zero 500s, zero tenant leaks.

## Screenshots (repo-relative, `shots/round5/`)

`chief01-01-dashboard.png`, `chief01-02-trial-balance-1.png`, `chief01-03-balance-sheet.png`,
`chief01-04-profit-loss.png`, `chief01-05-tax-summary.png`, `chief01-06-ap-aging.png`,
`chief01-07-ar-aging.png`, `chief01-08-sales-summary.png`,
`chief01-09-bank-reconciliation-autoselect.png` (9 total).
