# Wave B-bn — Billing notes (ใบวางบิล) + WHT certificate (50 ทวิ), co5, prod v1.22.10

Agent: sonnet (browser/Playwright, raw `chromium.launch()` scripts, temp
`frontend/army-B-bn*.mjs` + `debug-picker-tmp.mjs`, all deleted after each run).
Accounts used: ar01 (A5), ap01 (A4), appr01 (A3), acct01 (A2), admin01 (A8) — all
already granted on co5. Company: co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด) only.

## Done
- [x] A1's "unconfirmed" `/invoices` row count re-verified with a real settle wait
      (waitForResponse on the list fetch + loading-text-detached, not a fixed sleep):
      **10 pre-existing rows**, not empty. A1's loading-state screenshot race is
      confirmed as a false read, not a product bug.
- [x] Billing-note full lifecycle: create → Issue → Settled, with PDF.
- [x] Billing-note TI-aggregation path exercised (multi-select "ใบกำกับภาษีที่รวม"
      picker) — mechanism understood and documented; totals/back-link outcome is
      **unconfirmed**, see Findings.
- [x] WHT certificate: confirmed auto-issue + immutability + hand-calc match + PDF,
      using both a pre-existing cert and (partially) a fresh PV attempt.
- [x] Fresh WHT-bearing PV attempt surfaced a real, reproducible **blocking bug**
      (422 `pv.wht_type_missing`) — see Findings, HIGH.
- [x] SoD (create/approve/post as separate permissions) confirmed live on prod.
- [x] Tenant-leak checks clean throughout (ar01/ap01/appr01/acct01/admin01 sessions).
- [x] Blast cap respected: **6 documents total** — TI #26, TI #27 (tax invoices),
      BN #20, BN #21 (billing notes), PV #18 (abandoned draft, wrong WHT% input),
      PV #19 (Approved, stuck — can't post, see Findings). No FORBIDDEN actions
      (no ยืนยัน/ปิดงวด, no year-end, no payroll, no edits to existing master data).

## Evidence

### Billing note (BN #20 — full lifecycle, manual line items)
- Customer picker note: the `ค้นหาชื่อ...` search box only fetches on a **non-empty**
  query (an empty-string fill leaves the dialog stuck on a loading `...` forever —
  looks like a picker-debounce hang but is actually "no query typed yet"). Fixed by
  searching `ทดสอบ` (present in every demo party name on co5).
- Created 2 posted Tax Invoices for customer "บริษัท ลูกค้าทดสอบ จำกัด / 0105567000315":
  TI #26 = `07-2026-TI-0016` (qty 3 × ฿1,000 → ฿3,210.00 incl. 7% VAT), TI #27 =
  `07-2026-TI-0017` (qty 2 × ฿1,500 → ฿3,210.00 incl. 7% VAT).
  Shots: `B-bn-02-ti1-posted.png`, `B-bn-03-ti2-posted.png`.
- BN #20 (`07-2026-IV-0003`) created via the one-step **"ออกใบแจ้งหนี้" (Issue)**
  button with ONE manual line (qty 5 × ฿2,000 = ฿10,000 pre-VAT). This lands
  directly on status **Issued** (no separate visible Draft step in this path — the
  "บันทึกร่าง" button exists for an explicit Draft, untested here since Issue was
  the target flow). Shot: `B-bn-06-bn-issued-detail.png`.
- **Mark-Settled**: clicking "ยืนยันชำระครบแล้ว" opens a `ConfirmActionDialog`
  ("ยืนยันว่าชำระครบแล้ว") showing the computed total **฿10,700.00**
  (= 10,000 × 1.07 — **hand-calc match**), then a "ยืนยัน" click transitions the
  status. Confirmed **Draft(implicit)→Issued→Settled** all work; status read as
  `ชำระครบแล้ว · Settled` afterward. Shots: `B-bn-06-bn20-final-status.png`.
- BN doc numbering: `07-2026-IV-XXXX` (branch-month-prefix + `IV` for both Tax
  Invoices *and* Billing Notes route under the same "IV" doc-no family — TI uses
  `TI`, BN uses `IV`; not a collision, just worth noting for anyone assuming BN
  gets its own letter code).
