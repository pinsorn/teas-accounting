# Fix: purchase-side docs + non-VAT company UX (Ham findings, 2026-07-22)

Source: Ham live observations + screenshot (expense-claim line row). 4 findings — 2 are non-VAT-mode
correctness (one money-adjacent), 2 are display polish. Grounded against source before writing:
- `app/(dashboard)/expense-claims/new/page.tsx` renders the per-row VAT select (0%/7%) and the
  `isRecoverableVat` ("VAT เครดิตได้") checkbox UNCONDITIONALLY — no `vatRegistered` gate anywhere in
  the file (line 22/26/156-158). `lib/queries.ts:983` says §4.6 vatRegistered "drives app-wide VAT
  mode (nav, e-Tax CTAs, …)" — this form was missed.
- Line-row controls mix `select select-bordered select-sm` (VAT + category selects) with default-size
  `input input-bordered` textboxes → visible height mismatch (Ham's screenshot).
- `components/paper/PaperMeta.tsx:33` renders the counterparty address only when non-empty
  (`customer.address && …`) — a purchase paper whose data mapping doesn't pass the vendor's address
  silently drops the line. Ham: "บางเอกสารฝั่งซื้อไม่มีที่อยู่ Vendor".

## F-A (MED, audit-first) — some purchase documents omit the Vendor address
- AUDIT the data mapping of every purchase-side paper/preview/PDF: PO, VI (บันทึกใบกำกับภาษีซื้อ),
  PV (ใบสำคัญจ่าย), expense-claim print if any. For each: does the vendor block pass `address`
  (+ taxId + branch) into PaperHead/PaperMeta? List which docs currently drop it and WHY (mapping
  omits the field vs vendor record has no address).
- Fix: pass the vendor's address wherever the mapping omits it (vendor master has `address` —
  types.ts:719/732). Where the VENDOR RECORD itself has no address, keep the current silent-omit
  (correct behavior — don't print an empty line).
- [x] every purchase paper shows vendor address when the vendor has one; audit table recorded here.

### F-A audit table (2026-07-22)

| Doc | Live preview (FE, before save) | Detail/print (paper) | Address flows? | Root cause | Fix |
|---|---|---|---|---|---|
| PO | `PurchaseOrderForm.tsx:255` passes `vendor?.address` — OK | `/purchase-orders/[id]` → `paperDtoToProps` → BE `PurchaseOrderService.BuildPaperAsync` (`PurchaseOrderService.cs`, was line 304) | **NO** | Two-part bug: (1) `CreateDraftAsync` never snapshotted `po.VendorAddress` from `v.Address` even though the entity column exists; (2) `BuildPaperAsync`'s `PaperCustomer` ctor only passed `Name`+`TaxId`, omitting `Address` entirely | Set `VendorAddress = v.Address` at create + pass `Address: po.VendorAddress` into `PaperCustomer` (`PurchaseOrderService.cs`) |
| VI | `vendor-invoices/new/page.tsx:239` passes `vendor?.address` — OK | `/vendor-invoices/[id]:178` builds `PaperDocument` manually from `d.vendorAddress`, sourced from `VendorInvoiceService.CreateDraftAsync`'s `VendorAddress = vendor.Address` snapshot | YES | — | none needed |
| PV | `payment-vouchers/new/page.tsx:285` passes `vendor?.address` — OK | `/payment-vouchers/[id]` → `paperDtoToProps` → BE `PaymentVoucherService.Read.cs.BuildPaperAsync`, `Customer: new PaperCustomer(d.VendorName, ..., d.VendorBranchCode, d.VendorAddress)`, sourced from `PaymentVoucherService.cs`'s create-time `VendorAddress = vendor.Address` snapshot | YES | — | none needed |
| Expense claim | n/a (reimburses an employee, no vendor/counterparty concept) | **NONE** — `expense-claims/[id]/page.tsx:18` comment confirms "no PaperDocument — a claim has no print layout in scope" | N/A | — | none needed |

Only PO was broken; VI and PV were already correct (PV's `BuildPaperAsync` was the reference
implementation — it already threaded `VendorAddress`/`VendorBranchCode` through). Fixed in
`backend/src/Accounting.Infrastructure/Purchase/PurchaseOrderService.cs` (both the create-time
snapshot and the paper-DTO mapping). Scope kept to `address` only, per this item's literal ask —
`VendorBranchCode` doesn't exist on the `PurchaseOrder` entity at all (a separate, unrequested gap
— not touched here; PO's live preview also doesn't attempt to show a vendor branch code).

