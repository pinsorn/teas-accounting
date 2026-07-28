# Leg A — O14 (reopen a closed monthly accounting period) — co6 live prod verification

- **Env:** https://teas.kazaki-rio.com (prod), v1.24.1
- **Company:** co6 — บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด
- **User:** nvadmin01 (COMPANY_ADMIN on co6)
- **Date of run:** 2026-07-29 (Bangkok "today" the app pinned every DocDate to)
- **Tester:** browser automation (claude-in-chrome), live prod, no source touched

## Summary verdict

**co6 is now usable again.** The freeze described in `troubles-wiki.md` ("period.closed
422 on every new draft, and no button/route can undo it — company looks permanently
bricked") is resolved by O14. A Payment Voucher was created on co6 for the first time
since the FY2026 year-end/period closures, and it posted end-to-end to a balanced
journal entry. The single most important safety check (step 2 — refusing a month
reopen while the fiscal year is still closed) **PASSED** — this is the invariant that
protects the balance-sheet/closing-entry tie-out, and it held.

## Step-by-step results

### Step 1 — Confirm the freeze is real — PASS
Attempted to create a Payment Voucher on co6 (vendor "ผู้ขาย NON-VAT ทดสอบ B2NV",
category MISC, ฿1,000.00, doc date server-pinned to 2026-07-29). Clicking "บันทึก"
(Save) returned a blocking error toast:

> **"Period 2026-07 is CLOSED. Reopen the period or correct doc_date."**

No draft was created. Screenshot:
`Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785264687865-0.png`

### Step 2 — Reopen a MONTH while the fiscal year is still closed — PASS (refused, as required)
On `/period-close` for FY2026 (year-end status "ปิดแล้ว"/Closed, closed 2026-07-25,
all 12 months "ปิดแล้ว"), clicked "เปิดงวดใหม่" on January 2026, confirmed the
in-app dialog. The action was **refused** with a red error toast:

> **"ปีบัญชีนี้ปิดแล้ว กรุณาเปิดปีบัญชีก่อน แล้วจึงเปิดงวดนี้ใหม่"**
> ("This fiscal year is closed. Please reopen the fiscal year first, then reopen this
> period again.")

Confirmed at the network layer on a second attempt (February 2026):
`POST /api/proxy/periods/2026/2/reopen` → **HTTP 422**. This is the `period.year_closed`
refusal the spec calls for. No month's status changed as a result of either attempt
(verified both remained "ปิดแล้ว" after the refusal).

Screenshots: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785264784332-2.jpg`
(dialog open, pre-confirm) and `...screenshot-1785264815256-4.jpg` (post-refusal,
dialog reset to initial state — the transient toast itself had already faded by the
time each follow-up screenshot was taken, but the exact wording above was read live
off two separate attempts and the 422 status was confirmed via
`read_network_requests`).

**This is a PASS on the single most important assertion in this leg.** The
ledger-safety invariant (spec D3) held: a still-closed fiscal year blocks monthly
reopen.

### Step 3 — Reopen the fiscal year — PASS
Clicked "เปิดงวดบัญชีสิ้นปีอีกครั้ง" (existing pre-O14 action), confirmed. Success
toast: **"เปิดงวดบัญชีสิ้นปี 2026 อีกครั้งแล้ว"**. The year-end panel switched from
a closed badge to the close-year input form, confirming FY2026 is now open. All 12
months remained "ปิดแล้ว" (untouched, correctly — year reopen does not cascade to
months). Screenshot: `...screenshot-1785264907243-5.jpg`

### Step 4 — Reopen the month (now that the year is open) — PASS
Clicked "เปิดงวดใหม่" on July 2026, confirmed. Success toast: **"เปิดงวดกรกฎาคมใหม่แล้ว"**.
July 2026's row updated in place: status badge → **"เปิด"** (Open, green), "ปิดเมื่อ"
(closed-on date) cleared to "—", and the row's action button flipped to "ปิดงวด"
(Close period, red). As a side-effect I noticed the year-end close button is now
correctly disabled with tooltip "ต้องปิดทุกเดือนในปีบัญชีนี้ก่อน" (must close every
month in this fiscal year first) — a sane consistency guard.
Screenshot: `...screenshot-1785264941167-6.jpg`

### Step 5 — Create a Payment Voucher on co6 again — PASS (draft → post, end-to-end)
Built a fresh PV: vendor "ผู้ขาย NON-VAT ทดสอบ B2NV", category MISC, description
"ทดสอบ O14 reopen period - PV หลังเปิดงวด", ฿1,000.00.
- **Save (draft):** succeeded — green "บันทึก" toast, no period error this time.
- Created as **PV #23**, doc date 29/07/2569 (2026-07-29).
- **Approve (อนุมัติ):** succeeded — confirmation dialog showed VAT ฿0.00 / WHT ฿0.00 /
  net ฿1,000.00, confirmed → "อนุมัติแล้ว" (Approved).
- **Post (บันทึกเอกสาร / Post):** confirmation dialog explicitly warned the post is
  permanent/immutable ("จะบันทึกยืนกันที และไม่สามารถแก้ไขหรือลบได้"), confirmed →
  final doc number **`07-2026-PV-MISC-0001`**, status **"บันทึกแล้ว"** (Posted).
- **Journal entry balance (Dr = Cr) check:** rather than a dedicated JE-detail page
  (none found in the app's nav — see Step 6 note), verified via the Trial Balance
  report (`/reports/trial-balance`) at date 2026-07-29:
  - Report header badge: **"Dr = Cr ✓"**
  - Grand total row: **Debit ฿17,640.00 = Credit ฿17,640.00** (balance ฿0.00)
  - Bank account `1120 เงินฝากธนาคาร` credit column moved from ฿6,406.54 (as of
    2026-07-28, pre-PV) to **฿7,406.54** (as of 2026-07-29) — an exact +฿1,000.00
    delta matching the new PV's cash-out, confirming the posting actually hit the
    ledger and balanced.
  - Dashboard totals also moved consistently: รายจ่าย (expenses) ฿205,640.00 →
    ฿206,640.00 (+1,000), กำไรสุทธิ (net profit) -202,640.00 → -203,640.00 (-1,000).

  Screenshot: `...screenshot-1785265304611-7.jpg` (trial balance, Dr=Cr confirmed)

### Step 6 — Re-check period-close screen + audit trail — PASS (status) / gap noted (audit view)
- Reloading `/period-close` (fresh navigation, not just in-memory state) confirms
  July 2026 persists as **"เปิด"** (Open) — the reopen was durably saved, not a UI-only
  flicker.
- **Audit trail:** the PV document itself has a per-document "ประวัติกิจกรรม"
  (Activity History) panel (shows "อนุมัติ" → "สร้างเอกสาร → ฉบับร่าง", each stamped
  with actor `nvadmin01` and Thai-calendar timestamp `29 ก.ค. 2569`) — this proves the
  PV's own lifecycle is audited. **However, I could not find any reachable
  audit/activity view for the *period-close reopen action itself*** — I checked the
  main nav (all links enumerated via accessibility tree across several scroll
  positions), `/settings/company` and its sub-page, a bare `/settings` (404), and the
  notification bell icon (no dropdown content) — none exposes a company-wide
  activity/audit log, and the period-close page shows only current status + the
  "ปิดเมื่อ" (closed-on) date, not a reopen history. Per the instructions: **I am
  reporting this gap rather than inventing an audit view that isn't there.** This is
  a minor finding, not a blocker — see Findings below.

### Step 7 — Negative case: reopen a month that was never closed — PASS
The UI itself does not offer a "เปิดงวดใหม่" button on an already-open month (only
closed months show that action) — so an already-open period can't be reopened via a
stray click, which is correct. To exercise the actual guard, I checked years 2025 and
2027 on `/period-close` (both entirely open, never closed, since this is a fresh test
company) and called the same endpoint directly via an authenticated same-origin
`fetch()` from the browser console context (session cookie attached, same as any UI
action) against `POST /api/proxy/periods/2027/1/reopen`:

```json
{
  "status": 422,
  "body": {
    "type": "urn:teas:error:period.not_closed",
    "title": "period.not_closed",
    "status": 422,
    "detail": "Period 2027-01 is not closed."
  }
}
```

A clean structured 422 error — **not a 500, not a blank page, not a raw i18n key**.
Page remained fully functional afterward (screenshot confirms January 2027 still
reads "เปิด", unaffected).

## Tenant isolation check
Throughout the entire leg, every screen showed only co6 data: "บริษัท ทดสอบ NON-VAT
(DUMMY) จำกัด", vendor "ผู้ขาย NON-VAT ทดสอบ B2NV" / "ผู้ขาย NON-VAT ทดสอบ (จดVAT)".
**No trace of นาย พงศ์สันต์, เรปทาวน์, co2, co5, or co7 data appeared at any point.**
No tenant leak.

## Findings

1. **(Low / cosmetic-process gap) No reachable audit/activity view for period
   reopen/close actions.** The per-document activity history exists for
   documents (PV, etc.) but there is no equivalent for period-close state
   transitions — you can see *current* status and the *last* closed-on date, but
   not a log of "who reopened month X, when, why." Not a blocker for O14's stated
   purpose (unblocking co6), but worth a backlog note if period-close actions need
   to be defensible in an audit (Sor.Por.Kor or internal control review).
2. **(Informational, not a bug)** The newly posted PV (`07-2026-PV-MISC-0001`) shows
   two informational "ไม่สมบูรณ์" (incomplete) flags — "ขาดบันทึกใบกำกับภาษีซื้อ"
   (missing purchase tax invoice) and "ขาดไฟล์ใบเสร็จจากผู้ขาย" (missing vendor
   receipt file). This is expected/correct given the test data (no attached receipt,
   simplified NON-VAT vendor) — flagging only so it isn't mistaken for a step-5
   failure by a reader skimming screenshots.
3. No 500s, blank pages, or raw i18n keys were observed at any point in the leg
   (console messages checked, all error states were properly localized Thai toasts
   or structured JSON error bodies).

## Evidence index (screenshots)
- Step 1 refusal: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785264687865-0.png`
- Step 2 dialog (pre-confirm): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785264784332-2.jpg`
- Step 2 dialog (post-refusal, reset): `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785264815256-4.jpg`
- Step 3 year reopened: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785264907243-5.jpg`
- Step 4 month reopened: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785264941167-6.jpg`
- Step 5 trial balance Dr=Cr: `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785265304611-7.jpg`
