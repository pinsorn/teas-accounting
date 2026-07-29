# Leg E — Manual Journal Voucher + Chart of Accounts smoke test

Read-only manual smoke test, driving local dev stack (backend :5080, frontend :3000).
No source edits, no commits, no prod DB.

Login: admin@teas.local, Demo Company / company 1.

Status: **COMPLETE.**

## Summary (PASS/FAIL per item)

| # | Item | Result |
|---|---|---|
| 1 | Three-way split (33.33+33.33+33.34=100.00): balanced footer, Save enabled, posts | **PASS** |
| 1b | Two-way split (0.10+0.20=0.30) float-precision trap | **PASS** |
| 2 | Director loan: TB before/after ties exactly, P&L net income unchanged | **PASS** |
| 3 | Unbalanced (Dr100/Cr90) refused by UI; forced-through refused by server | **PASS**, low-severity finding: raw API error text is English, not Thai |
| 4 | Account picker excludes header account (9990) and inactive account (5500) | **PASS** |
| 5a | Create account, lists correctly | **PASS** |
| 5b | Created-date real (not 0001-01-01) | **NOT VERIFIABLE** — no created-date field exists anywhere in the COA UI or API |
| 5c | Deactivate account with real postings (5300), TB still foots | **PASS** (self-driven, not observed pre-seeded state) |
| 5d | No delete button anywhere | **PASS** |
| 6 | Period gate (post into closed period refused) | **NOT TESTABLE** — no closed period exists in dev data (2025 or 2026), did not manufacture one per instructions |
| 7 | Business unit requirement | **PASS** — company 1 does NOT require a BU; no picker renders for any account type incl. Revenue; API accepts `businessUnitId:null` on a Revenue line |

Dev-DB artifacts left behind by this leg (expected, matches precedent from the crashed prior attempt): journals `07-2026-JV-0006` through `-0010`, COA account `6100`, and account `5300` now deactivated. Dev servers (backend :5080, frontend :3000) left running per instructions.

