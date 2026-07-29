# Leg F — Journal Vouchers / Director Loan — Prod Verification (v1.25.0)

Target: https://teas.kazaki-rio.com
Company: co7 (non-VAT, active) — nvadmin02 / UxSwarm-2026-NV4 (attempt 1)
Status: IN PROGRESS

## Step 0 — Login
PASS. Logged in as nvadmin02 / co7 (บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด). Footer confirms "TEAS · v1.25.0". Dashboard loads clean, no errors. Screenshot: dashboard after login.

## Step 1 — Director loan JV

### BEFORE (baseline, as of 29/07/2569 / July 2026 period)
Trial Balance (`/reports/trial-balance`, ณ วันที่ 07/29/2026):
- Total Dr = Total Cr = ฿115,953.07 (Dr=Cr checkmark shown)
- 1120 เงินฝากธนาคาร: Dr 0.00 / Cr 1,070.00 / คงเหลือ -฿1,070.00 (net credit/negative bank balance — baseline)
- 2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น: Dr 0.00 / Cr 0.00 / คงเหลือ ฿0.00 (zero, as expected — new account)

P&L (`/reports/profit-loss`, month = July 2026, 01/07-31/07):
- รายได้ (Revenue): ฿0.00
- ค่าใช้จ่าย (Expense): ฿115,953.07
- กำไรสุทธิ (Net profit): **-฿115,953.07** (net loss)
- Note: UI shows a warning banner that the range includes a future date (31/07/2569 not yet reached) — informational, not a bug.

### POSTING
Created JV via `/journals/new`: date 07/29/2026, description "กรรมการให้บริษัทกู้ยืม", lines:
- Dr 1120 เงินฝากธนาคาร 100,000.00
- Cr 2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น 100,000.00
Footer showed "ผลต่าง: 0.00 (ยอดตรงกัน)" and บันทึก (Save) enabled. Clicked Save -> toast "บันทึก" success. Journal list shows new row **07-2026-JV-0003**, สถานะ = บันทึกแล้ว. Opening it (`/journals/180`) shows สถานะ = **Posted** already (nvadmin02/COMPANY_ADMIN has gl.journal.post — save = post directly, no separate post step in this flow). Screenshot saved: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785314622053-31.jpg`

### AFTER
Trial Balance (`/reports/trial-balance`, ณ 07/29/2026):
- Total Dr = Total Cr = **฿215,953.07** (up from 115,953.07 — exactly +100,000.00, footer still shows Dr=Cr ✓)
- 1120 เงินฝากธนาคาร: Dr 100,000.00 / Cr 1,070.00 / คงเหลือ ฿98,930.00 — delta vs before (-1,070.00 → 98,930.00) = **+100,000.00 exactly**
- 2190: Dr 0.00 / Cr 100,000.00 / คงเหลือ -฿100,000.00 — delta vs before (0.00 → -100,000.00) = **+100,000.00 on the credit side exactly**

P&L (`/reports/profit-loss`, July 2026):
- Revenue ฿0.00, Expense ฿115,953.07, **Net profit -฿115,953.07 — IDENTICAL to before.** Confirmed: a liability posting has ZERO effect on P&L.

Balance Sheet (`/reports/balance-sheet`, ณ 07/29/2026):
- Assets: 1120 เงินฝากธนาคาร ฿98,930.00 — total assets ฿98,930.00
- Liabilities (หนี้สิน): includes **2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น ฿100,000.00** — correctly listed as a liability, not equity or income
- Dr = Cr ✓ checkmark shown; total liabilities+equity ties to total assets (98,930.00)
- Screenshot saved: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785314672154-32.jpg`

**RESULT: PASS.** Trial balance foots, bank +100,000.00 exactly, 2190 +100,000.00 exactly (credit side), net profit unchanged (-115,953.07 both before/after), 2190 correctly appears as a liability on the Balance Sheet.

## Step 2 — Non-sales income JV

Posted JV `07-2026-JV-0004` (#181): Dr 1120 เงินฝากธนาคาร 5,000.00 / Cr 4300 รายได้อื่น 5,000.00, description "รายได้อื่นที่ไม่ใช่การขาย". Status: Posted immediately (same behavior as step 1).

P&L after: Revenue ฿5,000.00, Expense ฿115,953.07, Net profit **-฿110,953.07**.
Delta vs step-1 baseline (-115,953.07 → -110,953.07) = **+5,000.00 exactly.**

**RESULT: PASS.** This is the case that SHOULD move P&L, and it moved by exactly the posted amount — confirming step 1's "unchanged" result was real (the report engine does update; a loan genuinely doesn't touch it, income genuinely does).

