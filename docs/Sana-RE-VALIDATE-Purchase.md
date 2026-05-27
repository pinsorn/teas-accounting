# Sana RE-VALIDATE — Sprint 13j-PURCH (Purchase / AP Phase 1)

> For the next Chrome-MCP validation session. Commit under test: `01136c5`.
> **GROUND RULE — CLICK EVERYTHING. Do NOT pass a screen just because the UI *looks* right.**
> Press every action button, open every print option, click every chain node, run every export,
> and CONFIRM the resulting state/file/redirect. A page that renders but whose button 500s/404s is a FAIL.
> Log each ❌ with: page · button/action clicked · what happened vs expected · console/network error.

**Servers:** backend :5080 (`ASPNETCORE_ENVIRONMENT=Development`) · frontend :3000 · login `admin / Admin@1234` (company 1).
**Mode note:** some buttons are VAT-only; if testing non-VAT, expect VAT/TI controls hidden (that's correct).

---

## 0. Pre-flight
- [ ] `/reports/ap-aging` and `/settings/expense-categories` both load (new routes).
- [ ] Sidebar → reports section shows "เจ้าหนี้ค้างชำระ"; settings shows "หมวดค่าใช้จ่าย". Purchase menu items unchanged.

## 1. Purchase Order — create + every action button
- [ ] `/purchase-orders/new` → **CLICK** "เลือกจากรายการ" (ProductPicker modal) → pick a product → confirm line fills + VAT rate locks.
- [ ] **ADD a 2nd line** (multi-line is the Phase F lift — a 1-line-only form = FAIL). Set a discount % on one line → totals recompute.
- [ ] Submit → lands on `/purchase-orders/[id]` (not an error toast). Trigger a validation error on purpose (empty line) → confirm a **specific Thai** message, NOT generic "เกิดข้อผิดพลาด".
- [ ] On detail: **CLICK "อนุมัติ" (Approve)** → status → อนุมัติแล้ว. **CLICK "Mark Sent"** → SentToVendorAt set.
- [ ] **PrintMenu → CLICK "พิมพ์ต้นฉบับ"** → a PDF opens/downloads (not 400/404). Confirm watermark = ต้นฉบับ.
- [ ] **PrintMenu → CLICK "พิมพ์สำเนา"** → PDF opens, watermark = สำเนา. (Both hit `/purchase-orders/{id}/pdf?copy=…`.)
- [ ] Confirm the PO paper layout matches Sales (PaperDocument), seller = our company, vendor as the party.
- [ ] **CLICK every node in the chain panel** — at this point only the PO node exists; links must not 404.

## 2. Vendor Invoice — from PO, every action
- [ ] Create VI from the PO (lines pull from PO). **CLICK "Set Claim Period"** → period set. **CLICK "บันทึก (Post)"** → Posted.
- [ ] VI detail renders **PaperDocument (read-only)** — NEW this sprint. Confirm lines + vendor + totals show.
- [ ] VI has **NO print button** — that is CORRECT (a VI is the vendor's doc, no `/pdf`). Note it, don't flag.
- [ ] **CLICK the chain panel PO node** → navigates to the PO. Confirm the chain shows PO → VI (current highlighted).
- [ ] Try to edit a field on the Posted VI → must be impossible (read-only, §4.2).

## 3. Payment Voucher — from VI, every action + WHT
- [ ] Create PV from the VI (must use an expense category **belonging to company 1** — see Followups BP-08). Include a WHT line (>0).
- [ ] **CLICK "อนุมัติ"** (approver ≠ creator — SoD) → Approved. **CLICK "บันทึก (Post)"** → Posted.
- [ ] PV detail = **PaperDocument** with the **WHT row + "จ่ายสุทธิ / Net Paid"** in the footer (NEW). Verify net = subtotal+vat−wht.
- [ ] **PrintMenu → ต้นฉบับ AND สำเนา** → both PDFs open, correct watermark.
- [ ] 3-box signature (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน) present.

## 4. WHT certificate (50 ทวิ) — auto-generated
- [ ] Confirm a WHT cert was auto-created when the PV posted (WHT>0). Open `/wht-certificates/[id]`.
- [ ] **CLICK its print** → the bespoke 50 ทวิ PDF opens (RD layout — NOT the generic PaperDocument; correct).
- [ ] Chain panel on the WHT page → **CLICK back up to PV → VI → PO**.

## 5. Document chain — BOTH directions (Flag-2, the new bit)
- [ ] On the **PO** page: chain should resolve **down** PO → VI → PV → WHT. **CLICK each node** → each opens the right doc.
- [ ] On the **PV** page: chain resolves **down to the WHT cert** (new `whtCertificates` ref) AND up to VI/PO.
- [ ] On the **VI** page: chain resolves **down to the PV** (new `settlingPvs` ref).
- [ ] Any node that should exist but shows missing = ❌ (report which direction/page).

## 6. AP Aging report
- [ ] `/reports/ap-aging`: with the PV fully posted, the test vendor should show **ZERO outstanding** (VI settled).
- [ ] Create a 2nd VI (post, no PV) → vendor now shows an outstanding amount in the correct age bucket (0-30/31-60/61-90/>90 by VI doc date).
- [ ] **Change the as-of date** → buckets recompute. **Pick a vendor in the filter** → table narrows. **CLICK "Clear"** → filter resets.
- [ ] **CLICK "ส่งออก CSV"** → a CSV downloads; open it → header + rows + a totals row, Thai labels.
- [ ] Filter to a vendor with nothing outstanding → **MascotGreeting** empty state shows (not a blank table).

## 7. Expense categories (read-only)
- [ ] `/settings/expense-categories` → **count the rows = 19** seeded categories. Confirm NO create/edit/delete controls (read-only).

## 8. List pages — every filter
- [ ] PO / VI / PV / WHT list pages: status chips + vendor + date-range filters each **actually filter** when clicked. Empty result → Mascot. Column headers all Thai (no English bleed). Status badges Thai.

## 9. Regression (don't break Sales)
- [ ] Walk one Sales chain doc (Quotation or Tax Invoice) detail → PaperDocument + PrintMenu still work (the shared PaperFoot/PaperSign gained optional wht/middle — must be invisible on Sales docs).
- [ ] Sales E2E spec is currently red for an unrelated reason (BP-10, missing testids) — confirm the Sales **app** still works by hand; the red is test-infra only.

## 10. Audit trail (compliance)
- [ ] `GET /activity?entityType=PaymentVoucher&entityId=…` (or the UI if present) → rows for Created/Approved/Posted + the WHT "Generated", all `module="purchase"`.

---
**Report back:** per-section ✅/❌ with evidence. Any ❌ on a *button* (not just visuals) → file as `BP-NN` in `bugPurchase.md`. Visual-parity nits → note for polish.
