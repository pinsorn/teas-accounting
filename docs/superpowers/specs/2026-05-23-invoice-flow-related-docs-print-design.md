# Design — Invoice flow + full related-docs chain + universal print original/copy (cont. 69)

**Date:** 2026-05-23 · **Status:** drafted with Ham AFK (decisions D1–D4 locked by Ham in chat; D5–D8
are my conservative defaults — flagged ⚠️ASSUMED, confirm on return).

## 1. Problem / context

1. **Bug:** a non-VAT Delivery Order with `IsCombinedWithTi=true` returns **422** on "ยืนยันส่งมอบ"
   (`MarkDeliveredAsync` auto-creates a Tax Invoice → `EnsureVatRegistered` blocks it, ม.86/4).
2. The document chain has no explicit **Invoice (ใบแจ้งหนี้)** step. Today the chain is
   `Q→SO→DO→TI→RC`; `BillingNote` is a *post-TI* statement that bundles posted TaxInvoices for
   collection — the **opposite** direction from what Ham wants.
3. "Billing Note" wording should disappear from the whole UI → **Invoice / ใบแจ้งหนี้**.
4. Related-documents panels show only a partial chain; Ham wants the **full chain Q→RC** on every doc.
5. Print original/copy + "original already printed" warning exists only for TI/RC/CN/DN; Ham wants it on
   **every** document.

## 2. Target document flow (Ham-confirmed)

```
VAT:      Quotation → Sales Order → Delivery Order → Invoice → Tax Invoice → Receipt(apply TI)
non-VAT:  Quotation → Sales Order → Delivery Order → Invoice → Receipt(apply Invoice)
                                                              ↘ (or standalone cash Receipt)
```

- **DO "mark-delivered" = status change only.** The `IsCombinedWithTi` auto-TI path is **removed** for
  new docs (fixes the 422). Legacy DOs already `IsCombinedWithTi` are left untouched (read-only history).
- **Invoice is created manually from a DO** ("สร้างใบแจ้งหนี้" button on the DO detail). The Invoice copies
  the DO's lines + customer snapshot + `DeliveryOrderId` source link.
- **Tax Invoice is created manually from an Invoice** ("ออกใบกำกับภาษี" button on the Invoice detail,
  **VAT mode only**). The TI copies the Invoice lines + `BillingNoteId` source link. (Replaces DO→TI.)
- **Receipt:** VAT → apply to TI (unchanged). non-VAT → apply to **Invoice** (replaces the cont.68
  apply-to-DO) + standalone cash bill stays.

## 3. Decisions

- **D1 (Ham):** Invoice created manually from DO; TI created from Invoice. ✔
- **D2 (Ham):** DO mark-delivered = status only; drop combined-TI auto. ✔
- **D3 (Ham):** non-VAT Receipt applies to Invoice (+ standalone). ✔
- **D4 (Ham):** Rename "Billing Note" → "Invoice / ใบแจ้งหนี้" everywhere user-visible. ✔
- **D5 ⚠️ASSUMED — rename depth:** FE fully renamed (nav, labels, i18n, **route `/billing-notes`→`/invoices`**,
  page titles, PDF doc label). **BE keeps** the internal `BillingNote` entity / `billing_notes` table /
  `/billing-notes` API group and the **`BL` doc-number prefix** (renaming a posted-document table or a
  monthly number sequence is a compliance/migration risk for ~zero user value — the user never sees
  "BillingNote", only "ใบแจ้งหนี้/Invoice" and `MM-YYYY-BL-NNNN`). Revisit prefix→`IV` only if Ham insists.
- **D6 ⚠️ASSUMED — Invoice↔TI/DO schema:** add `BillingNote.DeliveryOrderId` (nullable source) and
  `TaxInvoice.BillingNoteId` (nullable source). Keep the legacy `billing_note_tax_invoices` join table
  for old data but stop writing to it; the new linear flow uses the single FK. Invoice "Issue" still
  allocates the `BL` number; TI create requires an **Issued** Invoice.
- **D7 ⚠️ASSUMED — full chain:** `DocumentCrossRefService` returns one normalized
  `DocumentChainDto { quotation?, salesOrder?, deliveryOrders[], invoices[], taxInvoices[], receipts[],
  adjustmentNotes[] }` resolved from whichever node you start at (walk both up and down the FK links).
  One FE `<DocumentChain>` renders the ordered list with a status badge + **per-row print buttons**.