Note: minor automation snag (not a product bug) — on the second JV form, an early batched click landed on stale coordinates after the page reflowed post dropdown-selection, which added a spurious 3rd blank line item instead of typing into the amount field. Recovered by removing the extra line and re-entering fields one action at a time with a screenshot between each. Worth flagging for future browser-automation legs: this journal form's layout shifts height when a dropdown is populated, so do not blind-batch multiple coordinate clicks across a dropdown-selection boundary.

## Step 3 — Split-entry float case

Entered 4 lines: Dr 1120 33.33 / Dr 1120 33.33 / Dr 1120 33.34 / Cr 1110 100.00 (postable accounts). Footer showed รวมเดบิต 100.00 / รวมเครดิต 100.00 / **ผลต่าง: 0.00 (ยอดตรงกัน)** and the บันทึก (Save) button was **enabled** (not greyed) — screenshot: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785314963249-33.jpg`. Saved successfully -> posted as **07-2026-JV-0005** (#182), status Posted, all 4 lines correct (33.33/33.33/33.34/100.00), รวม 100.00/100.00.

**RESULT: PASS.** This is exactly the regression case the fix targets (float rounding causing "balanced-looking but button disabled") — confirmed fixed: green/balanced state and enabled Save agree.

## Step 4 — Unbalanced refused

Entered Dr 1120 100.00 / Cr 2190 90.00 (description "ทดสอบ unbalanced entry 100 vs 90"). Footer showed **ผลต่าง: 10.00** in red, บันทึก (Save) button **disabled/greyed**, and Thai message displayed below the form:
> "ยอดเดบิตรวมต้องเท่ากับยอดเครดิตรวม และมากกว่า 0 จึงจะบันทึกบัญชีได้"
> ("Total debit must equal total credit, and be greater than 0, in order to save the entry.")

Clicked the disabled Save button anyway — no-op (page did not navigate, no submission). Confirmed via `/journals` list immediately after: still ends at 07-2026-JV-0005, **no JV-0006 was created**.

**RESULT: PASS.** UI correctly refuses to submit an unbalanced entry, and the Thai validation message is clear and sensible.

## Step 5 — Guards (dropdown, future date)

**No header accounts in dropdown:** Chart of Accounts (`/settings/chart-of-accounts`) shows 30 accounts total, all with "บัญชีหลัก (Header)" = "—" (none are headers in this company's CoA) — so there was nothing to filter for headers as a negative baseline. The JV line-account `<select>` (checked via `read_page`) lists exactly the 29 active leaf accounts (1110...5500), confirming no unexpected accounts leak in.

**No inactive accounts in dropdown (actively tested):** Created a test account **9999 บัญชีทดสอบ swarm / Swarm Test Account** via `/settings/chart-of-accounts` -> "เพิ่มบัญชี", confirmed it appeared active in the CoA list and in the JV dropdown. Then edited it, unchecked "ใช้งาน" (Active) and saved — CoA list showed its สถานะ column flip to "—" (inactive). Re-opened `/journals/new` and re-read the account `<select>` via `read_page`: **9999 no longer appears in the dropdown** (still exactly the 29 original active accounts). **Guard confirmed working.**

**Future-dated JV refused:** Set วันที่เอกสาร = 12/31/2026 (future vs. today 07/29/2026) on an otherwise-valid balanced entry (Dr 1120 50.00 / Cr 2190 50.00). The Save button was NOT pre-disabled by the future date (only the balance check disables it client-side) — but clicking Save triggered a red toast:
> "ไม่สามารถบันทึกรายการที่ลงวันที่ในอนาคตได้"
> ("Cannot save an entry dated in the future.")
Confirmed via `/journals` list immediately after: still exactly 5 JVs (07-2026-JV-0001..0005), **no new JV was created**.

**RESULT: PASS** on both guards (inactive-account exclusion, future-date refusal). Minor UX note (not a functional bug): the future-date rejection is server/submit-time only, not reflected by disabling the Save button up front the way the balance check is — a worker could type a whole entry before discovering it will be rejected. Not a correctness defect, just an earlier-feedback opportunity.

## Step 6 — Chart of accounts

Opened `/settings/chart-of-accounts` on co7 — confirmed all 3 new seeded accounts present and correct:
- **2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น** (Director & Shareholder Loan) — หนี้สิน (Liability), CR
- **5500 ดอกเบี้ยจ่าย** (Interest Expense) — ค่าใช้จ่าย (Expense), DR
- **4300 รายได้อื่น** (Other Income) — รายได้ (Revenue), CR

Created test account **9999 บัญชีทดสอบ swarm / Swarm Test Account** (สินทรัพย์/DR) via "เพิ่มบัญชี" — saved successfully, confirmed it lists (searched "swarm", row appears with correct code/name/type). Only action icon in every row (all 30+1 accounts, both pages) is a pencil "แก้ไข" (edit) icon — **no delete/trash icon anywhere in the table**. Opening the edit modal for the test account additionally surfaced explicit product copy confirming this is by design:
> "การปิดใช้งานคือวิธีที่ปลอดภัย — บัญชีที่มีการบันทึกบัญชีแล้วไม่สามารถลบได้"
> ("Deactivating is the safe way — an account that already has journal entries recorded cannot be deleted.")
(Used this same edit modal's "ใช้งาน" checkbox to deactivate the test account for the step-5 inactive-account dropdown test above.)

**RESULT: PASS.** All 3 seeded accounts present, test account creation + listing works, no delete affordance anywhere (by design, confirmed in-product).

## Step 7 — Immutability

Opened the posted director-loan JV `/journals/180` (07-2026-JV-0003) from `/journals`. Visually: page shows only document header fields (date, description, status "Posted", recorded date) and the line table — no buttons at all besides global nav/header icons. Confirmed via `read_page` (interactive-elements filter): the ONLY interactive elements on the page are the notification bell, settings link, sidebar nav links, language toggle, and logout — **zero edit or delete affordance** for the journal itself.

**RESULT: PASS.** Posted journal voucher is fully read-only/immutable in the UI, matching the "no void, no delete, everything posted is permanent" design.

## Findings summary

| Step | Result |
|---|---|
| 0. Login (co7 / nvadmin02, v1.25.0) | PASS |
| 1. Director loan JV — before/after TB, P&L, Balance Sheet | **PASS** — bank +100,000.00 exactly, 2190 +100,000.00 exactly, net profit UNCHANGED (-115,953.07 both), 2190 shown as liability |
| 2. Non-sales income JV | **PASS** — net profit moved by exactly +5,000.00 (contrast case confirms step 1 was real) |
| 3. Split-entry float case (33.33/33.33/33.34/100.00) | **PASS** — balanced footer + Save enabled together (the regression this fix targets) |
| 4. Unbalanced (100/90) refused | **PASS** — Save disabled, red diff, clear Thai message, no JV created |
| 5. Guards — no inactive accounts in dropdown, future date refused | **PASS** — both actively tested (deactivated a live test account and watched it vanish from the dropdown; future-dated balanced entry rejected server-side with a clear Thai toast) |
| 6. Chart of accounts — 2190/5500/4300 present, create + list, no delete | **PASS** |
| 7. Immutability — posted JV has no edit/delete | **PASS** |

No CRITICAL findings. No tenant leaks observed (all data stayed within co7 / บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด throughout). No 500s, stack traces, blank pages, or raw i18n keys encountered in any screenshot taken across all 7 steps.

**MINOR (non-blocking) UX observations, not correctness defects:**
1. The future-date guard (step 5) is enforced only at submit time via a toast, not by pre-disabling Save the way the balance check does — a user can fill an entire entry before learning it will be rejected for its date. Consider disabling Save (or warning) as soon as an out-of-range date is picked.
2. (Automation-only, not a product bug) The `/journals/new` form's line-item block changes height as soon as an account dropdown is populated (an extra row of vertical space appears under some conditions). Not a UI bug — just means script-driven testing must re-screenshot before each subsequent click rather than blind-batching coordinates across a dropdown-selection boundary.

**Test artifacts left in co7 (per spec's "post only what these steps need" — all authorized, immutable, cannot be cleaned up):**
- JV 07-2026-JV-0003 (director loan, 100,000.00) — the core deliverable of this leg
- JV 07-2026-JV-0004 (non-sales income, 5,000.00)
- JV 07-2026-JV-0005 (split-entry float test, 100.00)
- 2 blocked/rejected attempts (unbalanced 100/90, future-dated 50/50) — correctly did NOT create any JV
- Account 9999 "บัญชีทดสอบ swarm" — created active, then deactivated (สถานะ = inactive) to test the dropdown guard; left inactive, harmless

## Screenshots (saved to disk)
- Posted director-loan JV (#180, 07-2026-JV-0003): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785314622053-31.jpg`
- Balance Sheet showing 2190 as a liability (฿100,000.00): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785314672154-32.jpg`
- Split-entry float case, balanced + Save enabled (before submit): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785314963249-33.jpg`

STATUS: COMPLETE — all 7 steps executed and verified live on prod (teas.kazaki-rio.com, v1.25.0).