- **PDF**: the header "พิมพ์ / PDF" split-button's "ดาวน์โหลด PDF (สำเนา)" menu
  item did **not** reliably fire a `download` event under Playwright (click, force
  click, and raw mouse-coordinate click all timed out with no download, no popup,
  no network request — likely a Radix-menu interaction quirk under automation, not
  necessarily broken for a real user). Bypassed via the same direct-proxy pattern
  the WHT-cert e2e test already uses: **`GET /api/proxy/billing-notes/{id}/pdf`**
  → 200, `application/pdf` (note: NOT `/api/proxy/invoices/{id}/pdf`, which 404s —
  the frontend route is `/invoices/*` but the backend entity/API is
  `billing-notes`). Saved: `swarm-findings/army/pdfs/B-bn-billing-note.pdf` (1 page,
  verified valid PDF).

### Billing note TI-aggregation (BN #21 / `07-2026-IV-0004`) — inconclusive
- The multi-select "ใบกำกับภาษีที่รวม" field is a real, working typeahead: clicking
  it populates up to ~20 recent Posted TIs for the picked customer (doc no,
  customer, date, amount) after a **~2s debounce** (same picker-debounce class as
  the customer/vendor pickers — a <1s read will catch it mid-fetch and misreport
  "0 available", which is what happened on the first pass of this run).
- Selected TI #26 (`07-2026-TI-0016`) successfully; the re-open-to-pick-#27 step
  failed ("not found in listbox") — plausibly because the field's accessible label
  changes after the first pick (script reliability issue, not confirmed as a UI
  bug) rather than a real exclusion.