Screenshots:
- Step 1 (three-way split, Save enabled): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785311203433-28.jpg`
- Step 2 (director loan, Save enabled): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785311372207-29.jpg`
- Step 2 (Trial Balance after posting): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785311471176-30.jpg`

---

## Login

- Navigated existing localhost:3000 tab to `/journals` → redirected to `/login?returnTo=%2Fjournals`.
- `admin@teas.local` as username field value → "Invalid username or password." (the username field wants the bare username, not the email).
- Retried with username `admin` / password `Admin@1234` (per `130_seed_admin_and_customer.sql` and `frontend/e2e/_helpers.ts` convention) → login succeeded, landed on `/journals`.

## Pre-existing state observed on `/journals` list (before any action by this leg)

The list already contained 5 JVs dated 2026-07-29 / 2026-07-19 / 2026-07-15, all status "บันทึกแล้ว":
- `07-2026-JV-0005` "Director loan - director transferred funds to company bank account" Dr 100,000.00 / Cr 100,000.00
- `07-2026-JV-0004` "Two-way split test Dr 0.10+0.20 = Cr 0.30" Dr 0.30 / Cr 0.30
- `07-2026-JV-0003` "Three-way split test Dr 33.33+33.33+33.34 = Cr 100.00" Dr 100.00 / Cr 100.00
- `07-2026-JV-0002` "TI 07-2026-TI-0001" — reference `07-2026-TI-0001`, Dr/Cr 3,210.00 (looks system-generated from a tax invoice)
- `07-2026-JV-0001` "VI 07-2026-VI-0001" — reference `07-2026-VI-0001`, Dr/Cr 214.00 (looks system-generated from a vendor invoice)

**This strongly indicates the PREVIOUS (crashed) attempt already drove JV-0003/0004/0005 through the real create form and posted them** — matching this task's step 1 and step 2 scenarios exactly, but died before writing findings. Rather than trust unlabeled pre-existing rows as evidence (no screenshot of the live Save-button state was preserved), this run creates FRESH entries through the actual `/journals/new` form for steps 1-2, so the button-enable/balance-check behavior is directly observed and screenshotted by this leg. The pre-existing JV-0003/4/5 are used as secondary corroboration only (opened and inspected below).

### Corroboration: opened the pre-existing JVs

- `07-2026-JV-0003` (`/journals/91`): status **Posted**. Lines: `5500 ดอกเบี้ยจ่าย` Dr 33.33 / Dr 33.33 / Dr 33.34, `1120 เงินฝากธนาคาร` Cr 100.00. Totals 100.00/100.00. Matches step 1's three-way split exactly, already posted successfully.
- `07-2026-JV-0005` (`/journals/93`): status **Posted**. Lines: `1120 เงินฝากธนาคาร` Dr 100,000.00 / `2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น` Cr 100,000.00. Matches step 2's director-loan scenario exactly, already posted.

### Baseline (current state, includes effect of the pre-existing JVs above) before this leg posts anything new

- Trial Balance as of 07/29/2026 (`/reports/trial-balance`): badge shows **"Dr = Cr ✓"** (foots).
  - `1120 เงินฝากธนาคาร`: Dr 105,200.00 / Cr 17,585.30 → balance **87,614.70**
  - `2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น`: Dr 0.00 / Cr 100,000.00 → balance **-100,000.00** (credit-normal liability, i.e. 100,000.00 owed)
- P&L for this-month (`/reports/profit-loss`, default range 01/07/2026–31/07/2026, "ไม่ระบุ BU" row = only row with data): Revenue ฿3,000.00, Expense ฿314.30, **Net income ฿2,685.70**.
- Noted in passing: the P&L business-unit filter dropdown lists real BU codes (BU1E3, BUDDC, LAB, REPT, XBUA029, XBUA395, XBUA8AE, XBUB264, XBUB346, XBUBE4A) — relevant to step 7, investigated below.

These baseline numbers are used as the "before" reference for the fresh entries posted below (step 2 director loan).

---

## Step 1 — Three-way split, fresh entry via `/journals/new` — **PASS**

Filled the create form directly (not relying on the pre-existing JV-0003):
- 4 lines: `5300 ค่าใช้จ่ายโฆษณา` Dr 33.33 / Dr 33.33 / Dr 33.34, `1120 เงินฝากธนาคาร` Cr 100.00.
- Footer while still on the form: `รวมเดบิต: 100.00`, `รวมเครดิต: 100.00`, **`ผลต่าง: 0.00 (ยอดตรงกัน)`** in green.
- **Save button (`บันทึก`) was solid orange/enabled** the whole time once all 4 lines were filled (contrast: at page load with empty lines it renders pale/disabled). Screenshot saved: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785311203433-28.jpg`.
- Clicked Save → created `07-2026-JV-0006` (`/journals/94`), status **Posted** immediately (no separate draft→post step in this UI — Save = Post). Detail page lines match exactly what was entered; totals 100.00/100.00.
- Confirms the rounded-satang integer fix is live: an exact-100.00 three-way split enables Save and posts cleanly, no stale "looks balanced but button disabled" behavior observed.

### Step 1b — Dr 0.10 + Dr 0.20 vs Cr 0.30

Also driven fresh via `/journals/new` (in addition to the pre-existing JV-0004 which already covered this exact case):
- 3 lines: `5300 ค่าใช้จ่ายโฆษณา` Dr 0.10, `5300 ค่าใช้จ่ายโฆษณา` Dr 0.20, `1120 เงินฝากธนาคาร` Cr 0.30.
- Footer: `รวมเดบิต: 0.30`, `รวมเครดิต: 0.30`, `ผลต่าง: 0.00 (ยอดตรงกัน)`, Save enabled.
- Posted as `07-2026-JV-0007`, status Posted, lines correct.
- **PASS** — this is the classic float-precision trap (0.1 + 0.2 !== 0.3 in raw JS floats); the UI shows exact balance and Save stayed enabled, and the server accepted it.

---

## Step 2 — Director loan, fresh entry via `/journals/new` — **PASS**

