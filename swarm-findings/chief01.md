# chief01 — Chief Accountant — co5 UX swarm findings (2026-07-19, prod v1.22.5)

Target: https://teas.kazaki-rio.com, company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด). Mission: read every
report, hunt cross-report number conflicts, probe admin-only buttons. Playwright headless via
temp `frontend/swarm-chief01.mjs` (+ two small follow-ups to read date-filter values and select
the bank account) — all deleted after the run. No mutations attempted anywhere.

## Done

- Logged in as chief01 / `UxSwarm-2026-A7`, 1st attempt succeeded.
- Confirmed tenant via `/api/proxy/me`: `companyId=5`, `companyName="บริษัท ทดสอบ VAT (DUMMY) จำกัด"`,
  `isSuperAdmin=false`, `allowedCompanies=[{id:5,...}]` only. CompanySwitcher control correctly
  absent (it only renders for super-admins). Swept every report + probe page and **found no
  data belonging to any other company** — no "นาย พงศ์สันต์" / "เรปทาวน์" anywhere. **CRIT
  tenant-leak check: clean.**
- Full-page screenshot + text dump of all 8 reports in my mission: Trial Balance, P&L,
  Balance Sheet, Sales Summary, Tax Summary, AR Aging, AP Aging, Bank Reconciliation (had to
  manually select the KBANK account — see LOW finding below).
- Read the actual date-filter **input values** (not just labels) on TB/P&L/BS/AR/AP-aging to
  root-cause a number mismatch rather than guess (see HIGH finding).
- Verified Trial Balance arithmetic by hand (Dr column and Cr column both sum to ฿55,640.00,
  matching the page's own "Dr = Cr ✓" badge) — no imbalance found anywhere (TB, BS, AR-aging
  tie badges all green).
- Probed 6 admin/restricted routes by typing the URL directly: `/settings/users`,
  `/settings/roles`, `/settings/companies`, `/settings/api-keys`, `/settings/employees`,
  `/payroll`.
- Did not click ยืนยัน/ปิดงวด, did not create/edit/approve/delete anything, did not touch
  payroll despite my own role carrying full payroll mutation permissions (flagged as a finding
  instead of exercised — see INFO row).
- Console/pageerror/5xx listeners active on every page for the whole session.

## Findings