- **Issuing BN #21 with 0 manual lines was blocked** by client validation
  ("กรุณากรอกข้อมูลให้ครบถ้วน" toast) even with a TI already linked — so **linking a
  TI does NOT auto-populate the line-items table or roll up the total**. A manual
  fallback line (qty 1 × ฿1) was added to get the BN to Issue at all.
  Resulting total did **not** equal the TI sum (฿6,420 expected from TI #26+#27);
  it reflected only the manual line. The `bn-ti-chips` back-link element also
  showed **0** items on the detail page's "เอกสารอ้างอิง" panel.
- **Net read**: on this evidence, the "ใบกำกับภาษีที่รวม" aggregation field appears
  to be either (a) not actually persisting the selection, or (b) a pure
  reference/tag with no computed effect on totals or back-links — **NOT
  confirmed either way** because the script's own reliability (partial 1/2 pick)
  makes this a weak signal. Recommend a short manual UI pass (pick both TIs
  cleanly, confirm via the picker's own visible "selected" state before Issue)
  before filing this as a product bug. Shots: `B-bn-07-bn2-ti-linked-no-manual-line.png`,
  `B-bn-08-bn2-issue-blocked-no-line.png`, `B-bn-09-bn2-issued-detail.png`.

### WHT certificate (50 ทวิ, direction P — auto-issue on PV post)
- `/wht-certificates` page subtitle confirms the design (from A1, re-confirmed
  live): auto-issued on posting a WHT-bearing PV, no manual create/edit UI.
- **Working reference (pre-existing cert, id=2)**: `07-2026-WT-0001`, PV #15,
  payee "บริษัท ผู้ขายทดสอบ จำกัด", incomeAmount ฿1,000, **whtAmount ฿30.00**,
  formType `Pnd53`, status `Posted`. Hand-calc: ฿1,000 × 3% = **฿30.00 — exact
  match**. Detail page has **0 edit-button affordance** — immutability confirmed.
  PDF: `GET /api/proxy/wht-certificates/{id}/pdf` → 200. Saved:
  `swarm-findings/army/pdfs/B-bn-wht-cert-50twi.pdf` (2 pages, verified valid PDF).
  Shots: `B-bn-21-wht-cert-detail.png`, `B-bn-20-wht-certs-after-pv2.png`.
- **SoD confirmed live**: ap01 (PV creator) has no working Approve button; appr01
  approves via a `ConfirmActionDialog` ("ยืนยันการอนุมัติใบสำคัญจ่าย", shows the
  computed VAT/WHT/net-pay preview); appr01 in turn has **no Post button** —
  only ap01 (the original creator role) can Post. Three-way SoD (create ≠
  approve ≠ post) is real and enforced, not just a client-side hide.
- **New PV attempt (PV #19) — see HIGH finding below**: correctly entering WHT as
  a plain percent ("3" for 3%, not the fraction "0.03" — see finding) produced a
  Post-confirm modal with an exactly-correct preview (VAT ฿70.00, WHT ฿30.00, net
  ฿1,040.00 — hand-calc match), but the actual POST call **422'd**, leaving the
  document permanently stuck in Approved state (see Findings).

## Findings

**HIGH — WHT-bearing PV Post hard-fails (422) when the per-line Income-Type
(50 ทวิ) dropdown is left at its default, and the document becomes permanently
unpostable/unfixable afterward.**
- Repro: `/payment-vouchers/new` → pick a domestic vendor → fill a line
  (รายละเอียด, มูลค่าก่อนภาษี = 1000, หัก ณ ที่จ่าย % = 3) → **do not touch** the
  adjacent "ประเภทเงินได้ (50ทวิ)" dropdown (defaults to "— ไม่หัก —") → Save
  (Draft OK) → Approve (as appr01 — OK, modal shows WHT ฿30.00 as if valid) →
  Post (as ap01) → **`POST /api/proxy/payment-vouchers/{id}/post` → 422**
  `{"type":"urn:teas:error:pv.wht_type_missing","title":"pv.wht_type_missing","status":422,"detail":"WHT line references missing WhtType ."}`.
- The confirm-post modal (opened right before this call) shows a fully computed,
  *materially misleading* preview — VAT ฿70.00, หัก ณ ที่จ่าย ฿30.00, จ่ายสุทธิ
  ฿1,040.00 — for a submission that is guaranteed to fail. Nothing in the UI
  warns the user beforehand that the Income-Type dropdown is required whenever a
  WHT % is set.
- Worse: once Approved, **the PV has no edit affordance at all** (confirmed: 0
  `<select>` elements, only Post/create-TI/print/upload buttons present) — so a
  document that hits this state cannot be corrected or (from what's visible)
  cancelled back to Draft. PV #19 on co5 is now stuck in exactly this state as
  live evidence.
- Recommend: (a) client-side validation blocking Save/Approve when a WHT % > 0
  line has no Income-Type selected, or at minimum blocking the Post-confirm
  modal from opening with an accurate-looking preview; (b) an edit-or-cancel path
  for Approved-but-unpostable documents.
- Screenshots: `B-bn-17-pv2-new-filled.png` (shows the unfilled "ประเภทเงินได้
  (50ทวิ)" dropdown at "— ไม่หัก —" right next to the correctly-filled 3% field),
  `B-bn-98-pv2-error-state.png` (Approved, no edit path, only Post available).

**LOW — `frontend/e2e/payment-voucher-with-wht.spec.ts` likely has a stale WHT%
input value.** The test fills the "หัก ณ ที่จ่าย %" spinbutton with `'0.03'` and
comments "WHT 3%". Live behavior (via a real PV's approve-confirm modal, cross-
checked twice) shows the field takes a **plain percent number** — `3` → ฿30.00
WHT on ฿1,000; `0.03` → ฿0.30 WHT on ฿1,000 (confirmed both ways this session,
PV #18 vs PV #19). The test's own assertions never check the WHT amount, so this
has been silently "passing" regardless. Not a product bug — flagging for whoever
next touches that spec, since as written it exercises a WHT rate two orders of
magnitude off from what it claims to test. (Not fixed — out of scope, no source
edits per HARD RULES.)

**INCONCLUSIVE — billing-note TI-aggregation ("ใบกำกับภาษีที่รวม") may not roll
up totals or produce back-links.** See Evidence above — script reliability
(partial TI pick) means this needs a clean manual re-check before it's filed as
a confirmed bug, but as observed, total ≠ TI sum and 0 back-link chips appeared
even with 1 TI linked.

**Not a finding (documented so nobody re-flags it)**: the BN detail page's
"พิมพ์ / PDF" dropdown download button doesn't fire a Playwright `download` event
under any click strategy tried (normal, force, raw-coordinate mouse click) — no
network request for a PDF was ever observed from that specific button in this
session. Worked around via the direct API endpoint. This *might* be a real
automation-only quirk (Radix dropdown interaction) or might affect real users
too — recommend a quick manual click-test outside Playwright before deciding if
it's product-facing.

## Unbuilt-vs-untested classification
Both billing notes and WHT certificates are **fully built, live features** on
prod v1.22.10 — nothing here is "unbuilt." The TI-aggregation sub-feature is
**built but its effect is unverified** (inconclusive, see above), and WHT-type
selection is **built and enforced server-side but has a UI gap** (no client-side
guard before the misleading confirm-post preview).

## Blockers / follow-ups for Wave C / consolidation
- Bring PV #19 (co5, Approved, stuck, un-postable) to Fable's attention — it's a
  live artifact of the HIGH finding above, left in place deliberately as repro
  evidence (not cleaned up, since no delete/cancel path was found for it).
- Manual UI re-check recommended for the TI-aggregation inconclusive finding
  before it's triaged as a bug.
- `B-bn-wht-cert-50twi.pdf` is from the **pre-existing** cert (id=2, PV #15), not
  from this session's PV #19 (which never posted) — still the correct, real,
  auto-issued 50-tawi artifact for Wave C's vision-compare.
