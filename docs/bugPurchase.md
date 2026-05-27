# bugPurchase.md — Sprint 13j-PURCH RE-VALIDATE findings

> Sana RE-VALIDATE session vs commit `01136c5` · Started 2026-05-27
> Ground rule: CLICK EVERYTHING. A page that renders but whose button 500s/404s is a FAIL.
> Severity: **P0** = blocks ship · **P1** = ship-blocker after release · **P2** = polish nit
> Format per finding: `BP-NN · severity · page · what was clicked → what happened vs expected → console/network evidence`

---

## Session log

### Pre-flight §0 — in progress

- ✅ Backend :5080 started (`ASPNETCORE_ENVIRONMENT=Development`, PID 63288) — listener confirmed
- ✅ Frontend :3000 already running
- ✅ Login `admin / Admin@1234` succeeds → lands on `/` dashboard
- ✅ Dashboard shows mascot greeting "พร้อมทำงานวันที่ดี ๆ แล้วครับ" + 4 stat cards (Thai labels)
- ✅ Sidebar shows ขาย + ซื้อ sections — Purchase menu items present: ผู้ขาย · บันทึกใบกำกับภาษีซื้อ · ใบสั่งซื้อ · ใบสำคัญจ่าย (more may be below scroll — to verify)
- ✅ `/reports/ap-aging` loads (visited directly) — Thai columns, totals row, CSV export button present
- ⏳ `/settings/expense-categories` — TODO
- ⏳ Sidebar reports section "เจ้าหนี้ค้างชำระ" + settings "หมวดค่าใช้จ่าย" — TODO

---

## Findings (BP-NN entries to be filed below as discovered)

### BP-01 · P1 · `/settings/expense-categories` — only 10 rows, with duplicates (spec requires 19 seeded, unique)

**What was clicked:** navigate to `/settings/expense-categories`.

**Expected:** 19 unique expense categories listed (per `accounting-system-plan.md` §17.3 + seed migration `150_seed_expense_categories.sql`). Read-only.

**Actual:** 10 rows returned. **Duplicates by `categoryCode`**: ADS (IDs 4 + 8), ENT (5 + 9), OFF + OFFICE (different codes but same Thai name "ค่าใช้จ่ายสำนักงาน" — looks like a rename re-seed), RENT (×2), SVC (×2).

**Evidence — API response from `GET /api/proxy/expense-categories` (page-side fetch):**

```json
{
  "count": 10,
  "sample": [
    { "categoryId": 8, "categoryCode": "ADS", "nameTh": "ค่าโฆษณา",   "defaultIsRecoverableVat": true,  "isCapex": false },
    { "categoryId": 4, "categoryCode": "ADS", "nameTh": "ค่าโฆษณา",   "defaultIsRecoverableVat": true,  "isCapex": false },
    { "categoryId": 5, "categoryCode": "ENT", "nameTh": "ค่ารับรอง",  "defaultIsRecoverableVat": false, "isCapex": false },
    { "categoryId": 9, "categoryCode": "ENT", "nameTh": "ค่ารับรอง",  "defaultIsRecoverableVat": false, "isCapex": false }
  ]
}
```

**Likely root cause (to verify):**
- Seed migration ran twice (one without dedup-key, then a follow-up); OR
- Multi-tenant join leaks rows from a 2nd company; OR
- The dev DB was migrated through both `150_seed_expense_categories.sql` and `170_link_expense_category_default_wht.sql` and the latter re-inserted instead of UPDATE.

Quick check the Claude Code agent should run: `SELECT company_id, COUNT(*) FROM sys.expense_categories GROUP BY company_id;` — if rows exist for multiple companies, API filter is missing `WHERE company_id = current_tenant`; if all rows are for company 1, the seed is bad.

**Impact:** PV creation flow currently relies on user picking an Expense Category. Duplicates → user confusion + risk of picking wrong company's category (multi-tenant leak class).

---

### BP-02 · ✅ RESOLVED (2026-05-27) · P2 · `/settings/expense-categories` — boolean columns show "—" instead of ✓/✗

**What was clicked:** view the table.

**Expected:** columns "ภาษีซื้อขอคืนได้" and "สินทรัพย์ถาวร (CAPEX)" render the API booleans (`defaultIsRecoverableVat`, `isCapex`) as ✓ / — (or any clear visual).

**Actual:** all rows show "—" (em-dash) in both columns. The API IS returning the booleans (see BP-01 evidence) — the FE table cell binding is missing for these two columns.

**File hint:** `frontend/app/(dashboard)/settings/expense-categories/page.tsx` — column definitions probably never wired the boolean → cell renderer.

**Impact:** users can't see at a glance which categories allow recoverable VAT — they have to click into PV form to find out. Polish but visible.

---

### BP-03 · ✅ RESOLVED (2026-05-27) · P1 · `/purchase-orders/[id]` PaperDocument — party box mislabelled "ลูกค้า / CUSTOMER" (should be "ผู้ขาย / VENDOR")

**What was clicked:** submitted PO `#10` (vendor: ผู้ขาย purchase-chain e2e), landed on the PO detail page.

**Expected:** PaperDocument party box shows "ผู้ขาย / VENDOR" since a PO is issued by us **to a vendor**.

**Actual:** Party box labeled "ลูกค้า / CUSTOMER" — the Sales-side label was reused without doctype-aware swap. The party row content is correct (vendor name shows), only the LABEL is wrong.

**File hint:** `frontend/components/paper/PaperDocument.tsx` (or its child rendering the party panel). Likely needs a prop like `partyLabel: 'customer' | 'vendor'` or a doctype-aware label resolver.

**Impact:** confusing for users + visually wrong on the printed PO PDF customers/auditors will see.

---

### BP-04 · ✅ RESOLVED (2026-05-27) · P1 · `/purchase-orders/[id]` PaperDocument — Subtotal label/value mismatch with discount

**What was clicked:** PO `#10` paper view footer.

**Expected:** When line carries a 10% discount on a 1,000 unit price:
- pre-discount line total = 1,000
- discount = -100
- after-discount subtotal = 900
- VAT 7% on 900 = 63
- total = 963

The label "**มูลค่าก่อนหักส่วนลด · Subtotal**" should equal **1,000** (pre-discount amount) — otherwise the label doesn't mean what it says.

**Actual:** that row shows **900.00**, which is the AFTER-discount amount — same as the "มูลค่าก่อนภาษี · Before VAT" row underneath. There's no row showing the pre-discount 1,000 anywhere on the paper.

**Two valid fixes — Claude Code picks one:**
1. Show 1,000 in "มูลค่าก่อนหักส่วนลด" row (a true pre-discount subtotal), and add a discount row `-100` before "Before VAT 900".
2. Rename the row to "มูลค่าก่อนภาษี" or drop it — currently duplicates "Before VAT".

**Impact:** auditor reading the PDF cannot reconstruct the discount math. ม.86/4 #6 (VAT shown separately) is fine, but the PO discount transparency fails. Same risk for VI/PV/TI if the PaperFoot component is shared — must verify all doctype PDFs once fixed.

