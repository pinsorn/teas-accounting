# Leg B — O10 (payroll deductions) + O11-alt (สปส.1-10 ส่วนที่ 2 on-screen schedule) — co7 live prod verification

- **Env:** https://teas.kazaki-rio.com (prod), v1.24.1
- **Company:** co7 — บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด
- **User:** nvadmin02
- **Date of run:** 2026-07-29
- **Tester:** browser automation (claude-in-chrome), live prod, no source touched
- **Runs used:** #11 (08/2026, doc `08-2026-PR-0001`) — new draft created this session,
  carries the test deduction. #10 (07/2026, doc `07-2026-PR-0001`) — pre-existing posted
  run from O8 proration testing, used for the prorated-employee check (step 11) and a
  second sample for the ส่วนที่ 1/ส่วนที่ 2 invariant.
- Employee names on co7 render literally as `??????? ?????` everywhere (payroll screen,
  employee master list `/settings/employees`, and inside the generated PDFs themselves).
  Confirmed this is the company's actual stored test data, not a rendering bug — the
  master employee list shows the same placeholder text. Not a finding.

## Summary verdict

**Both features PASS.** O10's deduction correctly reduces only net pay (gross/PIT/SSO
untouched), the cap is enforced with a clear Thai message, the posted JE balances with
an exact 2180 credit line, the ภ.ง.ด.1 filing is fully isolated from the deduction, and
the payslip shows the required deduction line. O11-alt's on-screen SSO schedule renders
only for posted runs, its totals match the สปส.1-10 ส่วนที่ 1 PDF exactly, the prorated
employee shows prorated wage (not full salary), formatting matches spec, and the batch
file download works. One step (13, print preview) could not be safely verified — the
print button triggers a native OS print dialog that froze the automated browser tab;
recovered by closing the tab, no data was affected.

## Part 1 — O10 payroll deductions

### Step 1 — Create a new draft payroll run — PASS
Created run 08/2026 via "สร้างรอบจ่าย" (period auto-suggested 202608, pay date
2026-08-30). Result: draft run #11, 2 employees (O8FULL, O8MID; a third employee
O8OUT did not appear — consistent with O8OUT being a terminated/prorated-out
employee from the O8 test setup, not a bug).

### Step 2/3 — Add a deduction, verify isolation — PASS
Baseline for employee **O8FULL** before deduction: gross (เงินได้) ฿60,000.00, PIT
(ภาษี) ฿372.92, SSO employee (ปกส.) ฿875.00, net (รับสุทธิ) ฿58,752.08.

Entered deduction ฿500.00, reason `เรียกคืนเงินจ่ายเกิน`, saved via "บันทึกรายการหัก".
After save:
- O8FULL: gross ฿60,000.00 (**unchanged**), PIT ฿372.92 (**unchanged**), SSO ฿875.00
  (**unchanged**), net **฿58,252.08** — dropped by exactly ฿500.00.
- Run totals: รวมเงินได้ ฿120,000.00 (unchanged), ภาษีหัก ฿549.45 (unchanged),
  ประกันสังคม ฿3,500.00 (unchanged), รับสุทธิ dropped **117,700.55 → 117,200.55**
  (exactly -500.00).

**PASS** — deduction affects only net pay; taxable base and SSO base are untouched.

### Step 4 — Cap check — PASS
O8FULL's cap = gross − PIT − SSO = 60,000 − 372.92 − 875.00 = **58,752.08**. Entered
a deduction of 60,000 (larger than the cap) and attempted save. Result: **rejected**,
red toast:

