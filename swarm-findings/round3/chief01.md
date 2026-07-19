# chief01 — Chief Accountant — co5 UX swarm ROUND 3 findings (2026-07-19, prod v1.22.6)

Target: https://teas.kazaki-rio.com, company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด). Mission: sweep
every report, hunt cross-report inconsistency, re-confirm the known TB/BS(as-of-today) vs
P&L(full-month incl. future payroll) cutoff mismatch (NOT fixed this round — just note). Account
REUSED from round 2 (chief01 / `UxSwarm-2026-A7`, no recreation). Playwright headless (msedge
channel, `@playwright/test` chromium) from `frontend/`, temp scripts `swarm3-chief01.mjs` +
`swarm3-chief01-recheck.mjs` — **both deleted** at end of run. Zero mutations attempted anywhere.

## Done

- Logged in as chief01, 1st attempt succeeded (2nd script's 1st attempt timed out 30s on the
  username field under swarm load — see MED finding below — 2nd attempt succeeded immediately).
- Confirmed tenant via `/api/proxy/me`: `companyId=5`, `companyName="บริษัท ทดสอบ VAT (DUMMY)
  จำกัด"`, `isSuperAdmin=false`. Swept every report + probe page and **found no data belonging to
  any other company** anywhere. **CRIT tenant-leak check: clean.**
- `/api/proxy/me/permissions`: role `CHIEF_ACCOUNTANT`, **74 grants** — see INFO/SoD finding below,
  this role can singlehandedly preview+finalize VAT, close a GL period/fiscal year, and
  approve+pay PO/PV. None of that authority was exercised (forbidden).
- Full-page screenshot + text dump of all 9 reports in scope: Trial Balance, P&L, Balance Sheet,
  Sales Summary, Tax Summary, AR Aging, AP Aging, Bank Reconciliation (incl. manually selecting
  the KBANK account — see MED finding, needed a longer wait this round), General Ledger.
- Read the actual date-filter **input values** on TB/P&L/BS/AR/AP-aging to reconfirm the cutoff
  split with hard evidence (not just eyeballing labels) — see PRIMARY re-confirmation below.
- Cross-checked every reported total by hand across reports (TB↔BS asset/liability totals,
  AR-aging↔sales-summary↔TB-1130, AP-aging↔TB-2110↔BS, tax-summary↔P&L) — **all tie out
  internally**; the only cross-report mismatch found is the known cutoff-window one (see below).
  No *new* cross-report inconsistency found this round.
- Refreshed Trial Balance **5×** while the other 9 agents hammered co5 concurrently: **Dr = Cr ✓
  held on every refresh that completed** (1 and 5 both clean; 2/3/4 hit client-side timeouts
  before resolving — retried, never returned an imbalance or an error page). No `500`/`23505`
  observed anywhere in this session (page-level `response` listener tracked every request ≥500 —
  zero fired, `net5xx: 0` in the run log).
- Probed 4 admin/restricted routes directly: `/settings/users`, `/settings/roles`,
  `/settings/companies`, `/settings/api-keys` — all deny correctly (see Denied-as-expected).
- Read-only payroll probe: `/payroll` loaded, 3 runs visible (07/2026, 08/2026, 09/2026, all
  `จ่ายแล้ว`) — **not** touched (Create-run/pay buttons not clicked, per hard rule).
- Did not click ยืนยัน/ปิดงวด anywhere, did not create/edit/approve/delete anything.
- Console/pageerror/5xx listeners active on every page for the whole session.

## PRIMARY re-confirmation: TB/BS-vs-P&L cutoff mismatch — **STILL PRESENT, unchanged in v1.22.6**

Not in this round's fix scope (626/627 only touched numbering + TAX_OFFICER grant) — confirmed
still standing, with hard evidence:

| Report | Date-filter input value(s) | Semantics |
|---|---|---|
| `/reports/trial-balance` | `2026-07-19` | ณ วันที่ = **today** |
| `/reports/balance-sheet` | `2026-07-19` | ณ วันที่ = **today** |
| `/reports/ar-aging` | `2026-07-19` | ณ วันที่ = **today** |
| `/reports/ap-aging` | `2026-07-19` | ถึงวันที่ = **today** |
| `/reports/profit-loss` | `2026-07-01` / `2026-07-31` | **full current month** |

