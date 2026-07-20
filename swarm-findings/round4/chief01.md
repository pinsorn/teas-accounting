# chief01 — Chief Accountant — co5 UX swarm ROUND 4 findings (2026-07-20, prod v1.22.7)

Target: https://teas.kazaki-rio.com, company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด). Mission (per
dispatch): sweep every report, cross-report consistency, re-confirm the known TB/BS(as-of) vs
P&L(full-month) cutoff mismatch (**not fixed this round — expected, out of scope**). Account
REUSED (chief01 / `UxSwarm-2026-A7`, not recreated). Playwright headless (msedge channel,
`@playwright/test` chromium) from `frontend/`, temp script `frontend/swarm4-chief01.mjs` —
**deleted at end of run**. Zero mutations attempted anywhere. Run window: 2026-07-20T02:39:32Z –
02:41:49Z (~2.3 min active, well inside the 25-min timebox), concurrent with the other 9 round-4
agents hammering co5.

## Done

- Logged in as chief01 (1/1 attempt, immediate). Confirmed tenant via `/api/proxy/me`:
  `companyId=5`, `companyName="บริษัท ทดสอบ VAT (DUMMY) จำกัด"`, `isSuperAdmin=false`. Body-text
  tenant-leak scan (`นาย พงศ์สันต์`/`เรปทาวน์`) ran on **every** page load (18 checks total) —
  clean every time. **CRIT tenant-leak check: clean.**
- `/api/proxy/me/permissions`: role `CHIEF_ACCOUNTANT`, **74 grants** — identical count to round 3,
  same broad-authority SoD note stands (see Findings).
- Full-page screenshot + text dump of **11 reports**: Trial Balance, P&L, Balance Sheet, Sales
  Summary, Tax Summary, AR Aging, AP Aging, Bank Reconciliation (KBANK/กสิกรไทย selected), General
  Ledger (default + a follow-up with account **1120** explicitly selected — see cross-report note
  below), **and 2 bonus reports not in round 3's chief01 scope**: PO ค้าง (outstanding-po), ภาษี
  หัก ณ ที่จ่ายค้างรับ (wht-receivable).
- Read the actual date-filter **input values** on every report to reconfirm the cutoff split with
  hard evidence, same methodology as round 3.
- Refreshed Trial Balance **5×** while the other 9 agents hammered co5 concurrently: **Dr = Cr ✓
  held on all 5** (`badge-success`, no exceptions this round — smoother than round 3, which saw
  client-side timeouts on 2/3/4). Zero timeouts, zero errors.
- Cross-checked every reported total by hand (TB↔BS, TB↔sales-summary/AR-aging/AP-aging,
  TB↔P&L via 4000−4100, P&L↔tax-summary) — **all tie out**, plus a new granular tie via GL account
  1120 (see below). Also cross-checked against **5 other agents' round-4 raw evidence**
  (ar01.md, tax01.md, audit01.md, purch01-results.json, acct01-run.log/console-errors.log) still on
  disk at the time of this write-up, per the dispatch's "sweep all reports; cross-report
  consistency" instruction.
- Probed 4 admin/restricted routes: `/settings/users`, `/settings/roles`, `/settings/companies`,
  `/settings/api-keys` — all deny correctly (see Denied-as-expected).
- Read-only payroll probe: `/payroll` loaded, 3 runs visible (07/2026, 08/2026, 09/2026, all
  `จ่ายแล้ว`) — **not** touched.
- Did not click ยืนยัน/ปิดงวด anywhere, did not create/edit/approve/delete anything.
- `page.on('response'|'console'|'pageerror')` listeners active for the entire session on every
  page: **`net5xx: 0`** across both script invocations (23 screenshots, 6 console errors, 1
  pageerror — all individually accounted for below, none is a 500).

## PRIMARY re-confirmation: TB/BS-vs-P&L cutoff mismatch — **STILL PRESENT, unchanged in v1.22.7**

Confirmed still standing, same root cause as round 2/3 (future-dated payroll runs already flagged
"paid"), one day closer to "today" than round 3 (system date advanced 07-19→07-20):

