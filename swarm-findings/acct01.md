# acct01 (Accountant) — UX Swarm Findings — co5 prod

Run: 2026-07-19T10:59:41.524Z — target https://teas.kazaki-rio.com, company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด)


## Done (สิ่งที่ทำ+ผล)

- Login สำเร็จ (acct01) → dashboard
- /me → companyId=5, companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด, isSuperAdmin=false
- /me/permissions → roles=["ACCOUNTANT"], permCount=54
- TB check #1: balanced ("Dr = Cr ✓") — screenshot swarm-findings/shots/acct01-tb-1.png
- GL: viewed account "1110 — เงินสด" for current-month range — page rendered, no crash
- GL: account "1110 — เงินสด" had no posted lines in current-month range (no drill-down link to test) — screenshot swarm-findings/shots/acct01-gl-report.png
- TB check #2: balanced ("Dr = Cr ✓") — screenshot swarm-findings/shots/acct01-tb-2.png
- Bank reconciliation: viewed report for bank account id=1 — rendered OK (read-only), screenshot swarm-findings/shots/acct01-bank-recon.png
- TB check #3: balanced ("Dr = Cr ✓") — screenshot swarm-findings/shots/acct01-tb-3.png
- ภ.พ.30 preview period=2026-07 — rendered OK, screenshot swarm-findings/shots/acct01-pnd30-preview.png
- ภ.พ.30 raw text dump (for manual number cross-check against baseline sales 13,000/910, purchases 15,000/1,050, credit c/f 140):

```
ภ.พ.30 (แบบแสดงรายการภาษีมูลค่าเพิ่ม)
งวด (เดือน/ปี)
แสดงตัวอย่าง
ยืนยัน/ปิดงวด
ดาวน์โหลด PDF (ภ.พ.30)
ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)
Preview · manual
ℹ️ ไฟล์ .txt นี้ยังไม่ใช่ไฟล์ยื่นโดยตรง — ต้องนำไปทำต่อในโปรแกรม RD Prep (ดูขั้นตอน)
บริษัท ทดสอบ VAT (DUMMY) จำกัด · 0105568000122 · กำหนดยื่นภายใน 2026-08-15
ขายที่ต้องเสียภาษี	฿13,000.00	฿910.00
ขายอัตรา 0% (ม.80/1)	฿0.00	฿0.00
ขายยกเว้น (ม.81)	฿0.00	฿0.00
ภาษีขายรวม		฿910.00
ซื้อที่ขอคืนได้	฿15,000.00	฿1,050.00
สัดส่วนเครดิตภาษีซื้อ (ม.82/6)	100.00%	฿0.00
ภาษีซื้อรวม		฿1,050.00
ภาษีที่ต้องชำระสุทธิ		฿0.00
เครดิตยกไปงวดหน้า		฿140.00
⚠ วันสุดท้ายของการยื่น: 2026-08-15 — ควรยืนยัน/ปิดงวดล่วงหน้าอย่างน้อย 1 วัน
```
- ภ.พ.30: finalize button present, enabled=true — NOT clicked (hard rule).
- TB check #4: balanced ("Dr = Cr ✓") — screenshot swarm-findings/shots/acct01-tb-4.png
- /settings/products: page loads (read access), but no edit affordance detected for existing rows — consistent with the "no master-data edits" expectation.
- /payroll: page loads, table empty ("ไม่มีข้อมูล"), Create-run + PND1a-print header buttons correctly HIDDEN (`PermissionGate scope="payroll.run.manage"`) — no mutate controls of any kind rendered. Verified the one auto-probe hit ("จ่าย" regex) was a false positive: it matched the sortable **column header** button "วันที่จ่าย" (Pay Date), confirmed via targeted DOM dump (`<button>วันที่จ่าย<svg class="lucide-arrow-up".../></button>`) — a read-only sort control, not an action. No real payroll mutation surface found for acct01.

### Trial Balance Dr=Cr — repeated-refresh check
Refreshed 4x across the session, interleaved with GL/bank-recon/PND30 activity while the swarm was posting documents concurrently.

| # | time (UTC) | balanced? | badge text | screenshot |
|---|---|---|---|---|
| 1 | 2026-07-19T10:58:51.732Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-1.png |
| 2 | 2026-07-19T10:59:17.802Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-2.png |
| 3 | 2026-07-19T10:59:26.341Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-3.png |
| 4 | 2026-07-19T10:59:32.079Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-4.png |

**Tie held across all refreshes: YES — Dr=Cr never broke.**

## Findings (severity CRIT/HIGH/MED/LOW)

| Severity | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| LOW | GL / UX | Account 1110 (เงินสด) had zero posted lines in the current-month window when queried — could not exercise the actual JE drill-down link (`/journals/{id}`) for acct01 this session; not a bug (co5 sandbox may genuinely be empty for this account/period this month), but the drill-down click-through itself is UNVERIFIED this run | goto /reports/general-ledger, pick account 1110, current-month range, showReport | swarm-findings/shots/acct01-gl-report.png |
| LOW | Payroll — investigate | Browser console logged a 403 while on `/payroll` (and separately on `/`) during this session; page still rendered a clean empty state ("ไม่มีข้อมูล") rather than any error/deny banner. Could be an unrelated resource fail (benign) OR a data-fetch 403 silently swallowed into "no data" (would be confusing: user can't tell "genuinely empty" from "hidden by permission"). Inconclusive from the client alone — worth a follow-up with network trace / backend logs before treating as a bug | goto /payroll, watch page.on('console') | swarm-findings/shots/acct01-probe-payroll.png |

## Denied-as-expected (RBAC ที่ deny ถูกต้อง)

- /settings/users → Thai/EN deny message shown on page (clean deny), user list table NOT rendered
- /payroll → Create-run and PND1a-print buttons correctly hidden via `PermissionGate scope="payroll.run.manage"`; no mutate control anywhere on the page (the one text match was a benign column-sort header, verified false positive — see Done section)
- /settings/products → page readable but no edit affordance surfaced for existing master-data rows

## Console / network errors captured (whole session)

- [console.error] https://teas.kazaki-rio.com/login — Failed to load resource: the server responded with a status of 404 ()
- [console.error] https://teas.kazaki-rio.com/ — Failed to load resource: the server responded with a status of 403 ()
- [console.error] https://teas.kazaki-rio.com/settings/users — Failed to load resource: the server responded with a status of 403 ()
- [console.error] https://teas.kazaki-rio.com/payroll — Failed to load resource: the server responded with a status of 403 ()


### Trial Balance Dr=Cr — extended watch (spaced ~75s apart, ~6min span, overlapping other 9 swarm agents' posting activity)

| # | time (UTC) | balanced? | badge text | screenshot |
|---|---|---|---|---|
| 5 | 2026-07-19T11:03:48.541Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-5.png |
| 6 | 2026-07-19T11:05:05.409Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-6.png |
| 7 | 2026-07-19T11:06:23.106Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-7.png |
| 8 | 2026-07-19T11:07:43.429Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-8.png |
| 9 | 2026-07-19T11:09:00.268Z | YES | Dr = Cr ✓ | swarm-findings/shots/acct01-tb-9.png |

**Extended-watch verdict: Dr=Cr held across the entire spaced watch — no imbalance observed despite concurrent swarm posting.**