> **"จำนวนเงินหักของพนักงาน O8FULL ต้องไม่เกินเงินได้สุทธิหลังภาษีและประกันสังคม
> 58,752.08 บาท"**
> ("The deduction amount for employee O8FULL must not exceed net income after tax
> and social security of 58,752.08 baht.")

The cap value in the message (58,752.08) matches the computed cap exactly. The
persisted net stayed at ฿58,252.08 (the prior valid 500 deduction) — **no negative
net pay was saved, no silent save occurred.** Reverted the field to 500 and re-saved
successfully before proceeding.
Screenshot: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785265980313-8.jpg`

### Step 5 — Approve, post, verify the journal entry — PASS (the money-critical assertion)
Approved ("อนุมัติ") then posted ("บันทึกบัญชี", confirmed the in-app dialog "ยืนยันบันทึกบัญชี?").
Run became doc `08-2026-PR-0001`, status "จ่ายแล้ว". Journal entry `08-2026-JV-0001`:

| Account | Description | Debit | Credit |
|---|---|---|---|
| 5400 เงินเดือนและค่าจ้าง | Salaries 08-2026-PR-0001 | 120,000.00 | |
| 5410 เงินสมทบประกันสังคม-นายจ้าง | Employer SSO | 1,750.00 | |
| 2153 ภ.ง.ด.1 หัก ณ ที่จ่ายค้างนำส่ง | PIT payable | | 549.45 |
| 2160 เงินสมทบประกันสังคมค้างนำส่ง | SSO payable | | 3,500.00 |
| 2170 เงินเดือนค้างจ่าย | Net wages payable | | 117,200.55 |
| **2180 เงินหักจากพนักงานค้างนำส่ง** | **Other deductions payable** | | **500.00** |
| **Total** | | **121,750.00** | **121,750.00** |

**Dr = Cr exactly (121,750.00 = 121,750.00).** Account 2180 carries a credit of
exactly the deduction total (500.00), confirmed independently via the General Ledger
report (`บัญชีแยกประเภท`, account 2180, Aug 2026: one line, credit ฿500.00, running
balance ฿500.00). Total debits (121,750.00 = 120,000 salary + 1,750 employer SSO) are
unaffected by the deduction — verified algebraically: without the deduction, 2170
would be 117,700.55 and 2180 would be 0; with it, 2170 is 117,200.55 and 2180 is
500.00 — the deduction only moves value between the two **credit** lines 2170→2180,
debits are untouched.
Screenshot: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785266334414-9.jpg`

### Step 6 — Tax-filing isolation (ภ.ง.ด.1) — PASS
Downloaded the run's ภ.ง.ด.1 PDF (`GET /api/proxy/payroll/runs/11/pnd1/pdf`). Summary
page: 2 persons, เงินได้ทั้งสิ้น (total income) **120,000.00**, ภาษีที่นำส่งทั้งสิ้น
(total tax) **549.45**. Attachment (ใบแนบ) detail: employee 1 (O8FULL) เงินได้ **60,000.00**,
ภาษี **372.92** — the exact pre-deduction gross and PIT figures, with **no trace of
the 500 deduction anywhere** in the filing (no reduced income, no adjusted tax, no
reference to the deduction). **PASS** — deduction is fully invisible to the RD filing.

### Step 7 — Payslip PDF — PASS
Downloaded the payslip PDF (`GET /api/proxy/payroll/runs/11/payslips/10/pdf`) for
O8FULL. Contains, in order:
```
เงินเดือน / ค่าจ้าง (ม.40(1))              60,000.00
รวมเงินได้                                  60,000.00
หัก ภาษีเงินได้หัก ณ ที่จ่าย (ภ.ง.ด.1)        -372.92
หัก เงินสมทบประกันสังคม (ลูกจ้าง)              -875.00
หัก รายการหักอื่น ๆ (เรียกคืนเงินจ่ายเกิน)      -500.00
เงินได้สุทธิ (รับจริง)                       58,252.08
```
The exact reason text `เรียกคืนเงินจ่ายเกิน` appears inside the parentheses as
required. **PASS.**

### Step 8 — Draft-only rule (posted run is immutable) — PASS
On the posted run #11, `read_page` (interactive-element filter) shows **zero**
textbox/input elements anywhere on the page and **no** "บันทึกรายการหัก" save button
— only view/print/PDF buttons remain (ภ.ง.ด.1, สปส.1-10 ไฟล์/PDF, พิมพ์ payslip,
50ทวิ). The "รายการหัก"/"เหตุผล" columns render as static text (฿500.00 /
เรียกคืนเงินจ่ายเกิน), not editable fields. **PASS** — the deduction cannot be edited
after posting.

## Part 2 — O11-alt: สปส.1-10 ส่วนที่ 2 on-screen schedule

### Step 9 — Schedule renders only for posted runs — PASS
The section "ตารางผู้ประกันตน (สปส.1-10 ส่วนที่ 2)" appeared automatically on run #11
the moment it was posted (it was not present while the run was still a draft). Also
confirmed present on run #10 (07/2026), the pre-existing posted run.

### Step 10 — Invariant: schedule totals = สปส.1-10 ส่วนที่ 1 PDF — PASS (both runs checked)

**Run #11 (08/2026, 2 employees, no proration):**
| | On-screen ส่วนที่ 2 | สปส.1-10 ส่วนที่ 1 PDF |
|---|---|---|
| Employee count | 2 | 2 |
| ค่าจ้าง / เงินค่าจ้างทั้งสิ้น | 120,000.00 | 120,000.00 |
| เงินสมทบผู้ประกันตน | 1,750.00 | 1,750.00 |
| เงินสมทบนายจ้าง | 1,750.00 | 1,750.00 |

All four figures match **exactly**. (The PDF's own ส่วนที่ 2 page — the official RD
paper template — renders completely blank/unfilled, which matches the task's stated
premise: the official template has no auto-fillable ส่วนที่ 2, which is exactly why
the on-screen schedule exists for manual transcription.)

**Run #10 (07/2026, 3 employees, includes proration) — cross-check:**
On-screen ส่วนที่ 2 totals: 3 คน, ค่าจ้าง 112,258.07, เงินสมทบผู้ประกันตน 2,625.00,
เงินสมทบนายจ้าง 2,625.00. Run header ประกันสังคม (รวมนายจ้าง) = 5,250.00 =
2,625.00 + 2,625.00, consistent.

Both samples confirm the invariant holds. **PASS.**

### Step 11 — Prorated employee shows prorated wage — PASS
Run #10 (07/2026), on-screen schedule:
| ลำดับ | Employee | ค่าจ้าง |
|---|---|---|
| 1 | O8FULL | 60,000.00 (full month) |
| 2 | **O8MID** | **32,903.23** (prorated — mid-month joiner) |
| 3 | O8OUT | 19,354.84 (prorated — mid-month leaver) |

O8MID and O8OUT both show the **actual prorated wage paid**, not the full ฿60,000.00
base salary. **PASS.**

### Step 12 — National ID/SSO number formatting — PASS
Zoomed screenshot of the schedule table: เลขประกันสังคม and เลขบัตรประชาชน render as
continuous ungrouped 13-digit strings (e.g. `1103700000011`, no dashes/spaces) in a
tabular/monospace-style numeral font. Money columns (ค่าจ้าง, เงินสมทบผู้ประกันตน,
เงินสมทบนายจ้าง) are right-aligned, comma-grouped, and show unrounded 2-decimal
values (e.g. `32,903.23`, not rounded to a whole baht). **PASS.**

### Step 13 — Print preview — BLOCKED (could not safely verify)
Clicking "พิมพ์" next to the SSO schedule table triggered a **native OS print dialog**
that froze the automated Chrome tab entirely: subsequent screenshot, `get_page_text`,
and `javascript_tool` calls all timed out (30–45s), consistent with the
claude-in-chrome skill's documented risk that native dialogs block CDP entirely (worse
than the known "screenshot timeout, page still responsive" class in `troubles-wiki.md`
— here even `Runtime.evaluate` timed out, meaning the renderer was genuinely blocked,
not just the screenshot channel). Recovered by closing the tab (`tabs_close_mcp`) and
opening a fresh one — no data was affected, nothing was sent to a physical printer.
**Did not retry** per the "stop after 2-3 attempts" guidance. **This step needs a
manual check by a human** (or a follow-up run with print dialogs suppressed at the
Chrome-profile level) to confirm the print view hides the app chrome cleanly.