| Report | Date-filter input value(s) | Semantics |
|---|---|---|
| `/reports/trial-balance` | `2026-07-20` | ณ วันที่ = **today** |
| `/reports/balance-sheet` | `2026-07-20` | ณ วันที่ = **today** |
| `/reports/ar-aging` | `2026-07-20` | ณ วันที่ = **today** |
| `/reports/ap-aging` | `2026-07-20` | ถึงวันที่ = **today** |
| `/reports/profit-loss` | `2026-07-01` / `2026-07-31` | **full current month** |

- P&L (`2026-07-01`→`2026-07-31`): revenue ฿14,000.00, expense **฿142,500.00**, net
  **-฿128,500.00**. Independently confirmed by `/reports/tax-summary`'s ก.ค. row at the same
  moment (both captured 02:39:40–02:39:52Z): revenue ฿14,000.00, รายจ่าย ฿142,500.00, กำไรสุทธิ
  -฿128,500.00 — **exact match**, same pairing round 3 found.
- TB (as of `2026-07-20`, at capture time 02:39:36Z): posted expense accounts 5000 COGS ฿5,000 +
  5200 services ฿10,000 = ฿15,000 (salary 5400 = ฿0). Revenue side: 4000 ฿15,000 Cr − 4100 (returns)
  ฿1,000 Dr = ฿14,000 net — ties exactly to P&L's revenue figure. TB net = 14,000−15,000 =
  **-฿1,000.00**, matching BS's "กำไร(ขาดทุน)สะสมงวดปัจจุบัน -฿1,000.00" exactly.
- `/payroll` still shows the same **3 runs already `จ่ายแล้ว`** with future pay dates: 07/2026 →
  2026-07-30 (**10 days ahead** of today), 08/2026 → 2026-08-30 (**41 days ahead**), 09/2026 →
  2026-09-29 (**71 days ahead**) — one day closer to each than round 3 reported (11/42/72), exactly
  as expected since "today" advanced by 1 day. Tax-summary's yearly table still shows the same
  inflated-expense pattern continuing into ส.ค./ก.ย. (฿127,500.00 each month).

**Verdict: confirmed still standing, unchanged from round 2/3. Not fixed this round (expected,
out of scope). Same root cause, same blast radius (July/Aug/Sep).**

## Cross-report consistency (this round's extra mission)

- **New granular tie-out (GL by account):** pulled General Ledger for account **1120** (bank)
  explicitly (the default GL view with no account selected correctly shows "ไม่มีข้อมูล" — that's
  expected UI behavior, an account must be typed into the datalist first, not a bug). The GL-1120
  running total (Dr ฿11,770.00 / Cr ฿13,910.00, ending -฿2,140.00) did **not** match TB/bank-recon's
  1120 figure captured ~2 minutes earlier (Dr ฿9,630.00 / Cr ฿13,910.00, -฿4,280.00) — **explained
  precisely, not a bug**: the GL-1120 pull (02:41:49Z) picked up two JV entries dated 20 ก.ค. 2569
  (`07-2026-JV-0015` Receipt 07-2026-RC-0001 +฿1,070.00, `07-2026-JV-0018` Receipt 07-2026-RC-0002
  +฿1,070.00 — together +฿2,140.00) that landed **after** the TB/bank-recon snapshot (02:39:36Z /
  02:40:04Z) but before the GL follow-up. ฿11,770 − ฿9,630 = ฿2,140.00 = exactly the sum of those
  two entries; Cr side (฿13,910) matched unchanged in both because no new credits landed in the
  window. This is airtight arithmetic evidence that concurrent postings from the swarm are landing
  **cleanly and immediately reflected** with zero data corruption — supporting evidence for CRIT-1,
  not a new inconsistency. Flagging so consolidation doesn't mistake a ~2-minute-apart snapshot
  drift for a genuine cross-report bug.
- **Cross-checked against 5 other round-4 agent reports** on disk at write-up time
  (`swarm-findings/round4/{ar01,tax01,audit01}.md`, `purch01-results.json`,
  `acct01-run.log`/`acct01-console-errors.log`): purch01's `http5xxEvents: []` (PO approve 2/2 ok,
  one 422 `po.not_draft` domain error from a genuine approve-race with appr01 — degrades cleanly,
  not a crash); acct01's TB checks #4/#5/#6 all `balanced=true` with **growing** totals (฿56,710 →
  ฿63,130 → ฿63,130) as the swarm posted, never broke; tax01's preview/PDF both 200, .txt still the
  known 422 data-completeness gap (not RBAC); audit01's 6 mutation probes (4 UI + 2 API) all clean
  403/401, zero 500s. **All five independently report `net5xx`/`http5xxEvents` of zero** for the
  same concurrency window chief01 observed — strong convergent evidence for CRIT-1.
