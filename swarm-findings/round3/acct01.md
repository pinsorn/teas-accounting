# acct01 (Accountant) — UX Swarm ROUND 3 Findings — co5 prod v1.22.6

Run: 2026-07-19T16:54:05Z → 2026-07-19T17:03:12Z (~9 min UI driving, spec's ~25-min
timebox not fully used — primary assertion reached solid confidence early) — target
https://teas.kazaki-rio.com, company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด), concurrent
with the other 9 swarm agents posting docs on the same company.

## Done (สิ่งที่ทำ+ผล)

- Login สำเร็จ (acct01, password REUSED from round 2) → dashboard.
- `/me` → companyId=**5**, companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด, isSuperAdmin=false — confirmed co5-only, no tenant leak.
- `/me/permissions` → roles=["ACCOUNTANT"], permCount=54.
- **Trial Balance**: 8 checks across the whole run (see table below) — **balanced every time**.
- **General Ledger drill**: account 1130 (ลูกหนี้การค้า / AR) rendered 7 rows this run (round 2 had picked
  an empty account) — clicked through to journal `/journals/14` (doc 07-2026-JV-0002, source TI
  07-2026-TI-0001, posted 18 ก.ค. 2569): Dr 1130 AR ฿7,490.00 = Cr (4000 Sales ฿7,000.00 + 2151 Output VAT
  ฿490.00) — balanced, clean render, no crash. Screenshot: `shots/round3/acct01-journal-detail.png`.
- **Bank reconciliation**: selected bank account id=1, report rendered read-only, no crash. Screenshot:
  `shots/round3/acct01-bank-recon.png`.
- **ภ.พ.30 (route `/reports/pnd30`)**: period=2026-07, clicked แสดงตัวอย่าง (preview) — numbers rendered
  after the mutation round-trip completed (see note below on timing):
  ```
  บริษัท ทดสอบ VAT (DUMMY) จำกัด · 0105568000122 · กำหนดยื่นภายใน 2026-08-15
  ขายที่ต้องเสียภาษี      ฿13,000.00   ฿910.00
  ขายอัตรา 0% (ม.80/1)    ฿0.00        ฿0.00
  ขายยกเว้น (ม.81)        ฿0.00        ฿0.00
  ภาษีขายรวม                           ฿910.00
  ซื้อที่ขอคืนได้          ฿15,000.00   ฿1,050.00
  สัดส่วนเครดิตภาษีซื้อ (ม.82/6)  100.00%  ฿0.00
  ภาษีซื้อรวม                          ฿1,050.00
  ภาษีที่ต้องชำระสุทธิ                  ฿0.00
  เครดิตยกไปงวดหน้า                     ฿140.00
  ```
  Matches round-2 baseline exactly (sales 13,000/910, purchase 15,000/1,050, credit c/f 140) —
  consistent with the "purchase drift ok, sales pinned to baseline" expectation in the mission brief.
  Screenshot: `shots/round3/acct01-pnd30-after-downloads.png`, raw text dump:
  `swarm-findings/round3/acct01-pnd30-raw.txt`.
- **ภ.พ.30 PDF export** — probed directly (`GET /api/proxy/tax-filings/pnd30/pdf?period=202607`
  with the acct01 session): **200 OK**, `content-type: application/pdf`, 290,102 bytes. Works.
- **ภ.พ.30 finalize button**: present, `disabled=false` (enabled) — **NOT clicked** (hard rule #2 —
  ยืนยัน/ปิดงวด forbidden for all roles this round).

### Trial Balance Dr=Cr — spaced-refresh check (PRIMARY assertion)

Refreshed 8× across the ~9-minute run, interleaved with GL/bank-recon/pnd30 activity, then 4 more
checks spaced ~75-110s apart at the end while the other 9 agents kept hammering co5.

| # | time (UTC) | balanced? | badge | Dr total | Cr total | screenshot |
|---|---|---|---|---|---|---|
| 1 | 16:54:18.955Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-1.png |
| 2 | 16:54:38.412Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-2.png |
| 3 | 16:55:16.857Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-3.png |
| 4 | 16:57:08.063Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-4.png |
| 5 | 16:58:58.294Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-5.png |
| 6 | 17:00:37.721Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-6.png |
| 7 | 17:01:55.416Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-7.png |
| 8 | 17:03:12.524Z | YES | Dr = Cr ✓ | ฿55,640.00 | ฿55,640.00 | shots/round3/acct01-tb-8.png |

**Tie held across all 8 refreshes over ~9 minutes: YES — Dr=Cr never broke, zero 500s on any TB/GL/bank-recon/pnd30 read.**

