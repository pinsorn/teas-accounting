# Answer-Sana-Question-Backend4 — Mid-Sprint 4 Decisions

**Date:** 2026-05-16  
**From:** Ham (via Sana, Cowork)  
**To:** Claude Code  
**Re:** [Question-Backend4.md](./Question-Backend4.md) — Receipt/CN/DN model conflicts vs Sprint 4 spec

> 5th escalation save. The Sprint 4 directive I wrote contained two design overreaches —
> standalone-Receipt and line-based-CN. Both fight existing legal-doc models that ARE already
> compliant. You called them out exactly right. Decisions below: don't redesign legal documents
> for nicer UX; reuse what works.

---

## Q1 — Receipt scope: **(b) Defer standalone receipts** ✅

**My Sprint 4 spec said:** "ใบเสร็จ standalone (when not combined with TI), optional TI reference."

**Why that was wrong:** I treated Receipt as a flexible "got money from customer" doc without
thinking through the GL implications. Standalone receipts (with no prior TI) need an advance/
liability account (เงินรับล่วงหน้า) and a separate "convert to settled when TI later issued"
workflow — that's a feature, not a small extension. The existing **application-based** model
(Receipt MUST reference ≥1 TI) is correct, compliant, and the predominant B2B use case.

**Decision: keep the shipped application-based Receipt model unchanged for Sprint 4.**

- Receipt requires ≥1 TI application (current validator stays).
- GL: Dr Cash/Bank / Cr AR settling each linked TI's `AmountPaid` / `PaymentStatus` (current
  flow stays).
- Field "optional TI reference" from the Sprint 4 directive → **drop**. TI reference is
  mandatory.
- Frontend form: pick customer → list their unpaid TIs → multi-select TIs to apply → enter
  amounts → optional WHT line if the customer deducted WHT → post.
- Standalone receipts (no TI link, advance payment use case) → **deferred** to a future
  sprint as its own feature, not a Sprint 4 add-on.

This unblocks the Receipt create form — model + GL + endpoints all already work, you just
build the UI over the shipped contract.

---

## Q2 — CN/DN model: **(a) Keep amount-based + add reasonCode enum** ✅

**My Sprint 4 spec said:** "Lines: editable but capped at original quantities."

**Why that was wrong:** I conflated "nicer UX" with "what the law requires". ม.86/10 requires
the CN to identify the original TI, state the reason, and state the amount reduced — **not**
require line-level granularity. The shipped amount-based model
(`adjustmentSubtotal + taxRate + reason + originalTaxInvoiceId`) is fully compliant. Adding
line-level snapshot + per-line caps + per-line GL would be a structural redesign of a legal
aggregate for marginal UX gain. Not worth it.

Bonus: amount-based is closer to how accountants actually work in practice — they think
"reduce by 1,000 baht" not "drop qty of line 3 from 2 to 1".

**Decision: keep amount-based CN/DN. Add the `reasonCode` enum.**

- Aggregate root (`TaxAdjustmentNote`) — unchanged structure.
- Add field: `reasonCode` enum (additive, safe).
- Frontend CN form:
  1. Pick original TI (search by doc-no or customer)
  2. Display original TI summary (read-only): total, subtotal, vat, customer name
  3. Pick `reasonCode` from dropdown (TH labels via next-intl)
  4. Enter `adjustmentSubtotal` (amount to reduce). Validation: `0 < adj ≤ originalSubtotal`
  5. `taxRate` auto-fills from original TI's effective rate
  6. `reasonText` free-form textarea (required; this is what shows on the credit note + ภ.พ.30 audit)
  7. PostConfirm shows: "ออกใบลดหนี้แล้วจะลด Output VAT ในเดือนที่ออกใบ + ส่ง e-Tax/email (เมื่อเปิด)"
- DN form: same shape, mirror values (Dr/Cr swapped per existing `PostTaxAdjustmentNoteAsync`).

Line-based CN remains a "future feature" — file it under `plan.md` if you like, but I'm not
prioritizing it. Compliance does not require it.

---

## Q3 — Reason code enums

### Q3a — CN (`CreditNoteReasonCode`)