- **One discrepancy worth flagging for consolidation**: ar01's round-4 report (`ar01.md`) headlines
  "**CRIT-1 NOT CLOSED**" based on its RC cycle 2 (TI `07-2026-TI-0002`) throwing a Playwright
  `locator.waitFor: Timeout 10000ms exceeded` while waiting for the tax-invoice picker to list
  TI-0002 on `/receipts/new`. I pulled ar01's own screenshot
  (`shots/round4/ar01-08-rc-c2-exception.png`): it shows the receipt-creation form's "ชำระสำหรับ
  ใบกำกับภาษี" (pay-against-invoice) panel simply **not listing** the invoice at screenshot time —
  no error banner, no stack trace, no HTTP status visible in-frame. ar01's own console/API error
  log for that run (20 entries) contains **zero 500s** — only 403s (dashboard-widget noise,
  consistent pattern with every other round-4 report) and 404s. In other words: this specific
  failure is a **client-side UI/data-population timing issue on the invoice-picker list**, and the
  RC POST itself never appears to have fired (blocked upstream by the inability to select an
  invoice) — it does **not** meet the spec's literal CRIT-1 closure bar ("every doc-numbering write
  ... must return 2xx, ZERO HTTP 500/23505 ... a single 500 ... = CRIT-1 NOT closed"). I'm not
  overriding ar01's classification (they drove the repro, I didn't), but flagging precisely why the
  evidence attached to it doesn't show a 500/23505 — recommend Fable's consolidation either (a)
  reclassify this as a separate finding ("receipt invoice-picker fails to list a just-posted
  invoice within 10s under concurrent load" — could be eventual-consistency lag on the picker's
  list query, or a genuine picker bug, worth its own repro) rather than reopening CRIT-1, or (b) if
  ar01 has server-side log evidence I don't have visibility into, treat that as authoritative over
  my read of the screenshot.

## CRIT-verify (this round's reason to exist)

- **CRIT-1 (doc-numbering 500/23505 under concurrency):** not directly exercised by chief01
  (read-only report sweep). Supporting evidence: **zero HTTP 500s** observed in my own session, and
  independently zero in ar01/tax01/audit01/purch01/acct01's raw logs for the same window (see
  cross-report section above) — TB's `Dr = Cr ✓` held clean across 5 refreshes (chief01) + 3 more
  (acct01) spanning the whole concurrent-posting window, growing totals, never broke. **One
  discrepancy to flag**: ar01's report headlines CRIT-1 "NOT CLOSED" from a client-side locator
  timeout with no 500 in its own evidence — see analysis above, recommend consolidation review the
  raw screenshot before accepting the "NOT CLOSED" verdict as written. Net read from chief01's
  vantage: **consistent with CRIT-1 being closed**, pending Fable's resolution of the ar01 item.
- **CRIT-2 (tax01 / ภ.พ.30 403):** not re-tested by chief01 (avoid redundant hammering, same as
  round 3). tax01's round-4 report: preview 200/200 (two samples 90s apart), PDF 200, .txt still
  422 `pp30_batch.missing_address` (known data-completeness gap, not RBAC) — **CRIT-2 CLOSED**,
  confirmed via tax01's own report, no regression from round 3.

## Findings

