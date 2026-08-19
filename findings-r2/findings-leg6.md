# Testing Swarm Round 2 — Leg 6 findings (round-1 leftovers, sales module)

Environment: FE http://localhost:3000, API http://localhost:5080. DB accounting_dev (read-only verify).

## Status
- [x] Item 1: Company 4 end-to-end sale (non-VAT) — BLOCKED by L6-1 (🔴), cannot be
      completed as specified; TI-refusal half verified and passes.
- [x] Item 2: N1 live re-verify (exempt product → 0% VAT, DB pair, edit-resave) — PASS
      (L6-2), both doors verified.
- [x] Item 3: N2 live re-verify (double quotation→TI conversion refused) — guard PASSES,
      UX gap found (L6-3, 🟡).

## Findings

### L6-1 🔴 Billing Note creation is broken for ANY line with an unresolved tax code — total blocker for non-VAT companies' only revenue document
**Repro (confirmed via real browser UI AND isolated via direct API, both against company 4 and company 3 and company 1):**
1. Log in as `admin`, switch to company 4 (non-VAT). Go to `/invoices/new` (Billing Note — the terminal revenue doc for non-VAT companies per `ม.86/4`; code comment in `receipts/new/page.tsx` confirms canonical non-VAT flow is `DO → Invoice → Receipt`).
2. Pick a customer, fill description/qty/price on line 1 (the only fields visible — the VAT-rate column is entirely hidden for a non-VAT company, `showVat = vatMode && vatEnabled` in `frontend/components/ui/LineItemsTable.tsx:103`).
3. Click "ออกใบแจ้งหนี้" (Issue). POST `/api/proxy/billing-notes` → **400 Bad Request**, `{"title":"validation_error","detail":"A required or malformed query/route parameter was rejected."}`. No document is created (`sales.billing_notes` has 0 rows for company 4 after the attempt). Screenshot `l6-1-company4-billing-note-400.png` shows the stuck create form with a generic toast **"เกิดข้อผิดพลาด"** ("An error occurred") — same `problemToast`-bypass UX gap documented in full under L6-3 (`BillingNoteForm.tsx`'s catch uses the identical broken `e.detail` read).

**Root cause (pinpointed to file/line, confirmed by curl bisection):**
- `frontend/components/ui/LineItemsTable.tsx` (`EMPTY_LINE`, ~line 39-45) defaults every line's `taxCode`/`taxCodeId` to `null`. They are only set when the user explicitly interacts with the tax-rate `<select>` (`onChange` at line ~250) — for a non-VAT company that `<select>` never renders at all, so `taxCodeId` can **never** be non-null on any line.
- `backend/src/Accounting.Application/Sales/BillingNoteDtos.cs:16` — `BillingLineInput.TaxCodeId` is typed **`int` (non-nullable)**, unlike the sibling DTOs used by Quotation/SalesOrder (`Sales/SalesChainDtos.cs:17,64` → `int? TaxCodeId`) and Tax Invoice (`Sales/TaxInvoiceDtos.cs:19` → `int? TaxCodeId`), which are correctly nullable.
- When the JSON body carries `"taxCodeId": null` for a non-nullable `int` record parameter, System.Text.Json throws during minimal-API model binding; ASP.NET Core wraps this as `BadHttpRequestException`, which `DomainExceptionMiddleware.cs:66-70/121-134` maps to the generic, unhelpful 400 shown above (masks the real cause by design, per its own F4 comment — safe but here it also hides an availability bug from the user entirely).
- **Bisected empirically** (direct curl against `:5080/billing-notes`, company-4-scoped JWT): `taxCodeId: 999, taxCode: null` → **201 Created**. `taxCodeId: null, taxCode: "X"` → **400**. Confirms `TaxCodeId`'s non-nullable `int` is the sole trigger; `TaxCode`'s non-nullable `string` does not throw (System.Text.Json doesn't enforce C# nullable-reference annotations at runtime).
- **Blast radius confirmed wider than company 4**: reproduced identically for company 3 (existing non-VAT tenant) and even company 1 (VAT-registered) when a line's tax code is left at its default (i.e. any VAT-registered-company user who accepts the visually-pre-filled 7% dropdown without explicitly re-clicking it also hits this — the *display* defaults via `l.taxCode ?? standardCode` but the underlying line *state* stays `null` until `onChange` fires). For a non-VAT company this is not a possible-oversight — it is **always** hit, 100% of the time, since the picker never renders.
- Corroborating evidence this is likely a **recent regression**: company 3's one existing billing note (`billing_note_id=3`, `SETTLED`) has `tax_code_id=1` ("VAT0") stored on both lines — i.e. it was created successfully at some point, before the "real tax-code picker" was wired into `LineItemsTable` (memory: "Tax Code Picker Infrastructure Initialized in LineItemsTable Component", 2026-08-16). No new billing note has been created since for either non-VAT tenant.
- **Company 4 also has no generic non-exempt/non-zero-rated "domestic non-VAT sale" tax code** in its seeded set (12 rows: VAT7/VAT-IN7/VAT-OUT-0-EXP/VAT-OUT-0-SVC-ABR + 8 EXEMPT-*), unlike company 3 which has a dedicated `VAT0` code — a secondary, lower-severity gap: even a caller who bypasses the FE and supplies an explicit `taxCodeId` has no obviously-correct company-4 code to use for an ordinary non-VAT domestic sale.
- No alternative UI path exists to complete a company-4 sale end-to-end: quotation→TI conversion button (`q-create-ti`) is hidden by `vatMode` gating (`quotations/[id]/page.tsx:199`); there is no quotation→billing-note server-side conversion action (the BillingNoteForm's `quotationId` field is hardcoded to `null` on create, dead client-side); Receipts (`/receipts/new`) settle an *existing* Invoice/TI (`CreateReceiptRequest.Applications` is required) and cannot originate a new unbilled sale.

**Severity: 🔴 (money/tax path).** This is an availability blocker, not data corruption — nothing posts, nothing corrupts. But it means **no non-VAT company can create any billing note (their only revenue document) through the UI today**, and any VAT company's manual line-entry billing note is equally fragile whenever the tax-rate dropdown isn't explicitly touched. `frontend/e2e/non-vat-mode-pdf.spec.ts` and `frontend/e2e/billing-note-flow.spec.ts` (both permanent suite members) exercise this exact manual-line-entry-without-touching-the-select path — stated for the record, **not run** per task instructions, but presumed currently red against this backend.

**Consequence for Task 1:** item 1 ("post ONE sale end-to-end through the UI for company 4") **cannot be completed as specified** — this bug blocks it at the first and only available step. Completed instead: (a) customer creation for company 4 via UI (succeeded, `customer_id=10`, code `E2EC4...`), (b) the TI-refusal half of the task — **passes**: with company 4 active, the sidebar has no "ใบกำกับภาษี" link and no "ภ.พ.30" link, and navigating directly to `/tax-invoices/new` (URL guard, bypassing the hidden nav) renders `NonVatGuard`'s empty state with the exact ม.86/4 message ("ฟีเจอร์นี้ใช้ได้เฉพาะกิจการที่จดทะเบียน VAT..."), never the create form. Screenshot: `l6-1-company4-ti-refused.png`.

---

### L6-2 (PASS) N1 live re-verify — exempt product locks 0% VAT through both doors
**Repro:** company 1 (VAT), admin. Created product `E2EEXM174100` (type `EXEMPT_GOOD`) via
`/settings/products`. Created a quotation (`/quotations/new`), picked the product into
line 1 via `ProductPicker` (qty 1, price 100). Screen locked-rate badge read exactly
`0%` (screenshot `l6-2-n1-create-0pct.png`); totals panel showed `ภาษีมูลค่าเพิ่ม ฿0.00`.
Saved as Draft → quotation_id=3.

**Door 1 (create), captured via `GET /api/proxy/quotations/3` immediately after save,
before any edit:**
```json
{"productId":14,"descriptionTh":"สินค้ายกเว้นภาษี e2e 4100","taxCode":"EXEMPT-AGRI","taxCodeId":5,"taxAmount":0}
```

**Door 2 (edit + re-save):** opened `/quotations/3/edit` — product type and 0% VAT
correctly re-hydrated on load (screenshot `l6-2-n1-edit-0pct.png`, totals still ฿0.00
VAT), clicked บันทึก with no changes. DB after re-save:
```
quotation_id | line_no | product_id | product_type | tax_code_id | tax_code    | tax_rate | tax_amount | is_exempt
3            | 1       | 14         | EXEMPT_GOOD  | 5           | EXEMPT-AGRI | 0.000000 | 0.0000     | t
```
`sales.quotations.status = DRAFT`, `doc_no` still unassigned (draft, as expected).

**Verdict:** N1 (commit 047fe95) holds up live through the real UI on both the create
path and the edit-resave path (the Aug-16 line-hydration fix 24657 that added
`productType`/`taxCode`/`taxCodeId` round-tripping to `QuotationForm.toLine` is what
makes door 2 survive — before that fix this exact scenario would have regressed to
`productType: 'GOOD'` on save). No finding here — passes as designed.

---

### L6-3 🟡 N2 backend contract is correct, but the quotation page swallows the specific error message
**Repro:** company 1, admin. Created quotation → Issued (`08-2026-QT-0003`, quotation_id=5) →
Accepted (confirm dialog) → `q-create-ti` → draft TI created → Posted it (`08-2026-TI-0003`,
tax_invoice_id=7). Navigated back to the same quotation, clicked `q-create-ti` again.

**Expected (per task):** refused with a typed `quotation.already_invoiced` error, visible
sanely in the UI. **Actual — two halves:**
- ✅ **Correctly refused, no double-invoice, no crash.** DB: exactly one row in
  `sales.tax_invoices` with `quotation_id=5` (`tax_invoice_id=7, status=POSTED`). No
  navigation away from the quotation detail page; no raw 500/blank page.
- 🟡 **But the specific reason is lost.** A direct repeat API call
  (`POST /quotations/5/create-tax-invoice`) proves the backend returns the correct, typed,
  informative 409:
  ```json
  {"title":"quotation.already_invoiced","status":409,
   "detail":"Quotation 5 has already been invoiced by Tax Invoice 08-2026-TI-0003."}
  ```
  But the real browser UI's second-attempt toast (captured via `toast.innerText()`,
  screenshot `l6-3-n2-second-attempt-toast.png`) reads only **"เกิดข้อผิดพลาด"** — the
  generic fallback. The user gets zero indication of WHY the action failed or which TI
  already covers this quotation (even though that info is right there in the cross-ref
  panel on the same page).

**Root cause:** `quotations/[id]/page.tsx`'s `createTaxInvoice()` (and its sibling `run()`
used by send/accept/reject/cancel) catch with the old, broken ad-hoc pattern:
```ts
catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
```
`ApiError` (thrown by `frontend/lib/api.ts`) exposes the ProblemDetails `detail` as
`.message`, **not** `.detail` — `lib/api.ts:31-33` documents this exact trap in its own
comment: *"ApiError sets `.message` to the body's `detail`... `.detail`... doesn't exist on
ApiError — the old `e.detail` reads always fell through to the generic fallback."* The
correct, shared helper `problemToast(err, fallback)` (same file, exported) already exists
and is used elsewhere in the codebase (Thai i18n mapping by code + sticky toast +
secondary description line) — the quotation detail page was simply never migrated to it.
This is the exact same ad-hoc-catch pattern also seen live in L6-1's billing-note 400
(`BillingNoteForm.tsx` catch blocks use the identical broken `e.detail` read).

**Severity: 🟡 (contract/UX).** Not a money/security bug — the guard works, data integrity
is fine — but it defeats the purpose of a typed, informative domain error: the user sees
an unhelpful generic message for what backend already computed as a specific, actionable
one.

**Fix scope (for triage, not implemented — findings-only worker):** swap the ad-hoc
`(e as {detail?:string})?.detail ?? tc('error')` catches in `quotations/[id]/page.tsx`
(`run()` and `createTaxInvoice()`) for `problemToast(e, tc('error'))`. Confirmed via
`grep -rln "as { detail?: string }" frontend/app frontend/components`: **22 files** carry
this exact pattern, including `BillingNoteForm.tsx` (L6-1's silent 400),
`SalesOrderForm.tsx`, `DeliveryOrderForm.tsx`, `CustomerForm.tsx`,
`tax-invoices/[id]/page.tsx`, `receipts/[id]/page.tsx`, `sales-orders/[id]/page.tsx`,
`invoices/[id]/page.tsx`, `delivery-orders/[id]/page.tsx`, `customers/[id]/page.tsx`,
`bank-accounts/[id,new]/page.tsx`, `settings/{api-keys,company,products,wht-types}/page.tsx`,
`components/AdjustmentNoteScreens.tsx`, `components/doc/ReceiptWhtCertSection.tsx`,
`components/forms/ProductQuickCreateModal.tsx`, `app/oauth/consent/page.tsx` — this is a
**repo-wide "not-yet-migrated-to-`problemToast`" pattern**, not a single-file fix.

---