Because `/payroll` shows **three** runs already `จ่ายแล้ว` with pay dates in the future relative
to system "today" (07/2026 → 2026-07-30, **and** 08/2026 → 2026-08-30, **and** 09/2026 →
2026-09-29 — 11, 42, and 72 days ahead of today respectively), P&L's full-month window bakes in
the not-yet-elapsed July payroll while TB/BS's as-of-today cutoff correctly excludes it:
- P&L (`2026-07-01`→`2026-07-31`): revenue ฿13,000.00, expense **฿142,500.00**, net **-฿129,500.00**.
- TB/BS (as of `2026-07-19`): actual posted expense accounts are only 5000 COGS ฿5,000 + 5200
  services ฿10,000 = ฿15,000 (salary account 5400 = ฿0), net **-฿2,000.00** — verified by hand
  against TB's own numbers and matches BS's "กำไร(ขาดทุน)สะสมงวดปัจจุบัน -฿2,000.00" exactly.
- `/reports/tax-summary` independently confirms the P&L number for ก.ค. (รายจ่าย ฿142,500.00,
  กำไรสุทธิ -฿129,500.00) — so it's not a fluke of one page, it's a consistent second pairing.
- **New this round**: the tax-summary yearly table shows the *same already-future-dated* pattern
  continuing into ส.ค. (รายจ่าย ฿127,500.00) and ก.ย. (รายจ่าย ฿127,500.00) — i.e. the underlying
  root cause (future-dated payroll runs already flagged "paid") isn't isolated to July; any report
  whose date range reaches into August or September will show the same inflated-expense artifact.
  Nothing in the UI on any of these 5 report pages warns the reader that they use different
  cutoffs, or that not-yet-elapsed future-dated postings are baked into "this month/year."

**Verdict: confirmed still standing, unchanged from round 2. Not fixed this round (expected —
out of 626/627's scope). Blast radius is slightly larger than round 2 documented (extends to
Aug/Sep, not just July), same root cause.**

## CRIT-verify (this round's reason to exist)

- **CRIT-1 (doc-numbering 500/23505 under concurrency):** **not directly exercised by chief01** —
  QT/TI/RC/VI/PV/PO writes are sales01/ar01/purch01/ap01/appr01's PRIMARY mission, chief01's role
  is a read-only report sweep. Supporting evidence from this session: **zero HTTP 500s** observed
  across the entire run (explicit `page.on('response')` listener on every request, `net5xx: 0`),
  and Trial Balance's `Dr = Cr ✓` never broke across 5 refreshes spanning the concurrent-posting
  window. Consistent with CRIT-1 being closed, but **defer to sales01/ar01/purch01/ap01/appr01's
  reports for the authoritative verdict** — they're the ones actually hitting the numbering paths.