Use the set I wrote in Answer-Sana-Backend4 — confirmed binding:

```csharp
public enum CreditNoteReasonCode
{
    Typo,           // พิมพ์ผิด — ชื่อ/ที่อยู่/Tax ID ลูกค้า
    AmountError,    // คำนวณผิด — จำนวนเงินผิด
    CustomerInfo,   // แก้ไขข้อมูลลูกค้า
    Return,         // ลูกค้าส่งคืนสินค้า
    PriceReduce,    // ลดราคา / ส่วนลดเพิ่มเติม
    Cancel,         // ยกเลิกธุรกรรม / ไม่ส่งสินค้า
}
```

### Q3b — DN (`DebitNoteReasonCode`) — **own enum, not "CN filtered"**

DN use cases ไม่ overlap CN เต็มที่ — Return / Cancel ไม่ apply. ใช้ enum แยกชัดดีกว่า:

```csharp
public enum DebitNoteReasonCode
{
    PriceIncrease,    // ปรับราคาเพิ่ม (under-billed in original)
    AdditionalCharge, // ค่าบริการ/ขนส่ง/ค่าธรรมเนียมเพิ่ม
    ScopeExpansion,   // ขยาย scope งาน (เพิ่มของ/บริการนอกใบเดิม)
    Typo,             // พิมพ์ผิดทำให้ amount under-stated
}
```

Don't reuse CN enum filtered — UI label mapping diverges (e.g. `Typo` is shared but `Return`
must not appear in DN). Separate enums = type-safe + i18n strings clean.

UI implication for DN form: same shape as CN, just different reason dropdown source + same
PostConfirm wording but with "เพิ่มหนี้" / "Output VAT เพิ่ม" framing.

---

## Lessons (for me — Sana)

1. **Don't spec UX patterns that imply a model change without saying so.** "Lines capped at
   original qty" looks like form-validation but means "redesign the aggregate". I should have
   either flagged it explicitly as a model change or skipped it. Same for "optional TI ref"
   on Receipt — it looked harmless but implied an entire advance-payment workflow.
2. **Trust the shipped model when it's legally compliant.** ม.86/10 doesn't require line-level
   granularity; the existing amount-based model is correct. UX wishlist ≠ legal requirement.
3. **The 5th escalation in a row** (XAdES C14N → Same-day Void → Next docs § dir → Receipt
   spec → CN line-based) — the CLAUDE.md §8 "flag, don't improvise" rule has paid for itself
   many times over. Keep doing it.

---

## Side work Sana did in parallel (no impact on your Sprint 4)

1. **`docs/api/openapi.yaml` updated** — added what you've shipped:
   - `GET /tax-invoices` cursor list + `TaxInvoiceListResponse` schema
   - `GET /customers` (paginated, with `search`) + `CustomerListItem` schema, deprecated `/customers/search`
   - `GET /reports/number-gaps` + `NumberGapsResponse` schema
   - Permission noted: `report.audit.read`
2. **`db/schema.sql` updated** — added `tax.v_number_gaps` view definition with full comment
   block (compliance §17.6 / §4.3). View body UNIONs across TIs + JV; structured to extend
   to PV/CN/DN/Receipt as their schemas mature — add the corresponding `UNION ALL` block
   when you ship each one.
3. **TH copy review of `frontend/messages/th.json`** — read once, professional and tonally
   consistent. No edits needed. **One small note:** `numberGaps.subtitle` says
   "ตามมาตรฐานเลขเอกสารต่อเนื่อง (ม. การบัญชี)" — accurate but could read more naturally as
   "ตามมาตรฐานพรบ.การบัญชี เลขเอกสารต้องต่อเนื่อง". Trivial; defer to your judgement during
   final TH pass.

---

## Acknowledge

Append to `progress.md`:  
`Answer-Sana-Question-Backend4 received — Q1=defer-standalone (b), Q2=amount-based (a), Q3 enums confirmed. Executing.`

Then finish Sprint 4 and write `Report-Backend5.md` (with screenshots, please — 4-5 frames
is plenty per Answer-Sana-Backend4).

Don't get fancy. Ship the slice.
