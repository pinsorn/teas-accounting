# fix-c2-fe-cleanup — Cleanup unit C2 (FE-only, 3 small items)

Blast cap: 6 files. Gate: `npx tsc --noEmit` from `frontend/`. No `dotnet test` (another
worker owns the test DB). No commits.

## Checklist

- [x] **1. FA modals a11y** — `frontend/app/(dashboard)/fixed-assets/[id]/page.tsx`:
      Dispose modal (~L217) and WriteOff modal (~L256) lack `role="dialog"`/`aria-modal`.
      Fix: copy the idiom from commit 46e3a46 (bank-rec modals) — add
      `role="dialog" aria-modal="true"` to each `<div className="modal modal-open">`.
- [x] **2. PO form hardcoded taxCodeId** — `frontend/components/forms/PurchaseOrderForm.tsx:193`
      hardcodes `taxCodeId: 1`. Backend worker fixes server side separately (deferred item
      per `specs/fix-r2-u2-billing-tax-integrity.md` §8: `PurchaseOrderService.cs:90` is
      "the ONLY verbatim-id writer left" — writes `TaxCodeId`/`TaxCode` from the request
      with NO resolver, unlike the sales side's `SalesLineBackstop.Resolve`). FE half:
      stop lying about a real id; match Quotation/SalesOrder's null-when-untouched contract.
      - Confirmed via code read: `PurchaseOrderLineInput.TaxCodeId`/`TaxCode` are already
        `int?`/`string?` (`PurchaseOrderDtos.cs:13-14`), and the entity fields are nullable
        too (`PurchaseOrder.cs:124-125`) — sending null is DB/entity-safe.
      - Confirmed via code read: `LineItemsTable.tsx:104-106` — the real tax-code picker is
        `purpose === 'sale'`-only; purchase keeps the old rate-only dropdown and NEVER sets
        `l.taxCode`/`l.taxCodeId` (stays the `EMPTY_LINE` null). So this always sends null
        today — same as Quotation/SalesOrder's "untouched line" case, never regresses an
        existing "real id picked" case (none exists for purchase).
      - Confirmed via code read: `PoLineDto` (FE) / `PurchaseOrderLineDto` (BE, read side)
        carry no taxCode/taxCodeId at all, so edit-mode `toLine()` has nothing to
        reconstruct — stays null on reload too, consistent.
      - Fix: add `taxCode`/`taxCodeId` to `lineSchema` (mirrors QuotationForm.tsx:37-38 —
        keeps zod from stripping the fields before submit, same Tier-2 bug class already
        fixed there 2026-08-16) and change the submit payload's
        `taxCodeId: 1, taxCode: vendorVat && l.taxRate > 0 ? 'VAT7' : 'VAT0'` to
        `taxCodeId: l.taxCodeId ?? null, taxCode: l.taxCode ?? null` (byte-identical
        pattern to Quotation/SalesOrder).
      - **DeliveryOrderForm.tsx:115** also hardcodes `taxCodeId: 1` (U2 designer: harmless,
        server re-resolves). Confirmed via code read: `SalesLineBackstop.Resolve` (used by
        the DO/sales chain, unlike PO) doesn't even take a `taxCodeId` parameter — the
        caller's id is PROVABLY never read, so 1→null is a strict no-op. `DoLine` has no
        taxCode/taxCodeId per-line state (non-fiscal bespoke line array, no picker) so
        there's no `l.taxCodeId ?? null` to substitute — just the literal constant. Applied
        the same one-line fix for consistency: `taxCodeId: 1` → `taxCodeId: null`.
- [x] **3. Back-dated claim surfacing** — expense-claim detail page pay flow. r2 L4-7
      (`PROGRESS-hard-test-r2.md`): PAY posts the JE on the PAYMENT date, not the claim
      date (defensible cash-basis-at-payment) but nothing surfaces the divergence to the
      user. Ham greenlit an informational (never-blocking) note.
      Fix: `frontend/app/(dashboard)/expense-claims/[id]/page.tsx`, in the pay modal —
      when `d.claimDate`'s month differs from the current Bangkok month, show an info
      callout: "การบันทึกบัญชีจะลงงวดปัจจุบันตามวันที่จ่าย ไม่ใช่เดือนของใบเคลม". Reused the
      `text-info` idiom from `payment-vouchers/new/page.tsx` (plain informational text, not
      the warning-red boxed style used for the FA dispose VAT notice — this is genuinely
      informational, not a warning). i18n key `expenseClaims.backDatedPayNote` in both
      `en.json`/`th.json`.

## Evidence

`npx tsc --noEmit` from `frontend/` — clean, no output, exit code 0.

Files touched (6, at blast cap):
- `frontend/app/(dashboard)/fixed-assets/[id]/page.tsx`
- `frontend/app/(dashboard)/expense-claims/[id]/page.tsx`
- `frontend/components/forms/PurchaseOrderForm.tsx`
- `frontend/components/forms/DeliveryOrderForm.tsx`
- `frontend/messages/en.json`
- `frontend/messages/th.json`

Thai glyph check (troubles-wiki.md pitfall — Bengali ম lookalike): PowerShell codepoint
scan of the new `backDatedPayNote` line in `th.json` — clean, no U+0980–U+09FF codepoints.
