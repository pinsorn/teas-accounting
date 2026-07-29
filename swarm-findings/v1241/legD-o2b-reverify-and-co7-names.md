# Leg D — O2b re-verification on co5 (v1.24.2) + co7 employee-name repair

**Environment:** https://teas.kazaki-rio.com, prod, v1.24.2
**Tester:** Claude (browser automation, live production)
**Prior context:** Leg C (`legC-o2b-co5.md`, v1.24.1) found O2b's core scenario BLOCKED
client-side ("ต้องมีรายการอย่างน้อย 1 รายการ", no network request ever fired) because
the shared line-items grid always keeps one undeletable blank row, so the "no lines
supplied" path could never be reached and zod rejected the blank row before submit.
v1.24.2 is supposed to strip fully-blank rows before validation. `legX-co7-employee-names-corrupt.md`
previously confirmed (via `octet_length`) that co7's three O8 employees have literal
`?` bytes in `first_name_th`/`last_name_th`, real corruption from a PowerShell-originated
API write, not placeholder text — and recommended the UI repair performed in Part 2 below.

---

## PART 1 — O2b re-verify on co5

**Company:** co5 — บริษัท ทดสอบ VAT (DUMMY) จำกัด
**Login:** admin01

Document-type mapping unchanged from Leg C: "ใบแจ้งหนี้" at `/invoices` IS the billing
note (`/api/proxy/billing-notes` under the hood); "ใบกำกับภาษีที่รวม" is the tax-invoice
link field.

### Step 1 — Two posted tax invoices: PASS