- **CRIT-2 (tax01 / ภ.พ.30 403):** **not tested by chief01** — CHIEF_ACCOUNTANT's grant set
  *does* include `tax.filing.preview`/`tax.filing.read`/`tax.filing.finalize` (broader than
  TAX_OFFICER's own grants, ironically), but ภ.พ.30 preview/PDF/.txt export is tax01's dedicated
  PRIMARY mission this round — not re-tested here to avoid redundant hammering of the same
  endpoint. **See tax01's round3 report for the authoritative CRIT-2 verdict.**

## Findings

| Sev | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| HIGH (reconfirmed) | P&L vs TB/BS period semantics | See PRIMARY re-confirmation above — still present in v1.22.6, blast radius now confirmed to extend into Aug/Sep. | `/reports/profit-loss`, `/reports/trial-balance`, `/reports/balance-sheet`, `/reports/tax-summary`, `/payroll` | chief01-profit-loss.png, chief01-balance-sheet.png, chief01-trial-balance.png, chief01-tax-summary.png, chief01-probe-payroll.png |
| MED (new this round) | System responsiveness under 10-agent concurrent swarm load | Multiple read-path client timeouts this round that round 2 did not report: TB refresh 2/3/4 of 5 hit `nav-gates-ready`/`page.goto` timeouts (15-30s) before eventually succeeding on retry; a fresh `/reports/bank-reconciliation` account-select action timed out once; a fresh login attempt (2nd script) timed out 30s waiting for the username field before a retry succeeded instantly; `/settings/users`/`/settings/roles` probe screenshots caught mid-"กำลังโหลด..." at a 1200ms wait and only resolved to the correct deny banner once rechecked with a 6s wait. **No HTTP 500 was ever observed** in any of these (page-level response listener confirms `net5xx: 0` across both script runs) — everything eventually succeeded, so this reads as latency/queueing under load rather than a correctness regression. Distinct from CRIT-1's numbering-write 500s; noting it because round 2 did not report this degree of read-side slowness and this round explicitly pushed harder (2-3x cycles per role). | Full session (both scripts); see chief01-tb-refresh-{2,3,4}.png (missing/partial vs -1/-5) and chief01-run.log timestamps | chief01-tb-refresh-1.png, chief01-tb-refresh-5.png, chief01-recheck-_settings_users.png |
| MED (reconfirmed, unchanged) | `/settings/api-keys`: partial deny + React error | Same as round 2 — deny banner text is correct but the MCP connector/OAuth section below it still fully renders (endpoint URL + setup instructions) for non-admin chief01 instead of being gated too; page still throws `Minified React error #418` in console. Not fixed this round (expected, out of scope). | `/settings/api-keys` | chief01-probe-_settings_api-keys.png |
| LOW (reconfirmed, unchanged) | AR aging: negative bucket, no visual flag | บริษัท ลูกค้าทดสอบ จำกัด still shows **-฿1,070.00** in the 0-30-day bucket (credit balance) styled identically to positive rows. Ties correctly to TB (5,350 + (-1,070) = 4,280). | `/reports/ar-aging` | chief01-ar-aging.png |
| LOW (reconfirmed, unchanged) | AP aging missing tie-out banner | AR aging has the `บัญชีคุมยอด` / `Dr = Cr ✓` tie-out banner; AP aging still has no equivalent for account 2110 (totals happen to agree with TB: 2,140 = 2,140, but no built-in verification badge). | `/reports/ar-aging` vs `/reports/ap-aging` | chief01-ar-aging.png, chief01-ap-aging.png |
| LOW (reconfirmed, unchanged) | Bank reconciliation: unreconciled ผลต่าง ฿3,210, no explanation | Same numbers as round 2: statement balance ฿0.00, GL balance -฿4,280.00, 1 deposit-in-transit ฿2,140 (RC `07-2026-RC-0002`), 1 outstanding payment ฿3,210 (PV `07-2026-PV-COGS-0002`), ผลต่าง = ฿3,210.00, still no tie-out badge like TB/BS/AR-aging have. | `/reports/bank-reconciliation` (KBANK selected) | chief01-bank-reconciliation-selected.png |
| INFO / SoD design (reconfirmed + expanded) | CHIEF_ACCOUNTANT holds very broad authority | 74 grants incl. `tax.filing.finalize`, `gl.period.close`, `gl.year.close`, `payroll.run.pay`, `purchase.purchase_order.approve`, `purchase.payment_voucher.approve` — one role can preview+finalize VAT, close a GL period/fiscal year, AND approve/pay purchases, with no separation-of-duties split visible in the permission set. Round 2's chief01 flagged this narrower (payroll-only); this round's full permission dump shows it's broader still. Per HARD RULE 2 **not exercised** — flagged for SoD design triage only. | `/api/proxy/me/permissions` (see chief01-permissions.json, not committed per output-only rule — grant list reproduced in this report) | — |

## Denied-as-expected

- `/settings/users` → clean deny (confirmed on recheck with 6s wait): "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้ (sys.user.manage) — กรุณาติดต่อผู้ดูแลระบบ"; underlying API 403. ✓ (first-pass screenshot caught it mid-load under swarm latency — see MED finding — not a deny failure.)
- `/settings/roles` → same pattern, `sys.role.manage`, confirmed on recheck. ✓
- `/settings/companies` → clean deny: "หน้านี้สำหรับ Super Admin เท่านั้น"; resolved immediately even on first pass. ✓
- `/settings/api-keys` → deny banner text correct; see MED finding above for the partial-render caveat on the same page (unchanged from round 2).
- Payroll: `/payroll` loaded read-only; no mutation buttons clicked despite the role holding
  `payroll.run.manage`/`payroll.run.pay` — treated as read-only per hard rule, same as round 2.
- ยืนยัน/ปิดงวด: never clicked anywhere, despite the role having `tax.filing.finalize` and
  `gl.period.close`/`gl.year.close` — SoD note above, not exercised.

## Screenshots (repo-relative)

`shots/round3/chief01-00-dashboard.png`, `chief01-trial-balance.png`, `chief01-profit-loss.png`,
`chief01-balance-sheet.png`, `chief01-sales-summary.png`, `chief01-tax-summary.png`,
`chief01-ar-aging.png`, `chief01-ap-aging.png`, `chief01-bank-reconciliation.png`,
`chief01-bank-reconciliation-selected.png`, `chief01-general-ledger.png`,
`chief01-probe-payroll.png`, `chief01-probe-_settings_users.png`,
`chief01-probe-_settings_roles.png`, `chief01-probe-_settings_companies.png`,
`chief01-probe-_settings_api-keys.png`, `chief01-tb-refresh-1.png` … `chief01-tb-refresh-5.png`,
`chief01-recheck-_settings_users.png`, `chief01-recheck-_settings_roles.png`.