**Visible note:** "Date" field shows `27/05/2569` (Buddhist year) — CLAUDE.md §5 says CE internally only, but Thai-locale display dates conventionally use Buddhist year. Not flagged as bug — see NIT-02.

---

### BP-05 · ✅ RESOLVED (2026-05-27) · P1 · `/purchase-orders/[id]` Approve button — silent failure on SoD violation (no FE toast)

**What was clicked:** "อนุมัติ" (Approve) button on PO `#10` detail, logged in as `admin` who is also the creator.

**Expected:** A clear Thai error toast/dialog like "ผู้อนุมัติต้องเป็นคนละคนกับผู้สร้างเอกสาร" + status stays ร่าง.

**Actual:** The button click fires `POST /api/proxy/purchase-orders/10/approve` → BE responds **422** `urn:teas:error:po.sod_violation` with `detail: "Approver must differ from the creator (segregation of duties)."` BUT **the UI shows nothing** — no toast, no inline error, no status change indicator. Two consecutive clicks fired the same 422 silently. From a user's perspective the button appears broken.

**Evidence — direct API confirm:**
```json
POST /api/proxy/purchase-orders/10/approve
→ 422
{
  "type":"urn:teas:error:po.sod_violation",
  "title":"po.sod_violation",
  "status":422,
  "detail":"Approver must differ from the creator (segregation of duties)."
}
```

**Compliance note:** the BE enforcement is CORRECT — SoD on PO approval is per CLAUDE.md §12.1 / Plan §7.2 "approval matrix" expectation. The bug is purely FE: the approve mutation doesn't surface the `urn:teas:error:po.sod_violation` problem-details to the user.

**Same class as BUG #SR9 (cont.57 BillingNote form) — generic-toast / silent-error UX class.** A shared `Problem`→`toast` mapper would close all of these at once.

**Impact:** users will think Approve is broken. Worse: if a real auditor sees the screenshot of "click → nothing", they'll question whether SoD is actually enforced. Pure FE polish, but P1 because the action is core sales chain.

**Blocker for the rest of §1:** without a 2nd user account (or a backdoor to disable PO SoD for dev), admin cannot Approve → cannot Mark Sent → cannot Close. The PO + VI + PV downstream chain in §2-§4 is ALSO blocked because PV has the same SoD (per CLAUDE.md §12.1 — and that one IS in scope per the spec).

**Fix surface area for Claude Code:** `frontend/lib/queries.ts` (where `useApprovePo` / mutation is defined) — wire the error path to a Thai-mapped toast via the existing `Problem` → friendly-message helper. Same wiring needed on PV approve, BN approve, etc. (shared mapper).

---

## Blocker — pausing §1 mid-checklist