## F-B (HIGH for non-VAT correctness, money-adjacent) — expense claim shows "VAT เครดิตได้" +
##     VAT rate select in a NON-VAT-registered company
- Behavior decision (Fable, per Thai tax rules): a non-VAT-registered company CANNOT credit input VAT
  (no ภ.พ.30) — VAT paid is simply part of the expense cost. So in a non-VAT company the per-row VAT
  select and the "VAT เครดิตได้" checkbox are meaningless and misleading.
- Fix (FE): in `expense-claims/new/page.tsx`, when the company profile's `vatRegistered === false`:
  hide the VAT column/select (submit `vatRate: 0`) and hide the `isRecoverableVat` checkbox (submit
  `false`). The ยอด summary then shows only จำนวนเงิน (no ภาษีมูลค่าเพิ่ม line). Use the same
  company-profile hook the §4.6 VAT-mode features already use (grep `vatRegistered` in lib/queries —
  follow the existing pattern, no new plumbing).
- VERIFY (BE, money path — read only unless broken): `ExpenseClaimService` posting for a non-VAT
  company must NOT route any amount to 1170 ภาษีซื้อ (input-VAT asset). If the DTO can arrive with
  vatRate>0 + isRecoverableVat=true for a non-VAT co and the service would post 1170, add the
  server-side guard (force non-recoverable/zero-rate when company !vatRegistered) — that's a money
  fix: Fable reviews that diff personally + a test (non-VAT co claim with vatRate 7 → JE has NO 1170
  line; VAT amount lands in the expense cost).
- CLARITY (VAT co, Ham "อ่านไม่เข้าใจ"): even in a VAT company the bare "VAT เครดิตได้" label is
  cryptic. Change the label/help to explain: "ภาษีซื้อเครดิตได้ (นำไปหักใน ภ.พ.30)" + unchecked hint
  "ภาษีซื้อต้องห้าม — บันทึกเป็นต้นทุน" (ExpenseCategorySelector.tsx:74 already has the ม.82/5 warning
  string — reuse the same wording family; the category selector already auto-flags ENT/VEHI ⚠).
- [x] non-VAT co: no VAT select, no checkbox, totals show no VAT line; server guard verified/added.
- [x] VAT co: clearer label + hint; behavior unchanged.

### F-B evidence (2026-07-22)
- FE: `expense-claims/new/page.tsx` gates on `useSystemInfo().data?.vatMode ?? true` (same §4.6
  hook `vendor-invoices/new` already uses — no new plumbing). VAT select + recoverable checkbox
  wrapped in `{companyVatRegistered && (...)}`; the VAT totals line is likewise hidden (not shown
  as "0.00"). `saveDraft()` additionally force-submits `vatRate: 0` / `isRecoverableVat: false`
  per line when `!companyVatRegistered`, and the `ExpenseCategorySelector` onChange no longer
  defaults a line to recoverable for a non-VAT company — three independent layers (hidden control,
  forced submit, forced default) so no code path can leak a nonzero recoverable flag.
