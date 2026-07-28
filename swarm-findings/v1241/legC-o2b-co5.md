# Leg C — O2b: Billing note lines generated from linked tax invoices (co5)

**Company:** co5 — บริษัท ทดสอบ VAT (DUMMY) จำกัด
**Login:** admin01 (Company Admin)
**Environment:** https://teas.kazaki-rio.com, prod, v1.24.1
**Tester:** Claude (browser automation, live production)

## Document-type mapping (needed before testing)

The task names the feature "billing note (ใบวางบิล)". The TEAS sidebar has no
item literally labelled ใบวางบิล. The document that supports linking tax
invoices is **"ใบแจ้งหนี้" (Invoice)** at `/invoices` — its create/edit form has
a field **"ใบกำกับภาษีที่รวม" (tax invoices included)** that lets you attach one
or more posted tax invoices. Confirmed via network trace that the underlying
API is `POST/GET /api/proxy/billing-notes` — i.e. **"ใบแจ้งหนี้" IS the billing
note (ใบวางบิล) internally**, just labelled "Invoice/INVOICE" in the UI and on
the printed paper. All testing below targeted this document type.

## Step 1 — Two posted tax invoices (PASS)

| Invoice | Customer | Subtotal | VAT | Total | Status |
|---|---|---|---|---|---|
| 07-2026-TI-0017 (#27) | บริษัท ลูกค้าทดสอบ จำกัด | ฿3,000.00 | ฿210.00 | ฿3,210.00 | Posted |
| 07-2026-TI-0018 (#28) | บริษัท ลูกค้าทดสอบ จำกัด | ฿100.00 | ฿7.00 | ฿107.00 | Posted |
| **Expected sum** | | **฿3,100.00** | **฿217.00** | **฿3,317.00** | |

## Step 2/3 — Empty line grid → auto-generate lines: **BLOCKED (CRITICAL finding)**

Created a new "ใบแจ้งหนี้", selected customer บริษัท ลูกค้าทดสอบ จำกัด, linked both
TI-0017 and TI-0018 via "ใบกำกับภาษีที่รวม", and left the line grid at its default
state (1 row, blank description, qty 1, price 0 — i.e. never typed into it).

**Result:** clicking either "บันทึกร่าง" (save draft) or "ออกใบแจ้งหนี้" (issue)
fails client-side with toast **"กรุณากรอกข้อมูลให้ครบถ้วน"** (please complete all
required information) and an inline message under the grid: **"ต้องมีรายการอย่างน้อย
1 รายการ"** (must have at least 1 item). No network request is ever fired
(confirmed via `read_network_requests` — zero requests to `/invoices` or
`/billing-notes` after clicking either button). The grid's own "remove" trash
icon on the last row is disabled, so there is no way to reach 0 rows either.

There is **no UI indication anywhere on the form** that lines will be
generated from the linked tax invoices on save (no hint text, no toggle, no
visual change when invoices are linked) — contrary to what the feature is
supposed to communicate to the user.

**Root cause (confirmed, not guessed):** the client-side "at least one valid
line item" validation does not special-case the "tax invoices linked, no
manual lines" scenario described by O2b. It requires a manual product/
description to be typed into the grid regardless of whether tax invoices are
linked, which makes the auto-generation entry point **unreachable through the
standard UI**. This blocks the core scenario the O2b fix is meant to enable.

Screenshot evidence:
- `Z:\temp\claude-chrome-screenshots-NEmSN9\screenshot-1785269341892-13.jpg` —
  TI-0017 linked, empty grid, red validation blocking save.

Tried/ruled out before concluding this is a genuine blocker (>2 attempts):
1. Default empty row + "บันทึกร่าง" → blocked.
2. Default empty row + "ออกใบแจ้งหนี้" → blocked (same error).
3. Attempted to delete the last row to reach 0 items → delete button
   disabled/no-op.
4. Searched the whole form (incl. หมายเหตุ section) for a hidden toggle or
   hint about auto-generation → none found.
5. Checked network requests during both save attempts → zero fired
   (confirms client-side block, not a server rejection).
6. Confirmed the validation is specifically about the row being "empty"
   (not about the tax-invoice link or dates) by typing a real description
   into the row — the error disappeared immediately (see Step 4 below).

**Impact:** the headline O2b behavior ("link invoices, leave grid empty →
system generates one line per invoice with correct copied amounts") could
not be exercised or confirmed at all via the UI on prod. Whether the backend
logic for auto-generation is correct is **unconfirmed** — the client blocks
the request before the backend to be tested is ever reached.

## Step 4 — Override case (manual lines win, nothing generated): **PASS**

Using the same document (both TI-0017 and TI-0018 linked), typed a manual
line item ("ค่าบริการทดสอบ manual line", qty 1, price ฿500.00, VAT 7%) into the
grid, then saved as draft. Result: doc **#25** saved successfully with:

- Line: "ค่าบริการทดสอบ manual line" × 1 @ ฿500.00 = ฿500.00
- Subtotal ฿500.00, VAT ฿35.00, **Total ฿535.00**
- Tax invoices TI-0017 and TI-0018 remain shown as linked ("ใบกำกับภาษีที่รวม"
  chips) on the document, but their amounts (which would sum to ฿3,317.00)
  were **not** used — the manual line's amount is exactly what was typed,
  untouched, and no additional generated lines were added alongside it.

This confirms: when a manual line is supplied, it survives untouched and no
generation occurs, even with tax invoices linked — this half of the O2b spec
works correctly.

## Step 5 — Edit a line, still Draft: **PASS** (generic — not a generated line)

Since Step 2/3 was blocked, there was no *generated* line to edit. Instead
verified the general "Draft lines are editable, edits persist" behavior using
doc #25's manual line: changed price ฿500.00 → ฿750.00 via "แก้ไข", saved.
Reloaded the document: price shows ฿750.00, VAT ฿52.50, **total ฿802.50** —
edit persisted correctly.

## Step 6 — Issue and check rendering: **PASS** (on the override-case document)

Issued doc #25 (confirmation dialog warned the document number would be
locked and could not revert to Draft — accepted). It was assigned number
**07-2026-IV-0006**, status changed to "ออกแล้ว · Issued". Fetched the paper/
print data via authenticated same-origin `fetch('/api/proxy/billing-notes/25/paper')`
(avoided opening the native print dialog per instructions) — response 200,
confirmed it contains the correct Thai line description, the correct
document number, the correct customer name, and the correct total (802.50).
Since the true generated-lines scenario was unreachable (Step 2/3), this
does not confirm generated-line rendering specifically, only that the
paper/PDF pipeline renders an issued document's real data correctly in
Thai.

## Step 7 — Issued document lines are immutable: **PASS**

On issued doc 07-2026-IV-0006 (#25): the "แก้ไข" (Edit) button is gone from
the detail view (replaced by "ยืนยันชำระครบแล้ว" / "ยกเลิก"). Navigating directly
to `/invoices/25/edit` redirects back to the read-only `/invoices/25` view —
edit is blocked both in the UI and at the route level.

## Other findings

- **[LOW] Missing i18n key**: console repeatedly logs
  `MISSING_MESSAGE: common.remove (th)` while on the invoice create/edit
  form (fires once per row render). Did not surface as visible raw-key text
  on screen (the "remove" trash-icon button has no visible label), so
  severity is low, but it is a real missing translation key worth fixing.
- **[INFO] Transient prod outage during testing**: mid-session, navigating
  to `https://teas.kazaki-rio.com/invoices/25` returned a Cloudflare 521
  "Web server is down" for roughly 20–25 seconds, then recovered on its own
  on retry. Not related to any action taken in this test session (no writes
  were in flight at the time) — flagging in case it correlates with a
  deploy/restart on the ops side around **2026-07-28 ~20:01–20:02 UTC**.

## Tenant isolation check

No data from any company other than co5 was observed at any point (all
customers/invoices shown belonged to co5's own customer list: บริษัท ลูกค้าทดสอบ
จำกัด, นายสมชาย ใจดี, and various "ลูกค้าทดสอบ swarm…" test customers). No leak.

## Summary table

| Step | Result |
|---|---|
| 1. Two posted tax invoices identified | PASS |
| 2/3. Empty grid → auto-generate lines, correct sums, no VAT-on-VAT | **BLOCKED** — unreachable via UI, client-side validation requires a manual line even when invoices are linked |
| 4. Override: manual line survives, nothing generated | PASS |
| 5. Edit a line while Draft, edit persists | PASS (generic, not a generated line) |
| 6. Issue + PDF/paper renders correctly in Thai | PASS (on override-case doc, not generated-line doc) |
| 7. Issued document lines immutable | PASS |

**Overall: the core O2b scenario (auto-generation from linked invoices with an
empty grid) is UNVERIFIED and appears BLOCKED in production** by a client-side
"must have at least 1 item" validation that was not updated to allow an empty
grid when tax invoices are linked. The complementary override behavior (manual
lines win, no double-billing) does work correctly.