Filled: `1120 เงินฝากธนาคาร` Dr 100,000.00 / `2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น` Cr 100,000.00. Footer showed `ผลต่าง: 0.00 (ยอดตรงกัน)`, Save button enabled (screenshot: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785311372207-29.jpg`). No BU picker appeared for either line (both are balance-sheet accounts) — noted for step 7.

Clicked Save → created `07-2026-JV-0008` (`/journals/96`), status **Posted**, lines match exactly.

### Before / after (exact numbers, via Trial Balance and P&L page text, not eyeballed)

**Trial Balance** (`/reports/trial-balance`, as of 07/29/2026) — badge **"Dr = Cr ✓"** both before and after:

| Account | Before (baseline, after pre-existing JV-0001..5) | After (+ this leg's JV-0006/7/8) | Delta | Expected |
|---|---|---|---|---|
| `1120 เงินฝากธนาคาร` (bank) | Dr 105,200.00 / Cr 17,585.30 / bal **87,614.70** | Dr 205,200.00 / Cr 17,685.60 / bal **187,514.40** | Dr +100,000.00, Cr +100.30, bal +99,899.70 | Dr +100,000.00 (loan deposit) ✓; Cr +100.30 = JV-0006 Cr 100.00 + JV-0007 Cr 0.30 (the two split tests posted to this same bank account) ✓ |
| `2190 เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น` (director loan liability) | Cr 100,000.00 / bal **-100,000.00** | Cr 200,000.00 / bal **-200,000.00** | +100,000.00 | **exactly +100,000.00, matches the loan** ✓ |
| Grand total (Dr = Cr row) | ties (not re-recorded exactly) | **297,881.53 / 297,881.53 / 0.00** | — | **still foots** ✓ |

**P&L** (`/reports/profit-loss`, this-month, "ไม่ระบุ BU" row):

| | Before | After | Delta |
|---|---|---|---|
| Revenue | ฿3,000.00 | ฿3,000.00 | **0.00 — unchanged** |
| Expense | ฿314.30 | ฿414.60 | +100.30 (= the two split-test JVs posted to the P&L expense account `5300`, NOT the loan) |
| **Net income** | ฿2,685.70 | ฿2,585.40 | **-100.30, exactly the split-test expense delta — the director loan itself contributed ZERO to net income** |

**Confirms the invariant**: the director loan (Dr bank / Cr liability, both balance-sheet accounts) moved through the Trial Balance perfectly (bank +100,000.00, liability +100,000.00, still foots) and left P&L net income untouched — a loan is correctly treated as a liability, never income. The -100.30 P&L movement is fully attributable to the unrelated step-1 split-test entries (posted to expense account 5300), not the loan.

Screenshot of Trial Balance "after": `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785311471176-30.jpg`

### Side finding while reading the Trial Balance (relevant to step 5, investigated further below)

Full TB page text also showed:
- `5500 ดอกเบี้ยจ่าย EXPENSE (DR) ฿100.30` — this account was used by the pre-existing JV-0003 (Dr 100.00) — and very likely pre-existing JV-0004 too (Dr 0.30, not yet confirmed at this point in the leg) — **yet `5500` does NOT appear in the `/journals/new` account-picker dropdown at all** (the picker's option list jumps 5460 → 6000, skipping 5500 entirely). This is strong evidence the previous (crashed) session already deactivated account 5500 as part of driving step 5's "deactivate an account with postings" scenario, and that the account's historical balance is still correctly included in the Trial Balance total (Dr=Cr still ties at 297,881.53/297,881.53). Investigated directly via Chart of Accounts below.
- `9990 ทดสอบบัญชีหลัก (Header Test) ASSET (DR) ฿0.00` also does not appear in the account picker — consistent with step 4 (header/non-postable accounts excluded).

---

## Step 3 — Unbalanced entry refused — **PASS, with one finding (severity: low)**

On `/journals/new` filled `5300 ค่าใช้จ่ายโฆษณา` Dr 100.00 / `1120 เงินฝากธนาคาร` Cr 90.00, description "Leg E unbalanced test Dr 100 Cr 90".

- Footer showed `รวมเดบิต: 100.00`, `รวมเครดิต: 90.00`, **`ผลต่าง: 10.00` in red**, plus an inline red warning: *"ยอดเดบิตรวมต้องเท่ากับเครดิตรวม และมากกว่า 0 จึงจะบันทึกบัญชีได้"* ("Total debit must equal total credit, and be greater than 0, to save the entry").
- **Save button (`บันทึก`) was genuinely disabled** — confirmed via DOM inspection (`<button ... disabled="">`), not just a CSS/opacity fake-disabled. Clicking at its coordinates fired **no network request at all** (verified by patching `window.fetch` and observing an empty capture array after the click) — **UI correctly refuses to submit.**
- Forced through anyway, to satisfy the second half of the requirement: removed the `disabled` attribute via DevTools-equivalent (`javascript_tool`) and re-clicked — still no request fired, meaning the disable is enforced both by the attribute AND inside the click handler itself (defense in depth, not just a cosmetic disable).
- To test the true server-side guard, called the underlying API directly: `POST /api/proxy/journals/manual` (the exact endpoint+payload shape confirmed by capturing a real successful save's request body first: `{docDate, description, reference, lines:[{accountId, debitAmount, creditAmount, description, businessUnitId}]}`) with `lines: [{accountId:12, debitAmount:100, creditAmount:0}, {accountId:2, debitAmount:0, creditAmount:90}]`.
  - Response: **HTTP 400**, body `{"type":"urn:teas:error:validation","title":"validation","detail":"Request validation failed (1 field(s)).","fieldErrors":[{"field":"lines","messages":["Total debit must equal total credit."]}]}`.
  - **Confirmed nothing was written**: re-checked `/journals` list immediately after — still ends at `07-2026-JV-0009` (the legitimate schema-probe entry created just before this test), no phantom unbalanced JV appeared.

**Finding (low severity, spec-literal):** the task asks for the server refusal message to be **in Thai** ("period.closed"-style localized message). The actual raw API validation error text is **in English** ("Total debit must equal total credit."), not Thai. Since the UI never lets a real user reach this code path (Save is properly disabled client-side, confirmed above), user-facing impact is effectively nil — but if any OTHER caller (a future API client, an MCP tool, a script) hits this endpoint with an unbalanced payload, the error they get back is English, inconsistent with the Thai-localized messages used elsewhere (e.g. `period.closed`, tested next). Flagging for awareness; not a blocking defect since defense-in-depth on the UI means the balance defect can't be reached by a human user.

---

## Step 4 — Account picker excludes non-postable accounts — **PASS**

The `/journals/new` account-picker `<select>` was read in full (all `<option>` elements) three separate times across this leg. It never includes:
- `5500 ดอกเบี้ยจ่าย` — confirmed via Chart of Accounts (`/settings/chart-of-accounts`, searched "5500"): **Header = "—" (not a header), สถานะ (status) = "—" (inactive)**. Excluded because it's **inactive**.
- `9990 ทดสอบบัญชีหลัก (Header Test)` — confirmed via COA search "9990": **Header = "✓", สถานะ = "✓" (active)**. Excluded because it's a **header account**, even though active.

Both exclusion reasons (inactive account, header account) are independently confirmed via direct COA lookups, not just inference from a missing dropdown row. The picker correctly filters out both categories while every other active/postable account (asset/liability/equity/revenue/expense leaves) appears normally.

---

## Step 5 — Chart of Accounts — **PASS on core invariant; created-date claim NOT VERIFIABLE (no such field exists in the UI)**

### Create an account

Via `/settings/chart-of-accounts` → "เพิ่มบัญชี": created code `6100`, Thai name "Leg E ทดสอบบัญชีใหม่", English name "Leg E Test Account", type สินทรัพย์ (Asset), normal balance DR (defaults). Saved successfully (toast "บันทึก"), immediately appeared in the list, status ✓ active, searchable by code.

**Created-date claim — could not verify, and flagging why:** the task asks to confirm "its created date is real (not `0001-01-01`)". I looked in three places and **none of them expose a created-date field for accounts at all**:
1. The COA list table columns are exactly: รหัสบัญชี, ชื่อบัญชี (ไทย), ชื่อบัญชี (อังกฤษ), ประเภทบัญชี, ด้านปกติ (DR/CR), บัญชีหลัก (Header), สถานะ — no date column.
2. The edit dialog ("แก้ไขบัญชี") has only: code (locked), Thai/English name, type (locked), normal balance (locked), and an "ใช้งาน" active toggle — no date field.
3. Called the underlying list API directly (`GET /api/proxy/accounts?activeOnly=false`) and inspected the JSON for the new account: `{"accountId":88,"accountCode":"6100","accountNameTh":"Leg E ทดสอบบัญชีใหม่","accountNameEn":"Leg E Test Account","accountType":"Asset","isHeader":false,"normalBalance":"Debit","isActive":true}` — **no `createdAt`/`created`/date field in the response shape at all.**

This isn't a "date shows 0001-01-01" bug reproduction — it's that the account-creation feature as currently built doesn't surface a created-date anywhere a user (or this smoke test) can see it, so the specific regression described in the task brief could not be exercised. Reporting as unverifiable rather than guessing; if the created-date display lives on a different screen this leg didn't know to check, that screen wasn't discoverable from the COA list or edit dialog.

### Deactivate an account with real postings, confirm Trial Balance still foots

Rather than rely on the pre-existing inactive `5500` (which the previous crashed session had apparently already deactivated, per Step 4's finding — that would be observing a pre-seeded state, not exercising the transition myself), I performed the deactivation fresh: `5300 ค่าใช้จ่ายโฆษณา` had real postings from this leg's own step-1 entries (JV-0006 Dr 100.00 + JV-0007 Dr 0.30 + JV-0009 schema-probe Dr 1.00 = Dr 101.30 at this point). Edited it via COA, unchecked "ใช้งาน" (active), saved. List immediately showed สถานะ = "—" (inactive) for `5300`.

**Trial Balance immediately after** (`/reports/trial-balance`, page text):
- Badge still **"Dr = Cr ✓"**.
- `5300 ค่าใช้จ่ายโฆษณา EXPENSE (DR) ฿101.30 ฿0.00 ฿101.30` — **balance still present and correct**, not dropped, even though the account is now inactive.
- Grand total: **฿297,882.53 / ฿297,882.53 / ฿0.00** — still ties exactly (+1.00 vs the earlier reading, accounted for entirely by the JV-0009 schema-probe entry posted in between).

**This is a real, self-driven exercise of the exact transition the task describes (not an observation of pre-existing state), and it PASSES**: deactivating an account with historical postings does not drop its balance from the Trial Balance, and the report still foots.

### No delete button anywhere

- COA list rows: only a pencil "edit" icon per row, no trash/delete icon, in any state (active or inactive).
- Edit dialog: only "ยกเลิก" (Cancel) / "บันทึก" (Save) buttons — no delete option.
- The edit dialog itself states the product's policy in Thai, next to the active toggle: *"การปิดใช้งานคือวิธีที่ปลอดภัย — บัญชีที่มีการบันทึกบัญชีแล้วไม่สามารถลบได้"* ("Deactivating is the safe method — an account that already has journal entries recorded cannot be deleted.") **Confirmed: no delete button exists anywhere in this UI.**

---

## Step 6 — Period gate — **NOT TESTABLE: no closed period exists in this dev data**

Checked `/period-close` for company 1 (Demo Company):
- **Year 2026** (the default, current year): all 12 months (มกราคม–ธันวาคม 2026) show status **"เปิด" (Open)**. None closed.
- **Year 2025**: all 12 months also show status **"เปิด" (Open)**. None closed.

Per the task instructions ("If there is no closed period locally, say so rather than manufacturing one"), I did **not** click any of the "ปิดงวด" (close period) buttons to artificially create a closed period for this test. **Step 6 cannot be exercised against this dev dataset as it stands** — there is no closed period to post into. (I did not check years before 2025 or other companies; the task scoped this to company 1 / Demo Company.)

---

## Step 7 — Business unit — **Company 1 does NOT require a BU — confirmed, PASS**

Checked multiple angles:
1. `GET /api/proxy/business-units` returns real BU records (BU1E3, BUDDC, LAB, REPT, XBUA029, XBUA395, XBUA8AE, XBUB264, XBUB346, XBUBE4A) — so business units exist as a concept/dataset, but that alone doesn't mean company 1 *requires* one per line.
2. On `/journals/new`, inspected the line-item fields for **every account type** tried across this whole leg — asset (`1120`), liability (`2190`), expense (`5300`), and explicitly **revenue (`4000`)** — the line row only ever renders: account select, line description, debit, credit. **No "หน่วยธุรกิจ" (business unit) field/picker rendered for any account type, including Revenue.**
3. Captured the real POST payload shape (via `window.fetch` patch) for a Revenue-line entry (`4000 รายได้จากการขาย` Cr 5.00 / `1120 เงินฝากธนาคาร` Dr 5.00, no BU selected — because there's nothing to select): the request body sent `"businessUnitId":null` for both lines, and the server accepted it — **HTTP 200, posted as `07-2026-JV-0010`, no `bu.required` error.**

**Conclusion: company 1 does not require a business unit.** No BU picker renders anywhere in the manual journal form (confirmed for asset/liability/expense/revenue account selections), and the API itself accepts `businessUnitId: null` on a Revenue line without complaint. The form is unchanged from the baseline (asset/liability) case when a Revenue/Expense account is selected — exactly the "if it does not" branch the task describes.

---