### Step 14 — SSO batch file download — PASS
Clicked "สปส.1-10 (ไฟล์)" next to the schedule. Network: `GET
/api/proxy/payroll/runs/10/sso/file` → **200**, downloaded a 548-byte fixed-width
text file. Content includes a header record (`1`) with company name and totals
(3 employees, wages 112,258.07 encoded as `0000000112258070`-style fixed-width
integer-cents, contributions 2,625.00/2,625.00) followed by 3 employee detail records
(`2`) each with the 13-digit ID, wage, and per-employee contribution — matching the
on-screen schedule. File encoding is a legacy Thai codepage (renders as mojibake in a
UTF-8 terminal), consistent with the RD/SSO fixed-width upload format requirement
noted in `troubles-wiki.md` (byte-exact, non-UTF-8 formats are expected here). **PASS**
— the file downloads successfully with correct structure.

## Tenant-isolation check
Throughout this entire leg, the only company name ever seen on screen or in any
generated document was **"บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด"** (co7). No other
company's data (นาย พงศ์สันต์ / เรปทาวน์ / co2 / co5 / co6) appeared at any point.
**No tenant leak.**

## Findings summary

| # | Severity | Finding |
|---|---|---|
| 1 | Info (not a bug) | Employee names on co7 render as literal `??????? ?????` placeholder text everywhere, including inside generated PDFs — confirmed this is the company's actual stored master data, not a display/encoding bug. |
| 2 | Info (expected) | The official สปส.1-10 PDF's own ส่วนที่ 2 page is blank by design (per task's stated premise) — the on-screen schedule is the intended substitute. |
| 3 | Low / environment limitation | Step 13 (print preview) could not be verified live — the print button opens a native OS print dialog that freezes claude-in-chrome browser automation. Recommend a human manually click "พิมพ์" and confirm the print view hides the sidebar/header chrome. No evidence of an app-side defect; this is a browser-automation tooling limitation. |

