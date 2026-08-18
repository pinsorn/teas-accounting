# Fix N1 / N2 / N3 — ChatGPT review findings, verified 2026-08-18

Source: `_review/codebase-review-2026-08-17.md`. All three verified REAL in source by Fable before
this spec existed. E1–E5 of that review were already fixed (`65a5419` `2b82dde` `25a9b8a`) — do not
touch them again.

## Status
- [ ] DESIGN (Opus) — fill §Design below; Fable reviews before implementation
- [ ] IMPLEMENT (Sonnet, from approved design)
- [ ] REVIEW (Opus, same dispatch as implement)
- [ ] Full suite + commit (Fable)

## N1 — exempt product charges standard VAT (🔴 money/tax, ม.81)
**Verified mechanics:** `LineItemsTable.tsx` onSelectProduct sets `taxRate: taxRateForProductType()`
(0 for EXEMPT_*) and clears `taxCode/taxCodeId` to null. Backend
(`TaxInvoiceService.RebuildLinesAndTotalsAsync`, `deriveLineTax:true`) runs
`SalesLineBackstop.Resolve`, which IGNORES caller rate, keys rate ONLY on tax-code flags, and a
null code falls to ladder step 3 → standard output code @ companyVatRate. ProductType is resolved
(`LoadProductTypesAsync`, master-authoritative) but **never consulted for the rate**. Result:
screen 0%, stored 7%, on a legally-exempt line.

**Design constraints (Fable, binding):**
- The fix lives in the BACKEND resolution ladder. The UI may improve display, but correctness must
  not depend on what the client sends — that is the entire lesson of Unit A. A request with
  `productId` of an EXEMPT_* product and no tax code must come out exempt, whatever the client sent.
- `Product.DefaultOutputTaxCodeId` already exists (Product.cs:35). Design must settle:
  1. Where it enters the ladder (a new step between 2 and 3? does an explicitly-supplied code
     still win when present-and-found?).
  2. Fallback when an EXEMPT_* product has NO DefaultOutputTaxCodeId: which exempt code, or rate 0
     with what stored pair? (The company master seeds 8 exempt categories; picking one arbitrarily
     may be wrong — rate 0 + a deterministic, documented pair choice is required. NEVER a rate>0 on
     an EXEMPT_* line.)
  3. Whether GOOD/SERVICE products with a DefaultOutputTaxCodeId (e.g. zero-rated export good)
     should also resolve through it — probably yes, same mechanism, but state the rate invariant.
- **Money invariant (must appear in the design, stated as invariant not field values):**
  - An EXEMPT_GOOD/EXEMPT_SERVICE line NEVER stores TaxRate > 0 and never contributes to TaxAmount.
  - A GOOD/SERVICE line with no product default and no caller code keeps today's exact behavior
    (standard output code @ companyVatRate) byte-for-byte.
  - Stored `(tax_code, tax_code_id)` is ALWAYS a matched pair from the caller's own master (or the
    documented sentinel) — F13 must not regress.
  - Ledger: for any pre-existing green test, Dr=Cr and totals unchanged.
- Chain-copy paths (`deriveLineTax:false`) inherit source rates — OUT of scope, do not touch.
- FE: after the backend is authoritative, decide the minimal FE change so the screen shows the
  TRUE resolved code/rate (current behavior hides the picker and guesses by type — acceptable only
  if what it shows always matches what the server will store).
- Existing posted rows that already charged 7% on exempt products: posted tax documents are
  immutable. Design documents the detection query (for Ham to assess exposure) but repairs NOTHING.

## N2 — unlimited Tax Invoices from one Quotation (🔴 compliance)
**Verified mechanics:** `CreateFromQuotationAsync` checks only `Status == Accepted`; no
existing-link guard; `HasIndex(t => t.QuotationId).HasFilter("quotation_id IS NOT NULL")` is NOT
unique. Sibling `CreateFromSalesOrderAsync` HAS a guard (`"already has an Invoice"`) — one-to-one
is the established repo pattern, and Ham approved following it.

**Design constraints (Fable, binding):**
- Service guard mirroring the SO sibling: typed error (`quotation.already_invoiced` naming
  convention — designer confirms exact sibling code shape) when a TaxInvoice already references the
  quotation. Decide explicitly how CANCELLED/VOIDED TIs count (there is no TI delete route; can a
  TI be cancelled at all? If a dead TI blocks re-invoicing forever that is its own trap — check what
  states exist and what the SO sibling counts).
- Race closure: filtered UNIQUE index on `tax_invoices.quotation_id WHERE quotation_id IS NOT NULL`
  via EF migration (normal add-migration; the squash history is in `sys.__ef_migrations`, custom
  table). Unique violation maps to 409 per the existing `StatusFor` conventions (`.locked_mismatch`
  family) or a designed equivalent — designer picks the mechanism, but a raw 500 on the race is a
  REJECT.
- **Pre-migration data check:** production/test DBs may ALREADY hold duplicates (the review found
  none reported, but nobody looked). The design must include the detection query and state what the
  migration does if duplicates exist (fail loudly vs skip index — decide, justify; silently
  dropping data is forbidden).
- SO→TI sibling: verify whether IT has the race-closing unique index too (`sales_order_id`); if
  not, the design says so and Fable decides whether to widen scope — do not widen unilaterally.

## N3 — case-sensitive tax-code lookup defeats the ignore-case contract (🟠 tax, API path)
**Verified mechanics:** `LoadTaxCodeFlagsAsync` builds the request-code list with
`OrdinalIgnoreCase` and the dictionary ignore-case, but `codes.Contains(c.Code)` translates to
case-SENSITIVE SQL. `exempt-book` → no row → dictionary empty → ladder step 3 → 7% silently.

**Design constraints (Fable, binding):**
- Ponytail candidate the designer must evaluate FIRST: `tax.tax_codes` is tiny and tenant-filtered —
  loading ALL active codes for the company (dropping the `Contains` filter entirely) makes the
  existing ignore-case dictionary do the work with zero translation risk. If rejected, say why and
  what replaces it (UPPER() translation needs no index at this cardinality but must be proven
  EF-translatable).
- The stored casing must remain the MASTER row's casing (trap §9.2) — that behavior is already
  correct in the dictionary and must survive.
- Test: mixed-case exempt AND zero-rated codes through a request path, asserting rate 0 and the
  master-cased stored pair.

## Shared gates (implementer)
- Full suite baseline 1255/0/14 (Api) + 188 (Domain); tsc 0 for any FE change.
- New tests RED-then-GREEN where feasible.
- `TEAS_TEST_PG` same-shell; skip-count jump = fake green.
- Blast cap: 10 files (design may propose fewer; exceeding = stop and re-spec).

## Attempt log
(append here)