| Invoice | Customer | Subtotal | VAT | Total | Status |
|---|---|---|---|---|---|
| 07-2026-TI-0020 (#30) | บริษัท ลูกค้าทดสอบ จำกัด | ฿4,500.00 | ฿315.00 | ฿4,815.00 | Posted |
| 07-2026-TI-0019 (#29) | บริษัท ลูกค้าทดสอบ จำกัด | ฿2,000.00 | ฿140.00 | ฿2,140.00 | Posted |
| **Expected sum** | | **฿6,500.00** | **฿455.00** | **฿6,955.00** | |

### Step 2/3 — Empty grid + both invoices linked → auto-generate: **PASS (fixed)**

Created a new ใบแจ้งหนี้, selected customer บริษัท ลูกค้าทดสอบ จำกัด, linked both
TI-0020 and TI-0019 via "ใบกำกับภาษีที่รวม", left the line grid completely untouched
(default 1 row, blank description, qty 1, price 0).

**The hint text "ระบบจะสร้างรายการจากใบกำกับภาษีที่เลือกเมื่อบันทึก" now renders** directly
under the linked-invoice chips as soon as an invoice is linked — yesterday (Leg C) it
never appeared under any condition. Confirmed by screenshot before saving.

Clicked "ออกใบแจ้งหนี้" (issue) — **it succeeded**, no validation error, one real network
request fired and returned 200, landing on the new document (doc #26, number
**07-2026-IV-0007**, status Issued).

**Two lines were generated, one per invoice, in Thai:**

| # | Description | Qty | Unit | Unit price | Amount |
|---|---|---|---|---|---|
| 1 | ใบกำกับภาษี 07-2026-TI-0020 ลงวันที่ 25/07/2026 | 1 | ฉบับ | 4,500.00 | 4,500.00 |
| 2 | ใบกำกับภาษี 07-2026-TI-0019 ลงวันที่ 25/07/2026 | 1 | ฉบับ | 2,000.00 | 2,000.00 |

**Tie-out (exact arithmetic):**

| | Generated note (IV-0007) | Sum of TI-0020 + TI-0019 | Match |
|---|---|---|---|
| Subtotal | 6,500.00 | 4,500.00 + 2,000.00 = 6,500.00 | ✅ |
| VAT | 455.00 | 315.00 + 140.00 = 455.00 | ✅ (no VAT-on-VAT — VAT is exactly the sum of the invoices' own VAT, not 7% recomputed on top) |
| Grand Total | 6,955.00 | 4,815.00 + 2,140.00 = 6,955.00 | ✅ |

Screenshots:
- `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785293621183-14.jpg` — IV-0007 header + linked-invoice chips + generated lines.
- `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785293629847-15.jpg` — IV-0007 totals block (Subtotal 6,500.00 / VAT 455.00 / Grand Total 6,955.00).

### Step 4 — Regression: invoice linked + manual line → manual line survives, nothing generated: PASS

New note, customer บริษัท ลูกค้าทดสอบ จำกัด, linked TI-0018 (฿107.00 total), then typed a
manual line ("manual override line - O2b regression", qty 1, price ฿500.00, VAT 7%) and
issued. Result: doc #27, **07-2026-IV-0008** — exactly **one line**, the manual one
(฿500.00 / VAT ฿35.00 / total ฿535.00). TI-0018's own amount (฿107.00) was **not**
pulled in and no second line was generated alongside the manual one.

### Step 5 — Regression: empty grid, NO invoice linked → still refused: PASS

New note, customer selected, no tax invoice linked, grid left at its default blank row,
clicked issue. Refused client-side with the same message as yesterday,
**"ต้องมีรายการอย่างน้อย 1 รายการ"**, and `read_network_requests` confirmed **zero**
network requests fired (cleared the log immediately before the click). The guard for
the true "nothing to bill" case still bites correctly — v1.24.2 did not weaken it.

### Step 6 — Regression: half-filled row (price only, no description), no invoice linked → refused, not silently dropped: PASS

Same document state as Step 5, then typed a price (2500) into the row while leaving the
description empty, no invoices linked, clicked issue. Refused with
**"กรุณากรอกข้อมูลให้ครบถ้วน"** (please complete all required fields) — a different but
equally valid rejection message. `read_network_requests` confirmed **zero** requests
fired. The half-filled row was caught, not silently dropped/ignored.

### Part 1 summary

| Step | Result |
|---|---|
| 1. Two posted tax invoices identified | PASS |
| 2/3. Empty grid + both invoices linked → 2 generated Thai lines, exact tie-out, no VAT-on-VAT, hint text renders | **PASS — the Leg C blocker is fixed** |
| 4. Override: manual line survives, nothing generated | PASS |
| 5. Empty grid, no invoice linked → still refused | PASS |
| 6. Half-filled row, no invoice linked → refused, not dropped | PASS |

**O2b is now fully working end-to-end on prod (v1.24.2).** All three regression guards
(override, empty-grid-refusal, half-filled-row-refusal) still hold.

Tenant isolation: only co5 data was observed throughout Part 1 (customer บริษัท
ลูกค้าทดสอบ จำกัด, its tax invoices TI-0018/0019/0020, and the new IV-0007/IV-0008). No leak.

---

## PART 2 — co7 employee name repair

**Company:** co7 — บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด
**Login:** nvadmin02

Edited each employee **through the UI** at `/settings/employees` (พนักงาน (Payroll) list),
via the pencil/edit action → modal → changed **only** ชื่อ (first name) and นามสกุล
(last name), left คำนำหน้า (title/prefix — also corrupted as `???`, but out of scope per
the task) and every other field (salary, dates, SSO/national ID numbers, bank details)
untouched, then saved:

| employee code | ชื่อ set to | สกุล set to | Result |
|---|---|---|---|
| O8FULL | เอหนึ่ง | ปกติ | Saved — list row now reads `???เอหนึ่ง ปกติ` |
| O8MID | บีหนึ่ง | เข้ากลางเดือน | Saved — list row now reads `???บีหนึ่ง เข้ากลางเดือน` |
| O8OUT | ซีหนึ่ง | ออกกลางเดือน | Saved — list row now reads `???ซีหนึ่ง ออกกลางเดือน` |

(The leading `???` is the untouched title/prefix field, corrupted the same way but
explicitly out of scope for this repair — confirmed intentional, not a miss.)

### Step 7 — Re-open a POSTED payroll run, check the SSO schedule table: PASS (with a legitimate snapshotting finding)

Opened run **07-2026-PR-0001** (07/2026, Posted, จ่ายแล้ว) at `/payroll/10`.

- **"ตารางผู้ประกันตน (สปส.1-10 ส่วนที่ 2)" table — names now render as proper Thai**:
  `??? เอหนึ่ง ปกติ`, `??? บีหนึ่ง เข้ากลางเดือน`, `??? ซีหนึ่ง ออกกลางเดือน` (prefix
  still `???` as expected/untouched; first/last names fully repaired). This table is
  built by `ISsoFilingService`/the O11-alt on-screen schedule, which reads the live
  `master.Employees` join — so it picks up the repair immediately.
- **Finding (not a failed repair):** the top **"รายการพนักงาน (3)"** summary table on
  the same page (the payslip list) **still shows the old corrupted names**
  (`??????? ?????`, `??????? ?????`, `?????? ?????????`) even after the repair and a
  fresh page load. This is a payslip-snapshot field (`Payslip.EmployeeName`, frozen at
  posting time), separate from the live employee-master name used by the SSO schedule
  and the ภ.ง.ด.1 filler — it will never self-correct for already-posted runs no matter
  what the master record says now. This is exactly the "figures snapshotted at posting
  time" caveat the task anticipated, confirmed by reading `Pnd1FilingService.cs`
  (`NameMapAsync` queries `db.Employees` live and `ResolveName` prefers it over the
  frozen `snapshotFullName`) and by the two tables disagreeing on the same page for the
  same employees.

Screenshot: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785294417716-17.jpg`
— run 07-2026-PR-0001, top payslip table still `???????` / SSO schedule table below it
showing the corrected names side by side on the same page.

### Step 8 — Open the run's ภ.ง.ด.1 output, confirm names render there too: PASS

Per the task's hard rule against native dialogs, did not open the PDF in a normal tab/
viewer flow; instead used an authenticated same-origin `fetch('/api/proxy/payroll/runs/10/pnd1/pdf')`
(200, 307,013 bytes) and rendered the returned blob in an `<iframe>` injected into the
already-loaded page (still just page content, not a native OS dialog) to inspect it
visually and via `read_network_requests`.

The ภ.ง.ด.1 PDF is 3 pages: page 1 = the main RD summary form (aggregate only, no
per-employee names), pages 2–3 = the official RD instructions (คำชี้แจง) **plus**, at
the bottom of page 3, the **ใบแนบ ภ.ง.ด.1** (attachment) table listing each employee.
That table now shows:

| # | เลขประจำตัว | ชื่อ | ชื่อสกุล | เงินได้ | ภาษี |
|---|---|---|---|---|---|
| 1 | 1-1037-00000-01-1 | ??? เอหนึ่ง | ปกติ | 60,000.00 | 372.92 |
| 2 | 1-1037-00000-02-9 | ??? บีหนึ่ง | เข้ากลางเดือน | 32,903.23 | 0.00 |
| 3 | 1-1037-00000-03-7 | ??? ซีหนึ่ง | ออกกลางเดือน | 19,354.84 | 0.00 |

Names render correctly (prefix still `???`, as expected/untouched). This confirms the
repair reaches the actual RD filing artifact, not just the on-screen SSO schedule —
consistent with `Pnd1FilingService.NameMapAsync` reading the live employee master
rather than a frozen snapshot.

Screenshot: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785294406202-16.jpg`
— ใบแนบ ภ.ง.ด.1 attachment table with the three repaired names.

### Part 2 summary

| Step | Result |
|---|---|
| Repair O8FULL/O8MID/O8OUT ชื่อ+สกุล via UI, nothing else touched | PASS |
| 7. SSO schedule table on a posted run renders repaired names | PASS |
| 7b. (finding) Payslip summary table on the same page still shows the frozen pre-repair names — payslip snapshot, not a failed repair | Reported, not a defect |
| 8. ภ.ง.ด.1 ใบแนบ attachment table renders repaired names | PASS |

Tenant isolation: only co7 data was observed throughout Part 2 (its 3 O8 employees and
its 2 payroll runs). No leak.

---

## Other findings

- **[LOW]** The employee-name-search API's fuzzy matching on `/settings/employees` and
  the tax-invoice picker on `/invoices/new` both occasionally return unfiltered/stale
  results for a query typed immediately after a previous selection (e.g. typing "0018"
  after "0020" was already chosen showed all three invoices, not just the one
  matching "0018", until the field was fully cleared first). Cosmetic/UX only — did not
  block any of the steps above once the field was cleared before retyping.
- **[INFO]** A browser extension in this environment ("Redirect Blocker") intercepts
  `window.open()` calls that aren't tied to a very recent user gesture and logs
  "Stopping to prevent same tab redirects" — this affected only the automation
  environment's ability to inspect the PDF via a real new tab, not the app itself
  (confirmed the app's own `openPdf()` helper is a plain `fetch` + blob + `window.open`,
  no `window.print()`/native dialog involved).

## Summary

| Part | Result |
|---|---|
| Part 1 — O2b on co5 | **PASS end-to-end** — the v1.24.1 blocker is fixed; both regression guards and the override case still hold |
| Part 2 — co7 employee name repair | **PASS** — repaired via UI, confirmed on the live SSO schedule and the ภ.ง.ด.1 ใบแนบ; the payslip-snapshot summary table on already-posted runs legitimately still shows the old frozen name (not a repair failure) |