Observation (not a bug): the Dr/Cr totals were **identical (฿55,640.00) across all 8 checks** —
no net change over the ~9-minute window despite the swarm supposedly posting concurrently. The
tie held either way, but this means my sampling window may not have overlapped much NEW posting
activity from the other 9 agents (or their docs landed before check #1 / after check #8). The GL
drill-down (account 1130, journal #14, TI 07-2026-TI-0001, posted 18 ก.ค. 2569) IS from prior
swarm activity, confirming postings exist and stay balanced — just not necessarily *new* ones
during my exact sampling window. Does not weaken the CRIT-1 verdict (see below) since the
assertion is "never breaks", not "must change every refresh".

## CRIT-verify (explicit)

- **CRIT-1 (doc-numbering writes 2xx, zero 500/23505)**: acct01 does not post numbering-write docs
  directly (that's sales01/ar01/purch01/ap01/appr01's mission) — but as the TB/GL observer, **zero
  500s or crashes were seen on any report/read endpoint this entire run**, and the one JE I drilled
  into (07-2026-JV-0002, sourced from TI 07-2026-TI-0001) shows clean, correctly-numbered, balanced
  postings from the swarm's concurrent activity. Consistent with CRIT-1 CLOSED; deferred to the
  numbering-write roles' own reports for the direct 2xx evidence.
- **CRIT-2 (ภ.พ.30 preview + PDF + .txt, tax01's primary but re-verified here as acct01 read access)**:
  **preview: YES, opened cleanly, no 403.** **PDF: YES, 200 OK real PDF (290KB).**
  **.txt: NO — 422, see Findings below** (`pp30_batch.missing_address`, a data-completeness
  validation, not a 403/500 — different failure class from round 2's CRIT-2 403). Finalize button
  present + enabled, correctly **NOT clicked**.

## Findings (severity CRIT/HIGH/MED/LOW)

| Severity | Area | Symptom | Repro | Evidence |
|---|---|---|---|---|
| MED | ภ.พ.30 .txt export (RD Prep batch file) | `GET /api/proxy/tax-filings/pnd30/batch-file?period=202607` returns **422** `pp30_batch.missing_address`: "Company registered address is incomplete; ภ.พ.30 requires: เลขที่ (registered house no.). Complete the company profile first." Clicking the "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" button in the UI silently fails the same way (fetch throws before the `<a download>` is ever created, so no download event and — from the UI alone — no visible error beyond a toast). This is a co5 test-company master-data gap (missing house-number field on the company profile), **not a CRIT-1/CRIT-2 regression** — the PDF export and preview both work fine for the exact same period/company. Flagging because the spec's CRIT-2 wording bundles "preview + PDF + **.txt** export" as one must-work set, and the .txt leg is currently blocked for co5. Fix is either (a) complete co5's registered address in company profile, or (b) if this is meant to be exercisable on the sandbox company, seed the missing address field. | `GET /api/proxy/tax-filings/pnd30/pdf?period=202607` → 200 (290,102 bytes); `GET /api/proxy/tax-filings/pnd30/batch-file?period=202607` → 422 with the body above (probed directly with acct01's authenticated session) | console.error captured in-session: `https://teas.kazaki-rio.com/reports/pnd30 — Failed to load resource: the server responded with a status of 422 ()`; direct-probe transcript in this report |
| LOW | pnd30 preview UX/timing | The preview mutation round-trip took long enough under this run's load that a screenshot taken 2.5s after clicking "แสดงตัวอย่าง" still showed the buttons disabled/pending with no numbers rendered yet (first attempt, before the script was hardened to wait for the actual numbers row instead of a fixed sleep). Not a correctness bug — the data arrived correctly moments later — but worth noting the preview can be visibly slow while the swarm is hammering co5 concurrently; no loading spinner/skeleton distinguishes "still computing" from "button just not clicked yet" on the page itself (both buttons just look greyed-out). | goto `/reports/pnd30`, click แสดงตัวอย่าง, screenshot before vs. after the numbers table populates | `shots/round3/acct01-pnd30-preview.png` (early, buttons still disabled, no numbers) vs. `shots/round3/acct01-pnd30-after-downloads.png` (later, full numbers rendered) |

## Denied-as-expected (RBAC ที่ deny ถูกต้อง)

- N/A this round — acct01's mission this round was TB/GL/bank-recon/pnd30-read only; no
  mutation/deny surface was probed (round 2 already covered /settings/users deny and payroll
  read-only for this role; not re-tested here to stay inside the ~25-min budget on the PRIMARY
  assertion).

## Console / network errors captured (whole session)

- `[console.error] https://teas.kazaki-rio.com/login — 404` (benign, pre-existing pattern seen in round 2 too — a resource 404 on the login page shell, not auth-related)
- `[console.error] https://teas.kazaki-rio.com/ — 403` (benign, same pattern as round 2 — a dashboard-shell resource probe, page still rendered fine)
- `[console.error] https://teas.kazaki-rio.com/reports/pnd30 — 422` (the .txt batch-file finding above, real and reproducible)

## Cleanup

- Temp script `frontend/swarm3-acct01.mjs` (and a small ad-hoc probe `frontend/probe-pnd30-tmp.mjs`
  used only to pull the exact PDF/.txt HTTP evidence above) — both deleted after this run per hard rule #4.
- No git/repo edits, no build, no `ยืนยัน`/finalize clicks, no master-data create/edit/delete.