- **D8 ⚠️ASSUMED — universal print:** extend `PrintTrackingService` + add `OriginalPrintedAt`/`PrintCount`
  columns to Quotation, SalesOrder, DeliveryOrder, BillingNote (TI/RC/CN/DN already have them). Every
  doc's PDF supports `?copy=true`; reprinting an already-printed original auto-downgrades to สำเนา and the
  FE warns "ต้นฉบับเคยถูกพิมพ์แล้ว". QuestPDF renders all (already true) — copies get the สำเนา watermark.

## 4. Implementation phases (each independently shippable + gated)

**Phase 1 — Flow + 422 (BE, highest priority, unblocks testing).**
- `MarkDeliveredAsync`: remove the auto-TI branch (status only). Keep `CreateTaxInvoiceAsync` but it is
  now called from the Invoice path, not DO.
- Add `BillingNote.DeliveryOrderId`; `IBillingNoteService.CreateFromDeliveryOrderAsync(doId)` copies DO
  lines + customer + source link, status=Draft.
- Add `TaxInvoice.BillingNoteId`; `ITaxInvoiceService.CreateFromBillingNoteAsync(invoiceId)` (VAT-only,
  `EnsureVatRegistered`) copies Invoice lines.
- Receipt: `ReceiptApplicationInput.BillingNoteId` (replace DO usage for non-VAT); GL: Invoice-applied
  non-VAT receipt → Cr Sales 4000 (same cash-basis branch as the cont.68 DO path). Keep DO-apply code
  dormant/back-compat but the FE switches to Invoice.
- Migration `AddInvoiceFlowLinks` (DeliveryOrderId on billing_notes, BillingNoteId on tax_invoices,
  BillingNoteId on receipt_applications). Commit migration WITH code (cont.67 lesson).
- Tests: DO mark-delivered no longer creates a TI; DO→Invoice→TI (VAT) chain; non-VAT DO→Invoice→Receipt
  GL Cr Sales 4000; non-VAT Invoice→TI rejected 422.

**Phase 2 — Rename Billing Note → Invoice (FE).**
- Rename route folder `app/(dashboard)/billing-notes` → `invoices`; update all `href`/links/nav key;
  i18n `nav.billingNotes`→`nav.invoices` + all `bn.*` user strings → "ใบแจ้งหนี้ / Invoice"; PDF
  `PaperDocConfig` label. BE PDF label string → "ใบแจ้งหนี้ / Invoice". No BE entity rename.

**Phase 3 — Full related-docs chain.**
- `DocumentCrossRefService` → unified `GetChainAsync(anchorType, id)` walking the FK graph both ways.
- FE `<DocumentChain>` component replaces the per-page `RelatedDocs`; shows Q→…→RC ordered, current doc
  highlighted, each row linkable + has print original/copy buttons (Phase 4).

**Phase 4 — Universal print original/copy + tracking.**
- Migration: add `OriginalPrintedAt`,`PrintCount` to quotations, sales_orders, delivery_orders,
  billing_notes. Extend `PrintTrackingService.MarkPrintedAsync` + `PrintDocType`. FE `PrintMenu` +
  `<DocumentChain>` rows expose ต้นฉบับ/สำเนา for all; reprint-original → warn + downgrade to copy.

## 5. Compliance guards (unchanged / reinforced)

- TI immutability after post; TI blocked for non-VAT (`EnsureVatRegistered`) — now enforced at the
  Invoice→TI chokepoint too.
- Doc numbering monotonic per prefix; Invoice number on Issue, TI number on Post (unchanged).
- Every state change → `audit.activity_log`; print actions logged (existing).
- `company_id` filter on all new queries/joins.

## 6. Risks / notes for Ham

- D5: BE keeps `BillingNote` internally. If you want the **`BL`→`IV` prefix** changed (user-visible on the
  document number), that needs a numbering-sequence decision (existing `BL` docs keep `BL`; new ones `IV`)
  — say the word.
- Legacy: DOs already combined-with-TI and BillingNotes that bundled TIs remain viewable; the new flow
  applies to newly created docs only.
- Phases 2–4 are large but mechanical; Phase 1 is the compliance-sensitive one and is fully tested.