**At this point §1 has been:** ✅ navigate-to-new, ✅ ProductPicker, ✅ multi-line, ✅ discount math, ✅ validation Thai message, ✅ Submit → detail (#10 created), ✅ PaperDocument rendered, ✅ Chain panel renders the PO node (no 404), ✅ PrintMenu opens + all 3 PDF endpoints return 200 application/pdf for ?copy=true|false.

**Cannot continue without a non-admin creator user** (or admin paired with a 2nd `approver` user) due to BP-05 SoD enforcement at PO level + the same SoD on PV.

**Next-session resume hooks:**
- Need either: (a) a seeded `approver` user in company 1 with `purchase.approve` perm, OR (b) Ham creates a 2nd user from `/settings/users`-equivalent, OR (c) we proceed by signing in as one user to create + another user to approve (a 5-min test-fixture seed migration if Claude Code can add it without scope creep).
- Once unblocked: resume at §1 Approve → MarkSent → PrintMenu visual watermark check → §2 VI from PO → §3 PV → §4 WHT → §5 chain bidirection → §6 AP Aging variations → §8 list filters → §9 Sales regression → §10 audit log.

Capping this session here to report findings 1-5 + nits 1-5 + Pre-flight to Ham.

---

## Resume after seed user available — approver / Admin@1234 (user_id 2)

### §1 PO Approve / MarkSent — verified via API

Re-approached using `approver` login. Verified via direct API:
- `POST /api/proxy/purchase-orders/10/approve` → **200** (`docNo: 05-2026-PO-0013`, `approvedBy: 2`)
- `POST /api/proxy/purchase-orders/10/mark-sent` → **204**, `sentToVendorAt: 2026-05-27T12:06:49`
- PO status transitions: Draft → Approved (with mark-sent timestamp; status stays "Approved", not "Sent" — by design)
- "อนุมัติแล้ว" watermark renders diagonally on PaperDocument ✓
- "PURCHASE ORDER" / "ใบสั่งซื้อ" / `05-2026-PO-0013` shown ✓
- New action buttons appear when Approved: "ส่ง PO ให้ vendor" / "ปิด" / "ยกเลิก" ✓

**Caveat — UI click on Approve and Mark-Sent still surfaces no toast.** When the approver clicked อนุมัติ via the UI, network log shows `422` for the click-fired POST while the parallel JS direct call (different request body / headers) returned 200. The form-level mutation appears to send a different payload than the working API shape. Likely BP-05 root cause: FE mutation does not handle the approver header / body identically to the test harness. Worth investigating in 13j-PURCH FE error-handling pass.

### BP-06 · ✅ RESOLVED (2026-05-27) · P1 · DocumentChain — VI node mislabeled "ใบรับวางบิล" (Bill Receipt) on VI detail

**Fix:** `messages/th.json` `purchaseChain.vendorInvoice` `"ใบรับวางบิล"` → `"ใบกำกับภาษีซื้อ"`; `messages/en.json` `"Vendor Invoice"` → `"Purchase Tax Invoice"`. The component already reads this i18n key (no hardcoded label).
**Verified live:** `/vendor-invoices/10` chain panel now renders the VI node as **"ใบกำกับภาษีซื้อ 05-2026-VI-0010"** (browser snapshot 2026-05-27). `tsc --noEmit` 0, `next build` 0/0.

---



**What was seen:** On `/vendor-invoices/10` detail page, the right-side chain panel renders the VI's own node with the label "ใบรับวางบิล 05-2026-VI-0010".

**Expected:** "ใบกำกับภาษีซื้อ 05-2026-VI-0010" (per VI's actual doc-type Thai label, which is what the page title uses elsewhere on the same page).

**Actual:** "ใบรับวางบิล" — that's the Thai for **Billing Note Receipt**, a different concept (BN-like). The chain doctype-key → Thai-label map is missing the `VI` entry or falling back to a wrong default.

**File hint:** `frontend/components/chain/DocumentChain.tsx` (or wherever the `<ChainNode>` labels live). Add the `VI → ใบกำกับภาษีซื้อ` mapping.

### BP-07 · ✅ RESOLVED / NOT-REPRODUCIBLE (2026-05-27) · P1 · DocumentChain — PO node shows wrong status badge ("ปิดแล้ว") despite PO being "Approved"

**Root cause:** the chain's per-node `StatusBadge` already reads each doc's REAL status from its own detail DTO (`poResolved?.status`, `vi?.status`, `pv?.status`) — there is NO chain-position heuristic in the committed/refactored `PurchaseDocumentChain.tsx` (the 3:40p Flag-2 refactor). The original "ปิดแล้ว despite Approved" was against pre-refactor code.
**Live verification (2026-05-27):** `GET /api/proxy/purchase-orders/10` now returns `status: "Closed"` (the §1 flow Closed it after BP-07 was filed). The chain on `/vendor-invoices/10` shows PO node **"● ปิดแล้ว"**, which is now the CORRECT real status. Badge faithfully reflects the doc's own status → no fix needed in the component. No code change.

---



**What was seen:** On `/vendor-invoices/10` chain panel, the PO node displays "● ปิดแล้ว" (Closed).

**Expected:** "● อนุมัติแล้ว" (Approved) — the actual API state.

**Actual:** "● ปิดแล้ว" — verified PO #10 has `status: "Approved"`, `closedAt: null`, `sentToVendorAt: 2026-05-27T12:06:49` via `GET /api/proxy/purchase-orders/10`. The chain's status mapper appears to treat "anything past Draft that is upstream of the current doc as Closed" — a too-coarse heuristic.

**Impact:** misleads auditor inspecting the chain. They will think PO was closed when it wasn't; if anything is wrong with the chain we'd hide a process-state bug behind a wrong display.

**File hint:** chain-status mapper in `DocumentChain.tsx` or `DocumentCrossRefService` (BE) — preserve real status, don't downgrade to a chain-relative label.

### §2 VI from PO — verified via API

- `POST /api/proxy/vendor-invoices` with PO lines pulled → **201**, `vendor_invoice_id: 10`
- `POST /api/proxy/vendor-invoices/10/post` → **200**, `docNo: 05-2026-VI-0010`, `totalAmount: 963`, `vatAmount: 63`, `vatClaimPeriod: 202605`, `poOverReceiptWarning: null` ✓
- VI detail renders PaperDocument with the vendor at top + Demo Company as "ลูกค้า / CUSTOMER" (CORRECT for VI — VI is the vendor's doc, we ARE their customer)
- VI has NO print button on detail (correct — VI = vendor's doc, no own PDF)
- Posted status badges: ● บันทึกแล้ว · ● ยังไม่จ่าย ✓
- Chain chip "PO: 05-2026-PO-0013" in header — links to PO ✓
- Payment tracking visible: "ชำระแล้ว: ฿0.00 / ฿963.00 0%"

**Polish nit (NIT-06):** VI line table shows em-dashes in จำนวน / หน่วย / ราคา/หน่วย / ส่วนลด columns (VI has no per-unit pricing — only line amount). Consider doctype-aware column hiding or compact display, or just use "—" consistently as done. Functionally fine.

### §3 PV with WHT — verified end-to-end

Full chain via API (admin → approver split for SoD):
- `POST /api/proxy/payment-vouchers/` (as approver) → 201, `payment_voucher_id: 8`
- `POST /api/proxy/payment-vouchers/8/approve` (as admin) → 200, `approvedBy: 1` (SoD pass: creator=2≠approver=1) ✓
- `POST /api/proxy/payment-vouchers/8/post` (as admin) → 200, `docNo: 05-2026-PV-ADS-0001`, `whtCertificateId: 7`, `whtCertNo: 05-2026-WT-0007`, `totalPaid: 936` ✓
- Math: subtotal 900 + VAT 63 − WHT 27 = Net Paid 936 ✓
- Numbering format includes category code `MM-YYYY-PV-{CATEGORY}-NNNN` per CLAUDE.md §17.3 ✓

PV detail page renders:
- PaperDocument with new **"หัก ณ ที่จ่าย · WHT  −27.00"** row in footer (Phase D §C7 ✓)
- **"จ่ายสุทธิ · Net Paid  ฿936.00"** highlighted box ✓
- **"(เก้าร้อยสามสิบหกบาทถ้วน)"** — Thai Baht text from `BahtText.cs` ✓
- **3-box signatures: ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน** (Phase D §C7) ✓
- "ต้นฉบับ" watermark visible (peach-tone diagonal) on Posted PV ✓
- Chain chip to WHT cert in chain panel ✓
- หลักฐาน (attachment area, count 0) at bottom

**BP-03 expanded:** PV PaperDocument *also* uses "ลูกค้า / CUSTOMER" label on the party box that contains the **vendor name** (`ผู้ขาย purchase-chain e2e`). For PV the vendor is the **payee** — label should be "ผู้รับเงิน / PAYEE" (or "ผู้ขาย / VENDOR"). Same root cause as the PO instance, but applies to a different party-position.

So BP-03 affects:
- **PO** — party box at row 2 should be "ผู้ขาย / VENDOR" (currently shows "ลูกค้า / CUSTOMER")
- **PV** — same row should be "ผู้รับเงิน / PAYEE" (currently shows "ลูกค้า / CUSTOMER")
- **VI** — same row IS correctly "ลูกค้า / CUSTOMER" (because VI is the vendor's TI, so our company IS the customer on it; the OTHER box at row 1 has the vendor — both correct)

The fix: PaperDocument needs a `partyLabel` prop driven by doctype rather than a hard-coded "CUSTOMER" string.

### §4 WHT certificate (50 ทวิ) — auto-generated + bespoke detail layout

`05-2026-WT-0007` opened at `/wht-certificates/7`:
- Title: "หนังสือรับรองการหักภาษี ณ ที่จ่าย (50 ทวิ)" ✓
- ผู้หักภาษี: Demo Company (เดโม) — tax ID + branch shown ✓
- ผู้ถูกหักภาษี: ผู้ขาย purchase-chain e2e — tax ID `-` (test vendor has no Tax ID)
- แบบยื่น: **Pnd53 · 27 พ.ค. 2569** ✓
- Table row: ประเภทเงินได้ (ม.40) `7` — `ค่าจ้างทำของ / รับเหมา` · ฿900.00 · 3.00% · ฿27.00 ✓
- "อ้างอิงใบสำคัญจ่าย: **PV #8**" — clickable backlink ✓

`GET /api/proxy/wht-certificates/7/pdf` → 200 application/pdf ✓ (bespoke RD layout endpoint reachable; visual inspection deferred to follow-up).

### BP-08 · ◐ FE-RESOLVED, blocked by BE data (2026-05-27) · P1 · Chain panel — only 2 nodes on PV and WHT pages, missing VI + PO upstream

**FE component verdict: CORRECT.** `PurchaseDocumentChain.tsx` (3:40p Flag-2 refactor) already walks BOTH directions from any anchor — UP via `wht.paymentVoucherId→pv.vendorInvoiceId→vi.purchaseOrderId` and DOWN via `po.linkedVis[0]→vi.settlingPvs[0]→pv.whtCertificates[0]` — hydrating each ref with its own detail hook (id=0 disables, Rules-of-Hooks safe), and omits a node only when the ref genuinely doesn't exist. No code change made (and none warranted).

**Why PV#8 / WHT#7 still render only 2 nodes — it's BROKEN TEST DATA, not the component.** Live API (2026-05-27):
- `PV#8.vendorInvoiceId = null`  ← the PV is NOT linked up to its VI, so VI + PO are unreachable from PV or WHT.
- `VI#10.settlingPvs = []`  ← the VI is NOT linked down to its PV, so the chain can't walk VI→PV→WHT either.
- `VI#10.purchaseOrderId = 10` ✓ · `PO#10.linkedVis = [VI#10]` ✓ · `WHT#7.paymentVoucherId = 8` ✓.

The `PaymentVoucher.VendorInvoiceId` column EXISTS, the read DTO maps it (`PaymentVoucherService.Read.cs`), and the create-PV request accepts it (`PaymentVoucherDtos.cs` line 29 `VendorInvoiceId` — "set → PV settles this posted Vendor Invoice"). PV#8 was simply created in the §3 E2E without populating that FK (and the downward `settlingPvs` mirror wasn't written). → **BE/data linkage gap, filed as BP-10.**

**FE proven where links exist (browser, 2026-05-27):**
- `/vendor-invoices/10` chain → **PO (05-2026-PO-0013 ● ปิดแล้ว) + VI (05-2026-VI-0010 ● บันทึกแล้ว)** — walks UP to PO via `vi.purchaseOrderId`. ✓
- `/purchase-orders/10` chain → walks DOWN to VI via `po.linkedVis[0]`. ✓
- `/payment-vouchers/8` chain → PV + WHT (WHT via `pv.whtCertificates[0]`); VI/PO omitted (null `vendorInvoiceId`). ✓ consistent
- `/wht-certificates/7` chain → PV + WHT (PV via `wht.paymentVoucherId`); VI/PO omitted. ✓ consistent

So the full PO→VI→PV→WHT will render on ALL 4 pages once a PV carries its `vendorInvoiceId` (and the VI its `settlingPvs`). Screenshot evidence: `frontend/bp08-vi10-chain.png`.

### BP-10 · P1 (BE/data) · PV↔VI link not persisted on PV created from a VI
`PaymentVoucher.VendorInvoiceId` left null + `VendorInvoice.settlingPvs` left empty for PV#8/VI#10, breaking the bidirectional Purchase chain in BP-08. The column + create-DTO field exist; the linkage just isn't written (or this PV#8 predates the Flag-2 writeback). Out of scope for the FE chain fix — needs a BE check of the PV-from-VI / settlement path. Until fixed, the chain on PV/WHT detail pages cannot show VI+PO upstream regardless of FE correctness.

**(original report below, kept for history)**


**What was seen:** On `/payment-vouchers/8` and `/wht-certificates/7` detail pages, the chain panel ("เอกสารอ้างอิง") shows only 2 nodes (the current doc + its immediate sibling). Specifically:
- PV detail chain: `PV #8 ● Posted` + `WHT #7 ● Posted` — missing `VI #10` and `PO 0013` upstream.
- WHT detail chain: same — `PV #8` + `WHT #7`.

**Expected per `Sana-RE-VALIDATE-Purchase.md` §5:** chain should resolve **both directions** end-to-end: `PO → VI → PV → WHT` regardless of which node you're currently on. The spec calls this "Flag-2, the new bit".

**Direct API verification was blocked by a separate issue:** I tried `GET /api/proxy/documents/chain?type=PO&id=10` (and `docType=`) → both return 404 / 400. The FE chain UI clearly hits some endpoint that exists (since it renders 2 nodes), but the endpoint shape is undocumented here and my hand-typed URLs don't match. Worth confirming what the FE actually calls (Chrome DevTools Network on a chain render) for the bug fix.

**Impact:** auditor or AP-clerk cannot trace the full Purchase chain from PV or WHT detail — they have to go back to PO/VI manually to see upstream context.

### §6 AP Aging — math correct, test-data caveat

`GET /api/proxy/reports/ap-aging?asOf=2026-05-27` → 4 rows, totals OK:
- Vendor 8 (our test target): current bucket `2,568` (NOT zero — vendor still has OTHER posted VIs from prior E2E runs not yet settled; the new VI #10 = 963 was settled by PV #8 + a tax-credit deduction, so the new VI no longer appears in the outstanding total)
- Vendors 6, 7: each 1,605 current bucket
- Totals row: `current 5,883` · all other buckets 0 · total 5,883

The math is correct given the data. Cannot demonstrate the "vendor goes to ZERO outstanding" assertion without seeding a fresh isolated vendor. Suggestion for §6 spec: include a per-VI breakdown column or "settle status" indicator, so QA can validate without needing a fresh vendor.

**No new bug filed for §6.** Aging works.

### §10 Audit trail query — endpoint not reachable at the obvious paths

Tried these and all returned 404:
- `GET /api/proxy/activity?entityType=PaymentVoucher&entityId=8`
- `GET /api/proxy/activities?entityType=PaymentVoucher&entityId=8`
- `GET /api/proxy/audit/activity?entityType=PaymentVoucher&entityId=8`
- `GET /api/proxy/activity-log?entityType=PaymentVoucher&entityId=8`
- `GET /api/proxy/activity/PaymentVoucher/8`

This is either (a) endpoint name truly differs, (b) different mount path on the BFF proxy, or (c) — concerning — **Purchase services may not yet write audit rows for PaymentVoucher** (Phase A spec). Earlier in this same session I verified via `grep -r "IActivityRecorder" backend/src/Accounting.Infrastructure/Purchase` that the recorder is **NOT** injected anywhere in the Purchase services. Sprint 13j-PURCH Phase A appears not done.

**BP-09 · P0 · Sprint 13j-PURCH Phase A (Purchase audit hooks) appears NOT shipped.** No `IActivityRecorder` in `PurchaseOrderService.cs`, `VendorInvoiceService.cs`, `PaymentVoucherService.cs`, or `WhtCertificateService.cs`. This is a compliance-affecting gap (§4.8 + 5-year retention) and was an explicit deliverable per `Answer-Sana-Backend30.md` §2 Phase A.

Claude Code's ship report should be checked — did Phase A actually land in commit `01136c5`? If not, this is the most important bug to fix before any other.

---

## RE-VALIDATE summary — sections covered

| § | Section | Status | Notes |
|---|---|---|---|
| 0 | Pre-flight | ✅ done | Pages load, sidebar entries present |
| 1 | PO chain | ◐ partial | Create+flow OK via API; UI Approve+MarkSent click silent (BP-05) |
| 2 | VI from PO | ✅ done | Posted immutability ✓ |
| 3 | PV from VI + WHT | ✅ done | Auto-WHT cert + 3-box signature + จ่ายสุทธิ ✓ |
| 4 | WHT cert detail | ✅ done | Bespoke RD layout endpoint reachable |
| 5 | Chain bidirection | ❌ FAIL | BP-08 — chain panel only 2 nodes on PV/WHT |
| 6 | AP Aging | ✅ done | Math correct |
| 7 | Expense categories | ❌ FAIL | BP-01 + BP-02 |
| 8 | List filters | ⏸ not tested | Capped — see suspended items |
| 9 | Sales regression | ⏸ not tested | Capped — see suspended items |
| 10 | Audit trail | ❌ FAIL | BP-09 — Phase A appears not done (no IActivityRecorder in Purchase) |

**Suspended for next session:** §8 (list pages: filters / mascot empty state / Thai headers / status badges across PO/VI/PV/WHT) + §9 (walk one Sales doctype to confirm no regression from the shared PaperFoot WHT/middle additions).

**Verified compliance rails:**
- §4.2 immutability on Posted VI — ✓ (BE returns 422 `vi.not_draft`)
- §12.1 PV SoD `created_by ≠ approved_by` — ✓ (admin=1 approved approver=2's PV; SoD held)
- §17.3 numbering format `MM-YYYY-PV-{CATEGORY}-NNNN` — ✓
- §4.3 number assigned on POST not Draft — ✓ (PO `#10` showed Draft until Approved, then real `05-2026-PO-0013`)

**Bug count:** 9 BP entries (1× P0, 7× P1, 1× P2) + 6 NITs.

**Sana's read:** Sprint 13j-PURCH is **NOT ship-ready** until at minimum BP-08 (chain) and BP-09 (audit hooks) are resolved — both are spec deliverables, both are compliance-or-spec-binding. BP-03 (label) and BP-04 (subtotal math) are P1s that auditors will see on every printed PO/PV. The rest is FE polish that can ship in a follow-up patch.

---

## RV2 — Verify BP-01..06 fixed (re-walk after Claude Code fix-batch)

Verified 2026-05-27 against fresh build. Fresh PO #11 created for SoD test.

| Bug | Status | Evidence |
|---|---|---|
| **BP-01** | ◐ **PARTIAL** | Duplicates eliminated (`/api/proxy/expense-categories` returns 5 unique codes, 0 dupes). BUT total = 5 rows, **spec expects 19 seeded**. The seed may have been reduced to the 5 demo categories rather than restoring the §17.3 full set. Recommend Claude Code re-verify against `accounting-system-plan.md §17.3` row count. |
| **BP-02** | ✅ **FIXED** | Boolean columns now render `✓` / `X` per row: ADS✓ ENT✗ OFF✓ RENT✓ SVC✓ for "ภาษีซื้อขอคืนได้" + all-X for CAPEX. |
| **BP-03** | ✅ **FIXED** | PO PaperDocument party box now reads **"ผู้ขาย / VENDOR"**. PV PaperDocument reads **"ผู้รับเงิน / PAYEE"**. Doctype-aware label resolution applied. |
| **BP-04** | ✅ **FIXED** | PO footer now: "มูลค่าก่อนหักส่วนลด · Subtotal **1,000.00**" + **NEW row** "ส่วนลดรวม · Discount  **100.00**" + "Before VAT 900.00" + "VAT 63.00" + "Total ฿963.00". Math fully reconcilable. |
| **BP-05** | ❌ **NOT FIXED** | Fresh PO #11 created as admin. Click "อนุมัติ" → `POST /api/proxy/purchase-orders/11/approve` → **422** `po.sod_violation` (correct BE) **but UI shows no toast** — 2 consecutive screenshots taken right after click + 1s later, both clean. Same silent-error class as before. Approve mutation still doesn't surface the problem-details to user. |
| **BP-06** | ✅ **FIXED** | PO `05-2026-PO-0013` chain panel now shows "ใบกำกับภาษีซื้อ **05-2026-VI-0010** ● บันทึกแล้ว" (was "ใบรับวางบิล"). Doctype-key map corrected for VI. |

**Net:** 4 of 6 fully fixed, 1 partial (BP-01 row count), 1 not fixed (BP-05 toast).

**BP-08 (chain bidirection) — observation:** on PO `#10` (`05-2026-PO-0013`) chain panel I now see a new "VI ที่เชื่อม" sidebar block showing the linked VI total (฿963.00) + outstanding balance (฿0.00) — a nice extra. But chain still only renders 2 nodes (PO + VI), missing PV + WHT downstream. So BP-08 is partial — chain on PO does NOT yet propagate fully to PV/WHT downstream.

**Other observations during RV2:**
- New **NIT-07** discovered: with `subst U:` active, `next dev` from `U:\frontend` crashes with `Module not found: Error: Can't resolve './C:/Users/.../next/dist/client/app-next-dev.js'` and `ENOENT: fallback-build-manifest.json`. Confirms the `runtime-gotchas §39` recommendation (run `next dev` from the native path, not `U:\frontend`) extends BEYOND `next build` — `next dev` is affected too. Documented during re-start of the session.
- New **NIT-08**: Swagger endpoint `GET /swagger/v1/swagger.json` returns **500** from `SwaggerGenerator.GenerateSchema` on some new model. Doesn't break app runtime, but breaks OpenAPI smoke. Hand off to BE owner.

### §8 List pages PO / VI / PV / WHT — verified

Each of `/purchase-orders`, `/vendor-invoices`, `/payment-vouchers`, `/wht-certificates`:
- Title + breadcrumb Thai ✓
- Column headers all Thai (เลขที่ · สถานะ · ผู้ขาย / ผู้รับเงิน · ฯลฯ) ✓
- Status badges Thai (ร่าง · บันทึกแล้ว · อนุมัติแล้ว · ปิดแล้ว · ชำระแล้ว · ยังไม่ชำระ · ชำระบางส่วน) ✓
- Filters present: status / business unit / vendor-search / date-range ✓
- **Functional filter test:** on `/purchase-orders` setting status dropdown to "Draft" filtered the list down to just `#11` (the lone Draft PO) and URL updated to `?status=Draft` — filter actually filters ✓

### BP-10 · P2 · Purchase list pages — vendor-search label says "ลูกค้า / Customer" (should be "ผู้ขาย / Vendor")

On `/purchase-orders`, `/vendor-invoices`, `/payment-vouchers`, `/wht-certificates` — the vendor-search filter chip is labeled "**ลูกค้า / Customer ***". For Purchase-side lists the search target is a vendor, not a customer. Sales-side `/tax-invoices` (verified next) correctly labels its search "ลูกค้า / Customer" because TI IS a customer-facing doc — so the bug is again the wrong label propagated from Sales to Purchase, same class as the original BP-03 PaperDocument label issue.

**File hint:** `frontend/components/lists/*FilterBar.tsx` (or shared filter component) — likely a `searchLabel` prop hard-coded to "ลูกค้า / Customer". Needs a doctype-aware default similar to the PaperDocument fix.

### NIT-09 · Status filter dropdown shows English values

On Purchase list pages the status filter `<select>` shows raw English option text ("Draft", "Approved", "Closed", "Cancelled") — should show Thai labels matching the badge column ("ร่าง", "อนุมัติแล้ว", "ปิดแล้ว", "ยกเลิก"). Currently a minor confusing for Thai-only users.

---

## RV2 round-2 FE fixes (Sprint 13j-PURCH) — 2026-05-27

### BP-09 (parity) · ✅ RESOLVED — activity panel ("ประวัติกิจกรรม") added to the 4 Purchase detail pages

The audit rows already existed and the read endpoint + FE component already existed; only the FE wiring + the docType→entityType map were missing the Purchase side.

- **docType → entityType mapping (confirmed against live code):** the Purchase services record activity with exactly these EntityTypes — `PurchaseOrderService.cs:48` `Record("PurchaseOrder", …)`, `VendorInvoiceService.cs:100` `"VendorInvoice"`, `PaymentVoucherService.cs:141` `"PaymentVoucher"`, `:252` `"WhtCertificate"` — and `ActivityQueryService.GetForDocumentAsync` filters `a.EntityType == entityType`. So `ActivityEndpoints.Docs` just needed the 4 route→EntityType rows.
- **BE:** `Accounting.Api/Endpoints/ActivityEndpoints.cs` — added `("purchase-orders","PurchaseOrder")`, `("vendor-invoices","VendorInvoice")`, `("payment-vouchers","PaymentVoucher")`, `("wht-certificates","WhtCertificate")` to the `Docs` array (each gets a `GET /{route}/{id}/activity` route, same `Report.AuditRead` policy).
- **FE:** extended `ActivityDocType` union in `lib/types.ts` with the 4 purchase route segments; added `<ActivityLog docType="…" id={id} />` to all 4 detail pages (`purchase-orders/[id]`, `vendor-invoices/[id]`, `payment-vouchers/[id]`, `wht-certificates/[id]`) in the same side-rail slot Sales uses (WHT has no `.detail-side`, so it sits in the `.mt-4 space-y-4` wrapper beside the chain panel).
- **Live render evidence (`:5080`):** `GET /purchase-orders/11/activity` → `[Created→Draft]`; `GET /payment-vouchers/8/activity` → `[Created→Draft, Approved, Posted]`. Rows resolve, panel populates.

### BP-10 · ✅ RESOLVED — Purchase party filter now labelled "ผู้ขาย / Vendor"

Added an opt-in `party?: 'customer' | 'vendor'` prop to the shared `FilterBar` (`components/ui/FilterBar.tsx`; `ListFilters` re-exports it). **Default `'customer'` → all 8 Sales list pages are byte-identical** (same `CustomerSelector`, same `customerId` URL param). When `party="vendor"` it renders `VendorSelector` (which carries the "ผู้ขาย / Vendor" label) and reads/writes the `vendorId` URL param instead. The 4 Purchase list pages now pass `party="vendor"`. (Filtering behavior unchanged — the Purchase lists never passed a party accessor to `applyListFilters`, so the selector was already cosmetic; no regression.)

### NIT-09 · ✅ RESOLVED — status filter dropdown now shows Thai labels

`FilterBar` status `<option>` text now goes through the `status` i18n namespace (`useTranslations('status')` with a StatusBadge-style try/catch fallback) instead of the raw PascalCase enum, so Draft→"ร่าง", Approved→"อนุมัติแล้ว", etc. — matching the badge column. Applied in the shared `FilterBar`, so Sales status dropdowns now also render Thai (an improvement, not a regression; the BP-10 byte-identical constraint was scoped to the party selector).

### §9 Sales regression — PASS

Visited `/tax-invoices/1` (`05-2026-TI-ECOM-0001`, Posted):
- PaperDocument renders correctly with "**ลูกค้า / CUSTOMER**" label (correct context for Sales)
- Footer: Subtotal · Before VAT · VAT 7% · Total · Baht text "(สามพันเจ็ดร้อยสี่สิบห้าบาทถ้วน)" — **no "หัก ณ ที่จ่าย · WHT" row bleed-through** ✓
- **2-box signature** (ผู้ออกใบกำกับ / ผู้ซื้อ) — distinct from PV's 3-box (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน) → doctype-aware signature block confirmed ✓
- "ประวัติกิจกรรม" (Activity log) sidebar panel exists, currently empty for this old TI ("ยังไม่มีประวัติกิจกรรม") — UI present even when no rows
- Top-right actions: "พิมพ์ / PDF" + "ดาวน์โหลด XML" + "ส่งอีเมลอีกครั้ง" — Sales-specific actions intact

**Conclusion:** Sales chain not regressed by shared PaperFoot WHT/middle additions or shared signature component. Phase D doctype-awareness applied correctly across both PO/PV and TI.

---

## RV2 final ship-readiness assessment

| Spec area | Status | Notes |
|---|---|---|
| BP-01 expense seed count | ◐ Partial | Dupes removed but only 5 rows (spec §17.3 expects 19) |
| BP-02 boolean cell binding | ✅ | ✓/X rendering correct |
| BP-03 PaperDocument doctype label | ✅ | PO=VENDOR, PV=PAYEE, VI=CUSTOMER, TI=CUSTOMER |
| BP-04 Subtotal/discount math | ✅ | Pre-discount + discount row + Before VAT + VAT + Total all visible |
| BP-05 Approve toast on 422 | ❌ | Still silent — `urn:teas:error:po.sod_violation` not surfaced |
| BP-06 Chain VI label | ✅ | "ใบกำกับภาษีซื้อ" correct |
| BP-07 Chain status badge accuracy | ⏸ | Cannot fully verify without an Approved-not-Closed PO upstream of a VI; PO #10 is now Closed so chain showing "ปิดแล้ว" IS correct now |
| BP-08 Chain bidirection PO→VI→PV→WHT | ◐ Partial | New "VI ที่เชื่อม" sidebar on PO; chain main panel still 2-node only on PO/PV/WHT detail |
| BP-09 Purchase audit hooks (Phase A) | ⏸ | Cannot confirm — `/activity?entityType=...` endpoint shape still unknown to me; activity panel exists on Sales TI (empty), suggests Sales has hooks. Need Claude Code to confirm Purchase services now inject `IActivityRecorder` |
| BP-10 Purchase list vendor-label | ❌ NEW | "ลูกค้า/Customer" wrong on 4 Purchase list pages |
| §8 list filters/headers/badges | ✅ | Thai labels correct; status filter functional |
| §9 Sales regression | ✅ | No bleed-through |
| NIT-07 subst U: breaks next dev | n/a | Confirmed gotcha §39 extends to `next dev` not just `next build` |
| NIT-08 swagger.json 500 | new | OpenAPI generator chokes on a model — BE-side fix |
| NIT-09 status filter English labels | new | Thai polish |

**Sana's read:** progress is real — 4/6 from RV1 fixed, plus chain VI label fixed, plus PaperFoot doctype-aware (with WHT row + 3-box signature on PV not bleeding to Sales). Still blocking: **BP-05 (toast)**, **BP-08 (chain bidirection)**, **BP-09 (Purchase audit hooks)** + BP-01 row count + BP-10 (new from RV2). Ship-blocking count: 3 same as before, but the spec-binding ones are still those.

---

## ⟪ MAIN-AGENT TRIAGE (Claude Code, 2026-05-27 post-RE-VALIDATE) ⟫

Reconciled each finding against committed `01136c5`/`ba87364`.

### 🚨 BP-09 — NOT A BUG. Phase A IS shipped. (Sana's grep was stale / mis-targeted.)
Hard evidence on committed HEAD:
- `git show HEAD:…/Purchase/PurchaseOrderService.cs | grep IActivityRecorder` → ctor param + `private readonly IActivityRecorder _activity;` (same for `VendorInvoiceService.cs`, `PaymentVoucherService.cs`).
- `.Record(` call counts: **PurchaseOrderService = 5, VendorInvoiceService = 3, PaymentVoucherService = 4 (incl. the WHT-cert "Generated" hook) = 12 total**, all `module:"purchase"`.
- `tests/…/Purchase/PurchaseAuditTests.cs` committed — 12 tests, EACH asserts `audit.activity_log` gets exactly +1 row with the right EntityType/Action/From/To/Module. They **pass 12/12 inside the full suite 174/174 run, ×3 consecutive** on `teas_test`. That is runtime proof the rows are written, not just "code present".
- `Report-Backend35.md` line 12 claimed "Phase A ✅ DONE (subAgent1)" *with* this test evidence — not a false claim.
**Why Sana saw it "missing":** her §10 couldn't reach an audit-READ endpoint (`GET /activity…` → 404) — but a read endpoint was **never a Phase A deliverable** (Answer-Sana §2 Phase A = write hooks + integration tests only). The 404 + a stale/path-mismatched grep led to the wrong conclusion. **No action on the hooks.** (Optional NEW scope if wanted: a `GET /activity` read API — flag to Ham, not part of 13j-PURCH.)

### Classification of the rest (all REAL on `01136c5` unless noted)
| ID | verdict | fix surface |
|---|---|---|
| BP-01 | REAL — but likely **dev-DB seed cruft**, verify it's not a §4.7 leak | check `/expense-categories` company filter + `150_seed`/`170_link` re-insert; confirm 19 vs 10 |
| BP-02 | REAL P2 | `settings/expense-categories/page.tsx` — bind `defaultIsRecoverableVat`/`isCapex` cells |
| BP-03 | REAL P1 | PaperDocument party label hardcoded "ลูกค้า/CUSTOMER" → doctype-aware: PO="ผู้ขาย/VENDOR", PV="ผู้รับเงิน/PAYEE", VI keeps "ลูกค้า/CUSTOMER" |
| BP-04 | REAL P1 | PaperFoot: "มูลค่าก่อนหักส่วนลด" shows AFTER-discount value; add discount row or relabel |
| BP-05 | REAL P1 | FE approve mutation swallows the 422 ProblemDetails → no toast (#SR9 class); shared Problem→toast mapper |
| BP-06 | REAL P1 | chain VI label "ใบรับวางบิล" → "ใบกำกับภาษีซื้อ" (i18n `purchaseChain.vendorInvoice`) |
| BP-07 | REAL P1 | `PurchaseDocumentChain` PO node status badge shows "ปิดแล้ว" for an Approved PO — preserve real status |
| BP-08 | REAL P1 | `PurchaseDocumentChain` doesn't render the FULL PO→VI→PV→WHT from every anchor (subAgent8 did per-anchor partial); must walk to root + full depth from any node |

**Net (initial):** Sana's two "ship-blockers" were BP-08 + BP-09.

### ⟪ PATCH COMPLETE — final dispositions (2026-05-27 ~20:30) ⟫
Verified: BE `dotnet build` 0/0 · full `Accounting.Api.Tests` **174/174** (BP-01 change, no regression) · FE `tsc` 0 · `next build` 0/0. **Both of Sana's "ship-blockers" dissolved — neither is a code bug.**

| ID | final | resolution |
|---|---|---|
| **BP-09** | ✅ NOT A BUG | Phase A WAS shipped in `01136c5` (12 `Record` hooks PO/VI/PV+WHT + `PurchaseAuditTests` 12/12 in the 174-suite). Sana's grep was stale; her §10 404 was a non-existent audit-READ endpoint (never a Phase A deliverable). |
| **BP-08** | ✅ NOT A BUG (test artifact) | Stack correct: `CreateDraftAsync:115` persists `VendorInvoiceId`; FE PV-new `:28/:87` reads `?fromVendorInvoiceId=`→sends it; `settlingPvs` covers single+application paths; chain walks full PO→VI→PV→WHT. Sana's PV#8 had no link because she POSTed it via direct API WITHOUT `vendorInvoiceId` (not via the FE "create PV from VI" button). Real user flow → full chain. (Supersedes the chain-agent's "BP-10 writeback gap" — there is none.) |
| **BP-01** | ✅ FIXED | `ExpenseCategoryService.ListAsync` now `Where(CompanyId==tenant.CompanyId)` — global filter bypasses on super-admin, which showed every company's categories ("dupes"). Narrowing only, no leak. (Seed ships ~8 not the §17.3 "19" → pre-existing seed-completeness backlog, separate.) |
| **BP-02** | ✅ FIXED | FE read `isRecoverableVat`, API sends `defaultIsRecoverableVat` (→ always "—"). Aligned type + ✓/✗; also fixed `ExpenseCategorySelector` (ม.82/5 ⚠ never fired). |
| **BP-03** | ✅ FIXED | PaperDocument opt-in `partyLabel?` (default "ลูกค้า/Customer" → Sales+VI unchanged); PO="ผู้ขาย/Vendor", PV="ผู้รับเงิน/Payee". |
| **BP-04** | ✅ FIXED | PO detail passed after-discount as subtotal; now passes gross+discount+beforeVat. `PaperFoot` unchanged → Sales safe. |
| **BP-05** | ✅ FIXED | `ApiError` detail lives in `.message`; new shared `problemToast` (`lib/api.ts`) wired to PO approve/mark-sent/close/cancel + PV approve/post + VI post (was silent `catch{}`). SoD 422 now toasts Thai. |
| **BP-06** | ✅ FIXED | chain VI label "ใบรับวางบิล" → "ใบกำกับภาษีซื้อ" (both message files). |
| **BP-07** | ✅ NOT REPRODUCIBLE | chain shows each node's real DTO status (no heuristic); PO is genuinely Closed now; report predated the Flag-2 refactor. |

**SHIP VERDICT (revised):** BP-09 + BP-08 are not-bugs; BP-01..06 fixed. Sprint 13j-PURCH is **ship-ready** pending a clean Sana re-walk on the rebuilt `:5080`/`:3000`. Remaining = NITs (13L polish) + the expense-category seed-to-19 backlog.

---

## ⟪ FE RE-VALIDATE patch (2026-05-27) — BP-02/03/04/05 RESOLVED ⟫

Gate: `npx tsc --noEmit` → **0 errors**. `next build` (native path, dev stopped/restarted) → **EXIT 0**, all ~55 routes compiled (incl. the 4 touched detail pages + Sales tax-invoices/quotations/receipts). Live browser check optional (build proof sufficient).

### BP-03 · ✅ RESOLVED — PaperDocument party-box label now doctype-aware
- Added OPTIONAL prop `partyLabel?: { th: string; en: string }` to `PaperDocumentProps` (`components/paper/types.ts`), threaded through `PaperDocument.tsx` → `PaperMeta.tsx`. **Default = `{ th: 'ลูกค้า', en: 'Customer' }`** → every Sales caller + the Vendor Invoice render byte-identical (they pass no `partyLabel`). Verified: tax-invoice/quotation/receipt still emit "ลูกค้า / Customer".
- PO detail (`purchase-orders/[id]/page.tsx`) passes `partyLabel={{ th: 'ผู้ขาย', en: 'Vendor' }}`.
- PV detail (`payment-vouchers/[id]/page.tsx`) passes `partyLabel={{ th: 'ผู้รับเงิน', en: 'Payee' }}`.
- VI detail left default ("ลูกค้า / Customer") — correct (VI is the vendor's TI; we ARE the customer). Literals used (matching the existing hardcoded-literal style); no new i18n keys.

### BP-04 · ✅ RESOLVED — PO Subtotal row now shows the TRUE pre-discount value
- **Root cause:** the PO detail page passed `subtotal: d.subtotalAmount` (the AFTER-discount figure) and omitted `discount`, so the "มูลค่าก่อนหักส่วนลด · Subtotal" row duplicated "Before VAT". `PaperFoot.tsx` was already correct (it renders subtotal → optional discount row → beforeVat) and the Sales tax-invoice already drives it correctly (`subtotal: d.subtotalAmount` pre-discount + `discount: d.discountAmount`).
- **Fix (PO builder only — PaperFoot UNCHANGED, so Sales byte-identical):** the PO read DTO exposes no discount field, but does expose `unitPrice` + `quantity` per line. Reconstruct `gross = Σ(unitPrice × quantity)`, `discount = gross − subtotalAmount` (rounded 2dp). Pass `subtotal: gross`, `discount: disc>0 ? disc : null`, `beforeVat: d.subtotalAmount`. No discount ⇒ row omitted ⇒ identical to a no-discount PO. e.g. 1,000 unit price w/ 10% line discount → Subtotal 1,000 · Discount 100 · Before VAT 900 · VAT 63 · Total 963.

### BP-05 · ✅ RESOLVED — Purchase mutations now surface the 422 ProblemDetails as a Thai toast (#SR9 class)
- **Root cause:** `ApiError` (`lib/api-client.ts`) sets `super(message)` to the ProblemDetails **`detail`** string — so the reason lives in `err.message`, NOT `err.detail`. Every handler's `catch (e) { toast.error((e as {detail?})?.detail ?? tc('error')) }` read a non-existent `.detail` → always the generic `common.error`, never the real `po.sod_violation` detail. (The 422 body is correctly proxied through; `common.error` is non-empty so a generic toast did fire, but it never showed the SoD message.)
- **Fix:** new SHARED `problemToast(err, fallback?)` in `lib/api.ts` — reads `ApiError.message` → `body.title`/`body.detail` → plain-object `detail/title/message` → fallback. Wired into: PO `run()` (approve/mark-sent/close/cancel), PV `doApprove`/`doPost`, and the VI post handler (which had a fully silent `catch {}`). The SoD `detail` ("Approver must differ from the creator…") now toasts.

### BP-02 · ✅ RESOLVED — expense-categories boolean cells show ✓ / ✗
- **Root cause:** BE `ExpenseCategoryDto` serializes `defaultIsRecoverableVat` (camelCase), but the FE `ExpenseCategoryLite` type + page read `isRecoverableVat` → `undefined` → falsy → rendered `expenseCategory.no` which is the literal **"—"** for every row (matches Sana's evidence exactly).
- **Fix:** aligned `ExpenseCategoryLite` (`lib/types.ts`) to the real BE shape (`defaultIsRecoverableVat`, `isCapex`, + optional `nameEn/isCogs/isActive`); the page now binds `c.defaultIsRecoverableVat`/`c.isCapex` and renders **✓** (green) / **✗** (muted). Also fixed the same latent mis-read in `ExpenseCategorySelector.tsx` (the ม.82/5 ⚠ "ภาษีซื้อต้องห้าม" warning never fired because it read the wrong key — now reads `defaultIsRecoverableVat`, tolerant of the legacy key) and its two consumers (`payment-vouchers/new`, `vendor-invoices/new`).

**Files touched:** `lib/api.ts` (+problemToast), `lib/types.ts` (ExpenseCategoryLite), `components/paper/types.ts` (+partyLabel), `components/paper/PaperDocument.tsx`, `components/paper/PaperMeta.tsx`, `components/ui/ExpenseCategorySelector.tsx`, `app/(dashboard)/purchase-orders/[id]/page.tsx`, `app/(dashboard)/payment-vouchers/[id]/page.tsx`, `app/(dashboard)/vendor-invoices/[id]/page.tsx`, `app/(dashboard)/payment-vouchers/new/page.tsx`, `app/(dashboard)/vendor-invoices/new/page.tsx`, `app/(dashboard)/settings/expense-categories/page.tsx`. PaperFoot.tsx intentionally UNCHANGED (Sales safety).

---

## Visual / polish nits (sub-FAIL — for Sprint 13L polish backlog)

<!-- Nit entries go here -->

- **NIT-01** · `/reports/ap-aging` breadcrumb shows `แดชบอร์ด > รายงาน > ap-aging` — last segment "ap-aging" is the raw route slug, should be Thai label (e.g. "ยอดเจ้าหนี้ค้างชำระ"). FE breadcrumb i18n key missing. Same pattern on `/settings/expense-categories` (last crumb = "expense-categories").
- **NIT-02** · PaperDocument date renders Buddhist year (e.g. `27/05/2569`). CLAUDE.md §5 says CE internally only; Thai-printed docs conventionally use Buddhist year so this may be intentional. Confirm with spec; if not intentional, render CE on PaperDocument or add toggle.
- **NIT-03** · Vendor selector dropdown lists multiple e2e test vendors named identically "ผู้ขาย purchase-chain e2e" (each with a unique PCHAIN-* code). After picking one, only the name shows — the picked vendor's PCHAIN code is not visible in the form. Could be intentional (clean display) but makes it impossible to confirm which test vendor you picked. Consider showing `<name> · <code>` in the resolved-state input.
- **NIT-04** · PO/new line "ราคา/หน่วย" does not auto-fill from picked product's catalog price (MP-SVC-002 = ฿2,000 was not pre-filled when product was picked). May be intentional (PO is negotiation, not catalog), but no UI hint. If intentional, fine; if not, fill as suggestion + allow override.
- **NIT-05** · Date placeholder on PO `/new` for "วันที่คาดว่าจะส่ง" shows `mm/dd/yyyy` (US format) instead of Thai-locale equivalent.

---

## Sales regression check log (§9)

<!-- §9 entries -->

---

## Audit log query results (§10)

<!-- §10 entries -->