| Sev | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| HIGH (reconfirmed) | P&L vs TB/BS period semantics | See PRIMARY re-confirmation above — still present in v1.22.7, unchanged root cause/blast radius, payroll dates now 10/41/71 days ahead (was 11/42/72 in round 3). | `/reports/profit-loss`, `/reports/trial-balance`, `/reports/balance-sheet`, `/reports/tax-summary`, `/payroll` | chief01-03-profit-loss.png, chief01-04-balance-sheet.png, chief01-02-trial-balance-1.png, chief01-06-tax-summary.png, chief01-22-probe-payroll.png |
| MED (reconfirmed, unchanged) | `/settings/api-keys`: partial deny + React error | Deny banner text correct ("ต้องมีสิทธิ์ผู้ดูแลระบบ") but the MCP connector/OAuth section below it (endpoint URL + setup instructions for Claude/Codex/Gemini) still fully renders for non-admin chief01 instead of being gated too; page still throws `Minified React error #418` in console — same as round 2/3. | `/settings/api-keys` | chief01-21-probe-settings_api-keys.png |
| LOW (reconfirmed, unchanged) | AP aging missing tie-out banner | AR aging has the `บัญชีคุมยอด` / `Dr = Cr ✓` tie-out banner (this round: ฿5,350.00 = ฿5,350.00, tied); AP aging still has no equivalent badge for account 2110, even though the totals happen to agree with TB (2,140 = 2,140). | `/reports/ar-aging` vs `/reports/ap-aging` | chief01-07-ar-aging.png, chief01-08-ap-aging.png |
| LOW (reconfirmed, unchanged) | Bank reconciliation: unreconciled ผลต่าง, no explanation | Statement balance ฿0.00, GL balance -฿4,280.00 (at capture time), 1 deposit-in-transit ฿2,140 (RC `07-2026-RC-0002`), 1 outstanding payment ฿3,210 (PV `07-2026-PV-COGS-0002`), ผลต่าง = ฿3,210.00 — no tie-out badge like TB/BS/AR-aging have. Same shape as round 3. | `/reports/bank-reconciliation` (กสิกรไทย selected) | chief01-09-bank-reconciliation.png |
| INFO / SoD design (reconfirmed, unchanged) | CHIEF_ACCOUNTANT holds very broad authority | 74 grants, same count as round 3, still includes `tax.filing.finalize`, `gl.period.close`, `gl.year.close`, `payroll.run.pay`, PO/PV approve — one role can preview+finalize VAT, close a GL period/fiscal year, AND approve/pay purchases. Not exercised (hard rule 2). | `/api/proxy/me/permissions` (74 grants, not re-dumped to a committed file per output-only rule) | — |
| INFO (cross-report flag, not independently sev-rated) | ar01's CRIT-1 "NOT CLOSED" claim | See Cross-report consistency section above — evidence attached (screenshot + ar01's own console log) shows a client-side invoice-picker timeout, zero 500s, doesn't meet the spec's literal CRIT-1 bar. Flagged for Fable's review, not overridden. | `/receipts/new` (ar01's repro) | shots/round4/ar01-08-rc-c2-exception.png (ar01's file, referenced not duplicated) |

## Denied-as-expected

- `/settings/users` → clean deny: "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้
  (sys.user.manage) — กรุณาติดต่อผู้ดูแลระบบ"; underlying API 403. ✓
- `/settings/roles` → same pattern, `sys.role.manage`. ✓
- `/settings/companies` → clean deny: "หน้านี้สำหรับ Super Admin เท่านั้น"; resolved immediately. ✓
- `/settings/api-keys` → deny banner text correct; see MED finding above for the partial-render
  caveat on the same page (unchanged from round 2/3).
- Payroll: `/payroll` loaded read-only; no mutation buttons clicked despite the role holding
  `payroll.run.manage`/`payroll.run.pay` — treated as read-only per hard rule.
- ยืนยัน/ปิดงวด: never clicked anywhere, despite the role having `tax.filing.finalize` and
  `gl.period.close`/`gl.year.close` — SoD note above, not exercised.

## Screenshots (repo-relative, `shots/round4/`)

`chief01-01-00-dashboard.png`, `chief01-02-trial-balance-1.png`, `chief01-03-profit-loss.png`,
`chief01-04-balance-sheet.png`, `chief01-05-sales-summary.png`, `chief01-06-tax-summary.png`,
`chief01-07-ar-aging.png`, `chief01-08-ap-aging.png`, `chief01-09-bank-reconciliation.png`,
`chief01-10-general-ledger.png`, `chief01-11-outstanding-po.png`, `chief01-12-wht-receivable.png`,
`chief01-13-tb-refresh-1.png` … `chief01-17-tb-refresh-5.png`,
`chief01-18-probe-settings_users.png`, `chief01-19-probe-settings_roles.png`,
`chief01-20-probe-settings_companies.png`, `chief01-21-probe-settings_api-keys.png`,
`chief01-22-probe-payroll.png`, `chief01-23-general-ledger-1120.png` (23 total).