| Sev | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| HIGH | P&L vs TB/BS period semantics | `/reports/profit-loss` defaults its date range to the **full current month** (`2026-07-01` → `2026-07-31`) while `/reports/trial-balance` and `/reports/balance-sheet` default to **"ณ วันที่" = today** (`2026-07-19`). July's P&L range therefore includes the `07/2026` payroll run — pay date `2026-07-30`, **11 days in the future relative to system "today"**, already flagged `จ่ายแล้ว` (paid) — while TB/BS's as-of-today cutoff correctly excludes it. Result: P&L reports a July net loss of **-฿129,500.00**, but the Balance Sheet's "กำไร(ขาดทุน)สะสมงวดปัจจุบัน" line for what looks like the same period reads only **-฿2,000.00**. Verified by hand: TB's actual posted expense accounts (5000 COGS ฿5,000 + 5200 services ฿10,000 = ฿15,000) net against revenue ฿13,000 = -฿2,000, matching BS exactly; salary account 5400 is ฿0 in TB. Tax-summary's July row independently confirms P&L's number (รายจ่าย ฿142,500 = the same figure), so this isn't a fluke of one page — TB/BS and P&L/tax-summary are two internally-consistent but **mutually inconsistent** pairs, and nothing in the UI warns the reader that they use different cutoffs or that a not-yet-elapsed future posting is baked into "this month." | Login chief01 → `/reports/profit-loss` (date inputs `2026-07-01`/`2026-07-31`, net loss -129,500) → `/reports/balance-sheet` (date input `2026-07-19`, current-period line -2,000) → `/payroll` (row `07/2026`, pay date `2026-07-30`, status `จ่ายแล้ว`) | chief01-profit-loss.png, chief01-balance-sheet.png, chief01-probe-_payroll.png |
| MED | AP aging missing tie-out banner | AR aging shows a control-account tie-out banner: `บัญชีคุมยอด (1130) ฿4,280.00` / `ยอดรวมทะเบียนย่อย ฿4,280.00` / `Dr = Cr ✓`. AP aging (same report family, mirrors the same design per its own source comment) has **no equivalent banner** for account 2110 — just the vendor table + total. The totals happen to agree with TB this time (2,140 = 2,140), but the Chief Accountant has no built-in way to verify AP subledger-to-GL tie the way AR gets one — asymmetric feature, exactly the kind of gap that matters for this role. | `/reports/ar-aging` vs `/reports/ap-aging` — compare page structure | chief01-ar-aging.png, chief01-ap-aging.png |
| MED | `/settings/api-keys`: partial deny + React error | The deny banner ("ต้องมีสิทธิ์ผู้ดูแลระบบ") renders correctly for chief01 (non-admin), but the MCP connector / OAuth endpoint section **below** it still renders in full (endpoint URL + setup instructions) instead of being gated too. No secret is exposed (it's a public connector URL, not an actual key), but the gating is inconsistent within one page, and the page throws `Minified React error #418` (hydration text mismatch) in the console — a real client bug, not just a UX nit. | Login chief01 → goto `/settings/api-keys` directly | chief01-probe-_settings_api-keys.png |
| MED | AR aging: negative bucket, no visual flag | `บริษัท ลูกค้าทดสอบ จำกัด` shows **-฿1,070.00** in the "ยังไม่ถึงกำหนด (0-30 วัน)" bucket (i.e. an overpayment/credit balance), rendered in the same style/color as every positive-balance row. Arithmetically it's fine (5,350 + (-1,070) = 4,280 total, ties to TB), but at a glance it reads like a data error rather than "this customer overpaid us." | `/reports/ar-aging` | chief01-ar-aging.png |
| MED | Bank reconciliation (KBANK): unreconciled ผลต่าง ฿3,210, no explanation | After manually selecting KBANK (see LOW below): Statement closing balance **฿0.00**, GL balance **-฿4,280.00** (ties correctly to TB/BS account 1120), 1 deposit-in-transit (฿2,140 — receipt `07-2026-RC-0002`) and 1 outstanding payment (฿3,210 — PV `07-2026-PV-COGS-0002`), leaving **ผลต่าง (difference) = ฿3,210.00**. The ฿0.00 statement balance strongly suggests no bank statement has been imported for this account/period yet, which would make the "difference" a non-issue — but unlike TB/BS/AR-aging (which all show an explicit green "Dr = Cr ✓" / tie badge), this report gives **no signal** telling the reader whether ฿3,210 is expected (no statement imported) or a real red flag. | `/reports/bank-reconciliation` (must select KBANK — see below) | chief01-bank-reconciliation-selected.png |
| LOW | Bank recon: account not pre-selected | The company has exactly **one** bank account (KBANK), yet `/reports/bank-reconciliation` still loads with the selector on "ทั้งหมด" and shows only "เลือกบัญชีธนาคารเพื่อดูรายงานกระทบยอด" until the single account is manually picked from the `<select>`. Trivial friction, but every user of this single-bank company pays it every visit. | `/reports/bank-reconciliation` | chief01-bank-reconciliation.png (before select) |
| LOW | `/login` fires one console 404 on first paint | A single `Failed to load resource: the server responded with a status of 404` logs on `/login` before any interaction. Did not block or delay login (succeeded on attempt 1). Not investigated further — looked like a static asset, out of scope for a report-reading sweep. | `/login` (first paint) | chief01-00-dashboard.png (post-login) |
| INFO / SoD design | Chief Accountant role holds full payroll mutation rights | `/api/proxy/me/permissions` for chief01 (role `CHIEF_ACCOUNTANT`) includes `payroll.run.manage`, `payroll.run.pay`, **and** `payroll.run.post` — i.e. one role can create, post, *and* pay a payroll run solo, with no separate preparer/approver split visible in the permission set. Per HARD RULE 2 this was **not exercised** (payroll treated as read-only for this test) — flagging purely for SoD design triage, same shape as the ap01 mission's PV-approval SoD check but on the payroll side. | n/a — API permission list, see script stdout | — |

## Denied-as-expected

- `/settings/users` → clean deny: "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้ (sys.user.manage) — กรุณาติดต่อผู้ดูแลระบบ"; underlying API call returned 403 (not just a UI hide). ✓
- `/settings/roles` → clean deny, same pattern, `sys.role.manage`; API 403. ✓
- `/settings/companies` → clean deny: "หน้านี้สำหรับ Super Admin เท่านั้น"; API 403. ✓
- `/settings/api-keys` → deny banner + API 403 are correct; see MED finding above for the
  partial-render caveat on the same page.
- `/settings/employees` → **not** a deny — chief01 legitimately holds `master.employee.manage`
  and the page loaded normally with data. Expected: Chief Accountant needs employee data for
  payroll/WHT reporting. Recorded here for completeness, not a bug.

## Screenshots (repo-relative)

`swarm-findings/shots/chief01-00-dashboard.png`, `chief01-trial-balance.png`,
`chief01-profit-loss.png`, `chief01-balance-sheet.png`, `chief01-sales-summary.png`,
`chief01-tax-summary.png`, `chief01-ar-aging.png`, `chief01-ap-aging.png`,
`chief01-bank-reconciliation.png`, `chief01-bank-reconciliation-selected.png`,
`chief01-settings-company.png`, `chief01-probe-_settings_users.png`,
`chief01-probe-_settings_roles.png`, `chief01-probe-_settings_companies.png`,
`chief01-probe-_settings_api-keys.png`, `chief01-probe-_settings_employees.png`,
`chief01-probe-_payroll.png`