- BE money guard (**was broken — added**, not just verified): `ExpenseClaimService.BuildLinesAsync`
  did NOT check the company's VAT-registration flag at all before this fix — a client (REST or the
  `create_expense_claim_draft`/`update_expense_claim_draft` MCP tools, which route through the same
  service) could submit `vatRate: 0.07, isRecoverableVat: true` for a non-VAT company and
  `GlPostingService.PostExpenseClaimAsync` (`GlPostingService.cs:281-290`) would post the VAT to
  1170 (`InputVatAccount`). Fixed in `ExpenseClaimService.cs`:
  - `BuildLinesAsync` now forces `isRecoverableVat = companyVatRegistered && input.IsRecoverableVat`
    (covers `CreateDraftAsync` + `UpdateDraftAsync`, the single seam every create/update path funnels
    through — mirrors `VendorInvoiceService`'s WP1.2/F27/D1 guard).
  - `PayAsync` re-asserts the same guard immediately before GL posting (defence for a claim
    approved while the company WAS VAT-registered and later flipped non-VAT, or a legacy row
    predating this fix) — mirrors `VendorInvoiceService.PostAsync`'s re-guard.
  - The VAT amount itself is NEVER zeroed server-side (only `IsRecoverableVat`) — the employee
    really paid that VAT, so it folds into the expense cost debit instead of vanishing.
  - Test: `backend/tests/Accounting.Api.Tests/Hardening/ExpenseClaimNonVatCompanyTests.cs` — (1)
    non-VAT co, `vatRate=0.07 + isRecoverableVat=true` on create → JE has NO 1170 line, full
    1070 lands on the expense account; (2) VAT co regression — 1170 still books 70; (3) company
    flips non-VAT AFTER Approve (before Pay) → Pay still forces non-recoverable (tests the
    `PayAsync` re-guard specifically, real Create→Submit→Approve→Pay transition, not a seeded row).
    **Flagged for Fable's personal review** (money-path diff).

## F-C (LOW, polish) — line-row control heights mismatch
- `expense-claims/new` row mixes `select-sm` selects with default-height inputs. Normalize: all
  controls in the row same size (either all `-sm` or all default — match whichever the rest of the
  app's line-item rows use; the doc-create forms use default-height, prefer that). Sweep the SAME row
  pattern in any sibling forms that copied it.
- [x] textbox/select/date controls in the expense row are equal height (code-level; static
  Tailwind/DaisyUI class inspection — no live browser drive per this dispatch's gate list, which
  does not require a UI smoke test for this backend/plumbing-heavy round).

### F-C evidence (2026-07-22)
- `expense-claims/new/page.tsx`: removed `-sm` from description/date/amount/VAT-select/checkbox so
  every control in the row now matches `ExpenseCategorySelector`'s default height (that component
  was already default-height and unchanged across all 3 of its callers — expense-claims, VI, PV).
- Sweep (spec's "sibling forms that copied it"): `vendor-invoices/new/page.tsx`'s line row has the
  identical shape (`ExpenseCategorySelector` default-height next to `-sm` siblings). Fixed the two
  page-local inline controls (description, amount → default height) plus `ProductTypeSelect.tsx`
  (its only consumer is this VI row, so its internal `select-sm` → default was a safe, zero-blast
  direct edit). `PercentRateInput` (used for VI's per-line VAT rate) is ALSO used by
  `payment-vouchers/new` for the WHT-rate field, where its row is internally consistent at `-sm`
  already (no mismatch to fix there) — changing its default would have introduced a NEW mismatch
  in PV. Gave it an additive `size?: 'sm' | 'md'` prop defaulting to `'sm'` (every existing call
  site, including PV, is byte-for-byte unaffected) and passed `size="md"` only from VI's row. PV's
  own line row was not touched (Ham never flagged it; it has no mismatch).

## F-D (DECISION + gate check) — should บันทึกใบกำกับภาษีซื้อ (VI) appear in a non-VAT company?
- Ham: "ถ้าได้ก็ไม่เป็นไรนะ" — but the answer must be deliberate, not accidental:
  1. CHECK what exists: does SidebarNav / VAT-mode gating (§4.6, lib/queries.ts:983) already hide the
     VI menu for a non-VAT company? Is `/vendor-invoices/new` reachable by URL in a non-VAT co
     (post-WP1 the route-guard checks the CREATE perm, not VAT mode)?
  2. CHECK what posting does: for a non-VAT co, does `VendorInvoiceService`/GL posting route VAT to
     1170 (WRONG — non-creditable) or into expense/cost (correct)?
  3. DECIDE by result: if posting is VAT-mode-aware (VAT→cost for non-VAT co), VI in non-VAT co is
     legitimate (a non-VAT business still receives tax invoices; recording them is fine) → leave
     visible, just ensure the ภาษีซื้อ (เครดิตได้) wording doesn't appear in non-VAT mode. If posting
     would hit 1170 for a non-VAT co → hide VI in non-VAT mode (nav + VAT-mode page gate) OR make the
     posting VAT-mode-aware — pick the SMALLER correct diff; a 1170 fix is money-path → Fable review.
- [x] documented answer (current behavior) + fix applied per the decision rule; test if posting changed.

### F-D findings + verdict (2026-07-22)
1. **Nav gating**: `SidebarNav.tsx`'s `vendorInvoices` item has NO `vatOnly: true` flag (unlike
   `taxInvoices`/`creditNotes`/`debitNotes`/`pnd30`, which do) — the VI menu item is NOT hidden for
   a non-VAT company today.
2. **URL reachability**: `vendor-invoices/new/page.tsx`'s route guard checks only
   `purchase.vendor_invoice.create` (`canCreate`) — no VAT-mode check. `/vendor-invoices/new` is
   reachable by URL in a non-VAT company, confirmed by code read (post-WP1 route-guard pattern).
3. **Posting behavior**: already fully VAT-mode-aware, and has been since WP1.2/F27/D1 (commit
   `65b9b2b`, 2026-07-14) — `VendorInvoiceService.CreateDraftAsync` (line ~149), `UpdateDraftAsync`
   (line ~311), and `PostAsync` (line ~356) all force `HasInputVat = false` and every line's
   `IsRecoverableVat = false` when `!company.VatRegistered`, regardless of vendor VAT status or an
   explicit client `HasInputVat: true`. `GlPostingService.PostVendorInvoiceAsync` reads
   `recoverable = vi.HasInputVat && l.IsRecoverableVat` — always false for a non-VAT company, so
   VAT always folds into the expense debit, never 1170. Existing regression coverage:
   `VendorInvoiceNonVatCompanyTests.cs` (3 tests, all still green).
4. **Verdict**: per the decision rule, posting IS VAT-mode-aware → **VI stays visible in a
   non-VAT company** (a non-VAT business legitimately still receives vendor tax invoices; recording
   them is correct). No nav/route change. The only remaining action was wording: the totals box's
   recoverable-VAT row unconditionally showed the label `t('vat')` = "ภาษีซื้อ (เครดิตได้)" / "Input
   VAT (recoverable)" even though its value is structurally always 0 for a non-VAT company (forced
   by the guard above) — misleading per the decision rule's "ensure the wording doesn't appear in
   non-VAT mode" clause. Fixed by dropping that one `totalRows` entry when `!companyVatRegistered`
   in `vendor-invoices/new/page.tsx` (the `nonRecVat` "ภาษีซื้อต้องห้าม" row, which correctly
   reflects where the VAT actually lands for that company, stays). No backend/posting change — the
   posting formula was already correct; this was a display-only fix. No new test needed (no
   money-path/posting behavior changed for F-D beyond what WP1.2 already covers).

## Gates
- `pnpm tsc --noEmit` + `pnpm next build` clean; i18n th/en parity + ম-glyph scan clean.
- If ANY backend/money change (F-B guard, F-D posting): dotnet build + full test, skip == baseline 8;
  new tests for the guard; Fable personally reviews the money diff (never skipped).
- Verify on BOTH companies: co5 (VAT) unchanged behavior; Repttown/non-VAT co shows the trimmed form.
- Blast radius: expense-claims form + paper mappings + (conditional) ExpenseClaim/VI service guard +
  i18n keys. ≤10 files. NO posting-formula changes beyond the specified guards.

## Attempt log
- 2026-07-22 ~01:2x spec drafted (Fable) from Ham's report + source grounding (see header).