No CRITICAL or HIGH severity findings. Both O10 and O11-alt are working correctly on
production for co7, with the single money-critical invariant (Dr=Cr, 2180 credit
line, tax-filing isolation) fully verified with exact numbers.

## Screenshots
- Cap refusal (step 4): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785265980313-8.jpg`
- Posted JE showing 2180 credit (step 5): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785266334414-9.jpg`
- Posted run #11 with deduction + SSO schedule (steps 5/8/9/10): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785267575975-10.jpg`
- Payslip deduction line and ภ.ง.ด.1 figures: read directly from downloaded PDFs (see
  step 6/7 tables above); PDF files retained at
  `Z:\temp\claude\Y--ClaudePlayground-TEAS-Project\6ade7177-9d1b-48a5-9bdf-17755afca153\scratchpad\pdfs\`
  (`pnd1-run11.pdf`, `payslip-run11-emp10.pdf`, `sso-run11.pdf`, `sso-file-run10.txt`)
  for the orchestrator to spot-check if desired.

## Note on method
Several PDFs referenced above (ภ.ง.ด.1, payslip, สปส schedule, สปส batch file) render
in Chrome as `blob:` URLs that claude-in-chrome's screenshot/read tools cannot
interact with ("browser-internal or unparseable URL"). To read their exact figures I
used `javascript_tool` to `fetch()` the same authenticated API endpoint the UI button
calls, saved the resulting PDF/file to this session's scratchpad directory (not the
user's real Downloads folder — cleaned up any transient `.tmp` artifacts that landed
there), and read it with the `Read` tool. No data was sent anywhere external; this was
purely to extract the on-screen/in-document figures needed for this verification
report.
