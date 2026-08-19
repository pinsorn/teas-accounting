# Fix N1 / N2 / N3 — ChatGPT review findings, verified 2026-08-18

Source: `_review/codebase-review-2026-08-17.md`. All three verified REAL in source by Fable before
this spec existed. E1–E5 of that review were already fixed (`65a5419` `2b82dde` `25a9b8a`) — do not
touch them again.

## Status
- [x] DESIGN (Opus, 2026-08-18) — §Design-N1 / §Design-N2 / §Design-N3 / §Test plan /
      §Implementation order appended below. **Fable must read §Conflicts / Deviations first (3 items need
      ratification) and the §N2.5 pre-check must return zero rows before any migration is added.**
- [x] IMPLEMENT (Sonnet, from approved design) — 2026-08-18. All 5 implementation-order steps
      done; §N2.5 pre-check zero rows; build 0/0; tsc clean; 45/45 filtered tests green; RED
      confirmed for the fix-dependent N1/N3/N2 tests via targeted git-stash. See attempt log.
- [x] REVIEW (Opus, same dispatch as implement) — closed by triage 2026-08-19 (047fe95)
- [x] Full suite + commit (Fable) — closed by triage 2026-08-19 (2f8dad8; suite 1255/0/14)

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
- Blast cap: **15 files** — raised from 10 by the Opus design pass (2026-08-18); 3 of the 15 are
  EF-generated migration artifacts (migration + Designer + ModelSnapshot) that no human edits.
  Full enumerated list in §Implementation order. Exceeding 15 = stop and re-spec.

## Attempt log

**2026-08-18 — Opus Tier-2 review: APPROVE-WITH-NITS.** All five lenses PASS; every design trap
honoured; M1–M6 verified against control flow. Nits, Fable's disposition:
- NIT-1 (Rule D ponytail marker sits in the Resolve XML doc comment, not beside the step-3 body) —
  ACCEPTED as residue; greppable and behaviourally enforced by
  `Taxable_product_is_unaffected_by_the_exempt_ladder`. Fix the placement in the next dispatch that
  touches SalesLineBackstop.cs; do not open a dispatch for it alone.
- NIT-2 (ToScreamingSnake whitespace realignment, diff noise) — ACCEPTED; not worth a dispatch.
- NIT-3 (stdRate not in the PV VI-prefill effect deps; wrong base only in the narrow
  system-info-cold + vatRate≠7% window; strictly better than the old hardcoded 0.07) — ACCEPTED as
  documented residue.
- Reviewer handoff: §N2.5's per-target-DB duplicate pre-check + post-restart unique-index probe
  MUST go into the deploy runbook (migration runs at API startup; duplicates = prod crash-loop).
  Step-5 ladder ("EXEMPT" synthetic) and Quotation-path VAT-company clamp have no direct test —
  accepted gaps, logged here.

**2026-08-18 — Opus design pass.** §Design-N1 / §Design-N2 / §Design-N3 / §Test plan /
§Implementation order / §Conflicts / Deviations appended below. No source file touched, no test run.
Blast cap raised 10 → 15 in the header (enumerated list in §Implementation order).
Three deviations from the literal binding constraints are listed in §Conflicts / Deviations and need Fable's
ratification before dispatch.

**2026-08-18 — Fable ratification.** D1, D2, D3 all RATIFIED as designed (posted-only index filter,
posted-only chokepoint guard at G1/G2/G3, `.already_invoiced` → 409). Rule D deferral RATIFIED —
a rate-controlling read of a field whose only UI write is null is a data-loss trap; the deferral
marker comment is mandatory. Blast cap 15 RATIFIED. Implementation dispatched to Sonnet from this
design; Opus reviews the diff before commit.

**Fourth-defect notes — surfaced while reading, NOT designed for (do not fix in this unit):**

- **F-N4.1 — the product settings screen destroys `Product.DefaultOutputTaxCodeId`.**
  `frontend/app/(dashboard)/settings/products/page.tsx:199` (create) and `:211` (update) both send
  `defaultOutputTaxCodeId: null` unconditionally, and `openEdit` (`:171-186`) never reads the field
  into form state. Any value set through `POST /products` or the MCP `create_product` tool is wiped
  the next time a human edits that product. There is no UI anywhere that can set it. This is the
  evidence behind the Rule D deferral (§N1.2) and it is why N1's exempt clamp cannot rely on the
  field being populated in practice.
- **F-N4.2 — `quotationId` on a tax-invoice request is never validated.**
  `CreateDraftCoreAsync` (`TaxInvoiceService.cs:353`) and `UpdateDraftAsync` (`:488`) assign
  `req.QuotationId` straight onto the entity with no existence check and no tenant check. The FK
  (`TaxInvoiceConfiguration.cs:75-76`) accepts any real `quotation_id`, including another company's,
  because `quotation_id` alone is the FK target. A caller can therefore link a TI to a foreign
  tenant's quotation. The N2 guard reads `_db.TaxInvoices` through the tenant filter, so it does not
  close this; a `quotation.not_found`-style existence check would, and that is its own small unit.

**Not logged as a fourth defect** — `taxRateForProductType`'s hardcoded `0.07`
(`ProductPicker.tsx:27-29`) is fixed in-scope by §N1.4.

**2026-08-18 — Sonnet implementation.** All 5 §Implementation-order steps done exactly as
designed; no deviation from the design's mechanics. 15 files touched (at the revised cap, not
exceeding it) — the 15th is a forced consequence of the `taxRateForProductType` signature change
mandated by §N1.4 (see "Forced deviation" below), not a scope expansion.

- Step 1 — `SalesLineBackstop.cs` rewritten per §N1.1/§N1.3: `ProductTaxDefaults` record struct,
  `TaxCodeMaster` class (`ByCode`/`ActiveOutputById`/`ExemptOutputFallback`), `LoadProductDefaultsAsync`,
  `LoadTaxCodeMasterAsync` (no `Where`, `OrderBy(TaxCodeId)` before `GroupBy`), `Resolve` ladder
  steps 2b/2c/3/4/5 added, `ExemptOutputVatCode = "EXEMPT"` const, the `// ponytail: Rule D
  (deferred…)` marker inserted with NO branch written. Class/`Resolve` doc comments rewritten to
  §N1.1 verbatim.
- Step 2 — call sites: `QuotationChainServices.cs` (2), `SalesOrderDeliveryServices.cs` (4),
  `BillingNoteService.cs` (1) renamed mechanically. `TaxInvoiceService.cs`: (a) N1 — inline
  `_db.Products` block replaced by one `LoadProductDefaultsAsync` call reused for both the
  ProductType override and the `Resolve` argument; `EmptyProductTypes` deleted; tax-code loader
  renamed. (b) N2 — `EnsureQuotationNotInvoicedAsync` added; called at G1 (`CreateDraftCoreAsync`,
  right after `EnsureVatRegisteredAsync`), G2 (`UpdateDraftAsync`, right before
  `ti.QuotationId = req.QuotationId`), G3 (`PostCoreAsync`, right after the `ti` row loads);
  `IsQuotationInvoiceUniqueViolation` + its catch clause added FIRST in `PostAsync`'s wrapper.
- Step 3 — frontend: `ProductPicker.tsx` `taxRateForProductType(t, stdRate)`;
  `LineItemsTable.tsx:165` passes `stdRate`.
- Step 4 — §N2.5 pre-check run FIRST (see below, zero rows both queries) — then
  `TaxInvoiceConfiguration.cs:77` → `.IsUnique()` + status-filtered predicate with the §N2.4
  comment; `DomainExceptionMiddleware.cs` `.already_invoiced` → 409 clause added;
  `dotnet ef migrations add QuotationSingleInvoice` run from the real repo path
  (`Y:\ClaudePlayground\TEAS-Project\backend`) — generated migration matched §N2.4's shape
  EXACTLY, zero hand-edits needed; Designer.cs/ModelSnapshot.cs generated, unedited.
- Step 5 — new test files: `ExemptProductTaxResolutionTests.cs` (13 tests, T-N1 + T-N3),
  `QuotationSingleInvoiceTests.cs` (8 tests, T-N2 incl. the API-level 409 test).

**§N2.5 pre-check (BYPASSRLS role `accounting`, `rolbypassrls=t` confirmed):**
count-probe `SELECT count(*) FROM sales.tax_invoices` → 2418 rows (session not RLS-filtered).
Duplicate-POSTED-TI query → 0 rows. Wider informational query (any status, >1 TI/quotation) →
0 rows. Proceeded to migration per design.

**Forced deviation — not in the spec's 14-file list, required to keep `tsc` green.**
`frontend/app/(dashboard)/payment-vouchers/new/page.tsx` calls `taxRateForProductType(productType)`
directly (twice — VI-prefill effect + `lineVat`), independent of `LineItemsTable`. §N1.4 changes
the function's signature to require `stdRate: number` with no default, which is a compile-breaking
change for every caller, not just the two files the design enumerated. Fixed by adding a local
`stdRate = useSystemInfo().data?.vatRate ?? 0.07` (mirrors `LineItemsTable`'s own `FALLBACK_VAT`
pattern) and passing it through both call sites. This is the 15th file — the cap is hit exactly,
not exceeded. Reported per "report, don't improvise."

**RED-then-GREEN evidence (git-stash technique — spec's own tests, no test file edited):**
`git stash push` on the 5 interdependent backend service files (`SalesLineBackstop.cs`,
`QuotationChainServices.cs`, `SalesOrderDeliveryServices.cs`, `BillingNoteService.cs`,
`TaxInvoiceService.cs`) reverted the ladder/guard logic to pre-fix while the two new test files
stayed as written. Rebuilt clean (0/0 — public signatures unaffected), ran the 21 new tests:
**13 failed / 8 passed** — every failure was a fix-dependent assertion (exempt-product rate,
mixed-case lookup, `quotation.already_invoiced` never thrown, 409 never returned); every pass was
a byte-identical/unaffected-path pinning test (`Taxable_product_is_unaffected_by_the_exempt_ladder`,
`Free_text_line_claiming_exempt_type_still_charges_vat`,
`Exempt_product_on_a_non_vat_company_stays_on_the_VAT0_sentinel`,
`Exact_case_code_still_resolves_unchanged`, `Exempt_product_honours_an_exempt_code_the_caller_supplied`
[step-2 code-supplied-and-found was already unconditional pre-fix], the two "not blocked" N2 tests,
and `Update_draft_can_re_save_its_own_quotation_link`). Bonus evidence: `A_draft_cannot_be_posted_once_a_sibling_was_posted`
failed with a RAW `Npgsql.PostgresException 23505` on `ix_tax_invoices_quotation_id` — proof the
DB-level backstop (the migration, already applied to `teas_test`) was live even before the C#
guard/catch existed, exactly the layered-defense the design intended. `git stash pop` restored the
fix cleanly; rebuild 0/0; re-ran the same 21 (now green) + the 4 must-stay-green classes = **45/45
passed, 0 failed, 0 skipped**.

**Evidence — gates, verbatim counts:**
1. `dotnet build Accounting.sln` (real path, not subst) → **0 Warning(s), 0 Error(s)**.
2. `npx tsc --noEmit` (frontend/) → clean, no output.
3. Filtered run, per class: `TaxCodePairIntegrityTests` 5/5, `TaxInvoiceRateDerivationTests` 7/7,
   `ChainConversionIntegrityTests` 5/5, `NonVatBillingTests` 7/7 (all four **byte-for-byte
   untouched, unedited**), `ExemptProductTaxResolutionTests` 13/13 (new), `QuotationSingleInvoiceTests`
   8/8 (new). Combined: **45 passed, 0 failed, 0 skipped**.
4. Post-migration DB check: `SELECT indexdef FROM pg_indexes WHERE indexname=
   'ix_tax_invoices_quotation_id'` → `CREATE UNIQUE INDEX ... WHERE ((quotation_id IS NOT NULL)
   AND ((status)::text = 'POSTED'::text))` — confirms the index actually landed on `teas_test`,
   not just that the migration file looks right.

Full suite (1255/0/14 + 188 baseline) intentionally NOT run — Fable runs it per the dispatch.

**New footgun for troubles-wiki.md (candidate, Fable to triage):** targeted `git stash push --
<paths>` on a subset of interdependent files is a fast, no-test-file-edit way to get RED evidence
for an already-implemented fix, PROVIDED every file in the subset is mutually consistent when
reverted together (here: all 5 files share the old `SalesLineBackstop` API surface). Reverting a
strict subset that leaves a caller referencing a renamed/added method breaks the build instead of
producing a clean RED.

(append here)

---

# §Design — Opus, 2026-08-18

> Everything below is designed against source read this session; every claim carries a
> `file:line`. Nothing here was assumed from the review document. Read §Conflicts / Deviations before
> dispatching — three literal deviations from the binding constraints need Fable's one-line
> ratification.

## §Facts established in code (this pass) — do not re-derive

| # | Fact | Evidence |
|---|---|---|
| N.1 | `Product.DefaultOutputTaxCodeId` is read by **nothing** except product CRUD/detail projection. No document builder consults it. | grep `DefaultOutputTaxCodeId` over `backend/src` → `Product.cs:35`, `ProductDtos.cs:13,30,51`, `ProductService.cs:65,86,180`, migrations only |
| N.2 | `ProductListItem` does **not** carry `DefaultOutputTaxCodeId`; only `ProductDetail` does. The line-item picker therefore cannot see it. | `ProductDtos.cs:39-45` vs `:48-53`; `ProductService.cs:162-171` |
| N.3 | The product settings screen sends `defaultOutputTaxCodeId: null` on **both create and update**, and `openEdit` does not even read it into form state. A human edit of any product **destroys** the field. | `frontend/app/(dashboard)/settings/products/page.tsx:199, 211, 171-186` |
| N.4 | The field *is* settable via `IProductService` / `POST /products` and the MCP tool `create_product` (which binds the whole `CreateProductRequest`). | `TeasMcpTools.cs:847-858` |
| N.5 | The 12 seeded tax codes contain **no generic exempt catch-all** — all 8 exempt rows are specific ม.81(1)(x) categories. | `MasterDataServices.cs:396-411` |
| N.6 | `SalesCategorizer` buckets a line whose `tax_code` is absent from the company master as **ZERO_RATED** when `TaxRate <= 0` — never EXEMPT. | `SalesCategorizer.cs:60-72` |
| N.7 | `PaperLine` carries **no tax code and no legal ref** — a per-line tax code is never printed on any document. Exempt value appears only as the aggregate `PaperSummary.NonTaxable` row. | `PaperDocModel.cs:38-45, 64-67` |
| N.8 | The EF tenant filter has **no super-admin arm** (removed 2026-07-08). `db.TaxCodes` in a request context is always exactly the caller's company. The migration-time context (`_tenant == null`) is the only bypass and never runs `SalesLineBackstop`. | `AccountingDbContext.cs:147-175` |
| N.9 | `tax.tax_codes` also carries RLS `company_isolation` (second belt). | `SqlScripts/010_rls_policies.sql:8-32` |
| N.10 | `/system/info.vat_rate` is served from **`ICompanyTaxConfigService.GetAsync().VatRate`** — the exact same source the backstop's `companyVatRate` comes from. A frontend that renders `sys.vatRate` renders the server's own rate. | `Program.cs:526-538`; `TaxInvoiceService.cs:411`; `QuotationChainServices.cs:163` (`cfg.VatRate`) |
| N.11 | `products.product_type` stores the literals `GOOD` / `SERVICE` / `EXEMPT_GOOD` / `EXEMPT_SERVICE`. | `Configurations/Master/ProductConfiguration.cs:12-27` |
| N.12 | **A Tax Invoice cannot be deleted, cancelled or voided by any application path.** No `MapDelete` for `/tax-invoices` anywhere; no code assigns `DocumentStatus.Voided` to a `TaxInvoice`; `MarkPosted` is the only status writer. Reachable states = `Draft`, `Posted`. | `TaxInvoiceEndpoints.cs` (whole file), `ApiV1Endpoints.cs:42-61`; grep `.Status = DocumentStatus` over `backend/src` → zero TI writers besides `MarkPosted` (`TaxInvoice.cs:126-140`) |
| N.13 | `POSTED→VOIDED` **is** legal at the DB trigger level, so a future void feature drops the row out of a `status='POSTED'` partial index automatically. | `SqlScripts/583_tax_invoice_header_immutable_v2.sql:45-52` |
| N.14 | `quotation_id` is **not** in trigger 583's frozen-field list — a DBA can re-point or clear it on a posted TI without disabling any trigger (unlike a line repair, which needs 582 disabled). | same file, `:24-38` |
| N.15 | `TaxInvoice.QuotationId` is written from the request on **three** paths, not one: `CreateFromQuotationAsync` → `CreateDraftCoreAsync` (`TaxInvoiceService.cs:353`), a plain `POST /tax-invoices` carrying `quotationId`, and `UpdateDraftAsync` (`:488`). Only the first is what the review looked at. | `TaxInvoiceService.cs:214-247, 353, 488` |
| N.16 | `PostAsync` **already has** a race-mapping wrapper (`DbUpdateConcurrencyException` / 23514 → `ti.locked_mismatch` → 409). A 23505 currently falls through it to the generic 500. | `TaxInvoiceService.cs:543-563` |
| N.17 | EF migrations are applied **at API startup** (`DbInitializer.InitializeAsync` → `MigrateAsync`), gated only by `Database:RunInitializerOnStartup` (default true). A failing `CREATE UNIQUE INDEX` is therefore a **startup crash-loop**, not a migration-tool error. | `DbInitializer.cs:103`; `Program.cs:438-442` |
| N.18 | `sales.tax_invoices` is `ENABLE` **+ `FORCE` ROW LEVEL SECURITY** with `company_isolation` keyed on `current_setting('app.company_id')`. FORCE means even the table owner is filtered — only a `BYPASSRLS`/superuser role sees all rows. | `SqlScripts/040_tax_invoice_immutability.sql:49-57` |
| N.19 | `HasIndex(t => t.SalesOrderId).HasFilter("sales_order_id IS NOT NULL")` — the **SO→TI sibling has NO unique index**. Its `so.invoice_exists` guard is service-only and racy. Same for `billing_note_id` and `delivery_order_id`. | `TaxInvoiceConfiguration.cs:82, 88, 91` |
| N.20 | `LoadTaxCodeFlagsAsync` applies **no `IsActive` filter** — an inactive code named by a request still matches today. | `SalesLineBackstop.cs:69-72` |

---

## §Design-N1 — the exempt-product clamp

### N1.0 What changes, in one sentence

`SalesLineBackstop` gains (a) a product-master **tax-defaults** map alongside the product-type map
it already loads, and (b) two new ladder steps that make ม.81 exemption a property of the
**product master**, not of whatever code the caller happened to send.

### N1.1 The new ladder — exact, in order (replaces the doc comment at `SalesLineBackstop.cs:110-125`)

Let `type` = master `ProductType` when `productId` resolves, else `requestedType ?? "GOOD"`
(**unchanged**), and let

> **`exemptProduct`** = `productId` resolved in the product-defaults map **AND** its master
> `ProductType` is `EXEMPT_GOOD` or `EXEMPT_SERVICE`.

```
1. non-VAT company (vatMode == false)
      -> (type, 0, "VAT0", SYNTHETIC_TAX_CODE_ID)                      [UNCHANGED]

2. code supplied AND found in this company's master as `flags`:
   2a. !exemptProduct
         -> (type, flags.IsExempt || flags.IsZeroRated ? 0 : companyVatRate,
             flags.Code, flags.TaxCodeId)                              [UNCHANGED — today's step 2]
   2b. exemptProduct AND flags.IsExempt
         -> (type, 0, flags.Code, flags.TaxCodeId)
            the caller named a real ม.81 category; honour it.
   2c. exemptProduct AND !flags.IsExempt   (a taxable OR a zero-rated code)
         -> FALL THROUGH to step 3. ม.81 exemption is a property of the goods/service
            (master data), so a non-exempt code on an exempt product is discarded, not applied.
            Deliberate: a ZERO-RATED code also falls through even though its rate is 0 too —
            the difference is the ภ.พ.30 bucket (EXEMPT vs ZERO_RATED, N.6), and the master wins.

3. exemptProduct AND the product's DefaultOutputTaxCodeId resolves, in THIS company's master,
   to a row `d` with d.Direction == Output AND d.IsActive AND d.IsExempt
      -> (type, 0, d.Code, d.TaxCodeId)
         the tenant curated the right ม.81 category on the product — this is the good path.

4. exemptProduct AND the company master holds at least one Output + IsActive + IsExempt code
      -> (type, 0, e.Code, e.TaxCodeId)
         `e` = ExemptOutputFallback: the LOWEST TaxCodeId among that set. Deterministic,
         documented, and a REAL master row of this company (Unit A I4 case (a)).
         Why a real row and not a sentinel: an unmatched code with rate 0 is bucketed
         ZERO_RATED by SalesCategorizer (N.6) — that is the wrong ภ.พ.30 box AND it inflates
         the ม.82/6 proportional-input-VAT ratio (real money). A real exempt row buckets EXEMPT.
         Why the sub-category imprecision is acceptable: no per-line tax code is ever printed
         on any document (N.7); the fix for the category is step 3, not this fallback.

5. exemptProduct AND the company has NO exempt output code at all
   (raw-SQL-seeded tenants — memory seed-cos-bypass-createasync-taxcodes)
      -> (type, 0, "EXEMPT", SYNTHETIC_TAX_CODE_ID)
         a THIRD documented synthetic pair, joining ("VAT0", 0) and ("VAT7", 0). Rate 0 is the
         invariant that must hold; such a tenant's ภ.พ.30 bucketing is already degraded because
         its whole code master is missing.

6. standardOutput present  -> (type, companyVatRate, so.Code, so.TaxCodeId)   [UNCHANGED — today's step 3]
7. otherwise               -> (type, companyVatRate, "VAT7", SYNTHETIC_TAX_CODE_ID)  [UNCHANGED — today's step 4]
```

Steps 3/4/5 are reachable **only** when `exemptProduct` is true, and one of them always returns —
so there is no path from `exemptProduct == true` to a rate greater than 0. A non-exempt
product-linked line and every free-text line take exactly today's path (2a → 6 → 7).

### N1.2 Boundaries the implementer must NOT cross (each of these is a review trap)

- **`IsActive` and `Direction == Output` are checked on the NEW steps only (3, 4).** Step 2 keeps
  matching inactive and input-direction codes exactly as today (N.20) — adding a filter there is a
  silent behaviour change to a green path.
- **A free-text line (no `productId`) claiming `productType: "EXEMPT_GOOD"` does NOT get the
  clamp.** The caller's type string is not master data; the designed channel for expressing
  exemption on a free-text line is the F14 tax-code picker, which is validated against the master.
  Trusting a client string to *reduce* charged VAT is the exact inversion of §4.6.
- **Rule D is DEFERRED, not forgotten.** A `GOOD`/`SERVICE` product with a `DefaultOutputTaxCodeId`
  does **not** resolve through it in this unit. This is the settled answer to binding constraint
  N1 item 3, with reasons:
  1. N1's defect is exclusively the exempt case; Rule D changes the VAT *charged* on taxable lines.
  2. The only UI that writes the field writes `null` (N.3) — it is set only via API/MCP (N.4) and is
     destroyed by the next human product edit. A tax-rate-controlling read of a field whose only UI
     write is `null` is a data-loss trap, not a feature.
  3. Rule D makes the locked line-rate display wrong (see N1.4), so it must ship **with** the picker
     plumbing (`ProductListItem` + `ProductPick` + `ProductSearchModal` + `ProductQuickCreateModal`)
     **and** a tax-code select on the product settings screen. That is its own unit.
  4. Cost of deferring is ~3 lines: Rule D is the `else` of step 3 (`!exemptProduct` →
     `(type, d.IsExempt || d.IsZeroRated ? 0 : companyVatRate, d.Code, d.TaxCodeId)`). Mark the
     insertion point with
     `// ponytail: Rule D (deferred — specs/fix-review-n-findings-2026-08-17.md §N1.2)`.
     **Do not write the branch.**
- **Chain-copy paths (`deriveLineTax:false`) are untouched.** A quotation written *before* this fix
  with 7% on an exempt product still copies 7% into its TI. That is the binding constraint, and the
  detection query (§N1.6) is how that residue becomes visible.

### N1.3 Loader shape — one query replaces two lookups (this is also the whole of N3)

`LoadTaxCodeFlagsAsync` stops filtering by the request's codes and returns a small object that
serves **three** lookups from **one** rowset. `Resolve` is synchronous, so every lookup it needs —
including the exempt fallback — must be precomputed here, never inside the per-line loop
(Unit A trap §9.4).

```csharp
// SalesLineBackstop.cs — shape only; the implementer writes the body.

/// <summary>Classification + identity of a per-company VAT tax code (tax.tax_codes).
/// Code carries the MASTER ROW's casing (trap §9.2).</summary>
public readonly record struct TaxCodeFlags(int TaxCodeId, string Code, bool IsExempt, bool IsZeroRated);

/// <summary>Every tax code of the CALLER'S OWN company (EF tenant filter, AccountingDbContext.cs:174
/// — no super-admin arm since 2026-07-08; RLS company_isolation is the second belt).
/// Loaded ONCE per request. tax.tax_codes is 12 rows on a seeded tenant, so loading the whole
/// master is cheaper than the round-trip that filtered it (N3).</summary>
public sealed class TaxCodeMaster
{
    /// keyed case-INSENSITIVELY on the master row's Code (this is the N3 fix).
    public required IReadOnlyDictionary<string, TaxCodeFlags> ByCode { get; init; }
    /// keyed on tax_code_id — resolves Product.DefaultOutputTaxCodeId (ladder step 3).
    /// Only rows with Direction == Output && IsActive are present.
    public required IReadOnlyDictionary<int, TaxCodeFlags> ActiveOutputById { get; init; }
    /// ม.81 fallback (ladder step 4): lowest TaxCodeId among Direction==Output && IsActive && IsExempt.
    /// null when the tenant has no exempt output code at all → ladder step 5.
    public required TaxCodeFlags? ExemptOutputFallback { get; init; }
}

public static async Task<TaxCodeMaster> LoadTaxCodeMasterAsync(AccountingDbContext db, CancellationToken ct);
```

Body requirements, exactly:

1. `db.TaxCodes.AsNoTracking().Select(c => new { c.TaxCodeId, c.Code, c.IsExempt, c.IsZeroRated, c.Direction, c.IsActive }).ToListAsync(ct)`
   — **no `Where` at all.** The tenant filter is the only scoping and it is sufficient (N.8/N.9).
2. `ByCode`: `rows.OrderBy(r => r.TaxCodeId).GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase)`
   → dictionary with `StringComparer.OrdinalIgnoreCase`, value
   `new TaxCodeFlags(g.First().TaxCodeId, g.Key, g.First().IsExempt, g.First().IsZeroRated)`.
   **`OrderBy(TaxCodeId)` before `GroupBy` is new and load-bearing**: the unique index is
   `(company_id, code)` case-**sensitive** (`TaxCodeConfiguration.cs:29`), so `VAT7` and `vat7` can
   coexist in one company; without the ordering `g.First()` is non-deterministic across calls.
   `g.Key` remains the master row's own casing — trap §9.2 survives untouched.
3. `ActiveOutputById`: `rows.Where(r => r.IsActive && r.Direction == TaxDirection.Output)` keyed on `TaxCodeId`.
4. `ExemptOutputFallback`: the `ActiveOutputById` value with `IsExempt` and the lowest `TaxCodeId`, else `null`.
5. `LoadStandardOutputTaxCodeAsync` is **left exactly as it is**. It is correct, tested, and its
   `cfg.VatMode ? … : null` gating at the call sites is load-bearing. Folding it in would churn five
   more call sites for no behavioural gain.

`LoadProductTypesAsync` becomes `LoadProductDefaultsAsync`, same query plus one column:

```csharp
public readonly record struct ProductTaxDefaults(string ProductType, int? DefaultOutputTaxCodeId);

public static async Task<Dictionary<long, ProductTaxDefaults>> LoadProductDefaultsAsync(
    AccountingDbContext db, IEnumerable<long?> productIds, CancellationToken ct);
```

`ProductType` is still the screaming-snake form produced by `ToScreamingSnake`
(`SalesLineBackstop.cs:157-163`).

`Resolve`'s parameter **order** is unchanged; two parameters change **type and name**:

```csharp
public static (string ProductType, decimal TaxRate, string TaxCode, int TaxCodeId) Resolve(
    bool vatMode, decimal companyVatRate, long? productId, string? requestedType,
    decimal requestedRate, string? taxCode,
    IReadOnlyDictionary<long, ProductTaxDefaults> productDefaults,   // was <long,string> productTypes
    TaxCodeMaster taxCodes,                                          // was IReadOnlyDictionary<string,TaxCodeFlags>
    (int TaxCodeId, string Code)? standardOutput);
```

**Seven of the eight `Resolve(...)` call lines need no textual change** — they pass both as
positional variables, so only the two `Load…Async` lines above each of them move. The **eighth**,
`TaxInvoiceService.cs:426-428`, passes `productTypes:` as a **named** argument and must be rewritten
to `productDefaults:` — which the file-5(a) edit is rewriting anyway (it is the call site that stops
passing `EmptyProductTypes`). Do not let the rename surprise you there.

### N1.4 Frontend — the minimal change, and why it is sufficient

After N1 the server's rate for a **product-linked** line is: `0` if the master says the product is
exempt, else `companyVatRate` (steps 2a/6/7 — Rule D deferred). The screen's locked display is
`taxRateForProductType(productType)`, which hardcodes `0.07` (`ProductPicker.tsx:27-29`). So the
only mismatch left is a company whose `VatRate ≠ 0.07`.

**Change, in full:**
- `frontend/components/forms/ProductPicker.tsx` —
  `export function taxRateForProductType(t: ProductTypeStr, stdRate: number): number`
  → `t === 'EXEMPT_GOOD' || t === 'EXEMPT_SERVICE' ? 0 : stdRate`. Update the doc comment: the rate
  is the company's configured rate, never a literal.
- `frontend/components/ui/LineItemsTable.tsx:165` —
  `taxRate: taxRateForProductType(p.productType, stdRate)`.
  `stdRate` is already in scope at `:96` (`sys?.vatRate ?? FALLBACK_VAT`) and comes from
  `/system/info.vat_rate`, i.e. the server's own `ICompanyTaxConfigService.VatRate` (**N.10**).

Nothing else on the screen changes: `taxCode: null, taxCodeId: null` on product select
(`:170-171`) is already correct and is exactly what makes the server authoritative. The purchase
side shares `LineItemsTable`; the substitution is a no-op there for every 7% company and a strict
improvement otherwise — the purchase backend does not use `SalesLineBackstop` at all (grep over
`Accounting.Infrastructure/Purchase/**` → zero references), so no purchase behaviour moves.

**Explicitly NOT done here** (each would be a scope breach): surfacing `defaultOutputTaxCodeId` on
`ProductListItem`/`ProductPick`; adding a tax-code select to the product settings screen; unlocking
the rate cell for product-linked lines.

### N1.5 Money invariants — stated as invariants, with the mechanism that preserves each

| # | Invariant | Preserved by |
|---|---|---|
| **M1** | An `EXEMPT_GOOD`/`EXEMPT_SERVICE` product line **never** stores `TaxRate > 0`, therefore contributes 0 to `TaxAmount` and 0 to `TaxableAmount`, and its value lands in `NonTaxableAmount`. | Every exempt exit of the ladder (2b, 3, 4, 5) returns literal `0m`; one of them always returns, so steps 6/7 are unreachable once `exemptProduct` is true. There is no path from `exemptProduct == true` to `companyVatRate`. |
| **M2** | A `GOOD`/`SERVICE` line, and every free-text line, whose caller supplied **no code** or a code matching a master row **exactly** (case included), produces **byte-identical** output to today for the same input. This covers the binding constraint's case verbatim ("no product default and no caller code keeps today's exact behaviour"). | `exemptProduct` false ⇒ the ladder is literally today's code path (2a → 6 → 7); Rule D deferred means step 3 never fires for them. Evidenced by `TaxCodePairIntegrityTests`, `TaxInvoiceRateDerivationTests`, `ChainConversionIntegrityTests`, `NonVatBillingTests` staying green **unedited**. |
| **M2-exception** | Exactly **one** input class deliberately changes: a caller code that differs from a master row only by **case** (`exempt-book` vs `EXEMPT-BOOK`) now matches that row instead of falling through to the company's standard output code. That is N3 — the whole point of the finding — so the change is from "silently charged 7%" to "the master row's classification and casing". No other input moves. | N3 (§N3.1). Asserted by `Mixed_case_exempt_code_resolves_and_stores_the_master_casing` and its zero-rated twin, which are **RED before the change and GREEN after**; `Exact_case_code_still_resolves_unchanged` pins the unchanged half. |
| **M3** | Stored `(tax_code, tax_code_id)` is always a matched pair: either a real row of **this company's** `tax.tax_codes` (steps 2, 3, 4, 6) or one of exactly three documented synthetic pairs — `("VAT0", 0)` non-VAT, `("VAT7", 0)` no master, `("EXEMPT", 0)` exempt product with no exempt code in master. F13 / Unit A I4 unweakened. | `ActiveOutputById` and `ByCode` are built from the tenant-filtered `db.TaxCodes` (N.8) — a foreign tenant's id cannot enter. `Direction == Output` on step 3 stops a product default pointing at an input code: `DefaultOutputTaxCodeId` gets **no** tenant and **no** direction validation on write (`ProductService.cs:65,86`), so this check is the only thing between a mis-set default and a cross-tenant / input code on a sales line. |
| **M4** | Ledger: for every input that reaches a currently-green test, `Dr == Cr` and every total is unchanged. For a newly-exempt line the reduction is self-consistent: line `TaxAmount` 0 ⇒ header `TaxAmount` smaller ⇒ `TotalAmount` smaller ⇒ AR debit smaller by exactly that amount ⇒ output-VAT credit smaller by exactly that amount. **Cash the customer owes and the VAT payable move together; no third account is touched.** | `BuildLine` (`TaxInvoiceService.cs:656-700`) computes `vat` from the resolved rate and `total = net + vat`; `RebuildLinesAndTotalsAsync` (`:434-440`) rolls both up; `GlPostingService.PostTaxInvoiceAsync` posts from those header fields. No independent VAT figure exists that could diverge. |
| **M5** | ภ.พ.30 / ม.82/6 classification of a newly-exempt line is **EXEMPT**, not ZERO_RATED. | Steps 3 and 4 store a code that exists in the company master with `IsExempt = true`, so `SalesCategorizer.cs:61` takes the dictionary branch, not the `TaxRate <= 0 ⇒ zero` fallback. Step 5's synthetic `"EXEMPT"` **does** fall to ZERO_RATED — accepted and documented, reachable only on a tenant with no tax-code master at all, whose classification is already degraded. |
| **M6** | **No pre-existing stored row moves.** This change has no data migration and no backfill; it only affects lines written after it ships. | No `UPDATE` anywhere in the design; the detection query in §N1.6 is `SELECT`-only. |

### N1.6 Detection query for historical mis-charged rows (query only — repairs NOTHING)

Posted tax documents are immutable (`SqlScripts/582`, `583`); this is exposure assessment for Ham.

> **RLS FOOTGUN — read before running.** `sales.tax_invoices` is `FORCE ROW LEVEL SECURITY`
> (N.18): run as the application role with no `app.company_id` GUC and this query returns **zero
> rows and looks like good news**. Run it as a `BYPASSRLS`/superuser role (`psql -U postgres`), or
> per company with `SET app.company_id = <id>`. **Probe first, then trust the result:**
> `SELECT count(*) FROM sales.tax_invoices;` must equal the true row count — if it returns 0 on a DB
> you know holds invoices, your session is being filtered and every finding below is a false
> negative. `master.products` and the `sales.*_lines` tables are not in any RLS policy list, but the
> header tables they join to are.

```sql
-- N1 exposure: sales lines that charged VAT on a product the master classifies as ม.81-exempt.
-- Read-only. Run as a BYPASSRLS role. Verify the count probe above FIRST.
WITH exempt_products AS (
    SELECT product_id, company_id, product_code, product_type
    FROM master.products
    WHERE product_type IN ('EXEMPT_GOOD', 'EXEMPT_SERVICE')   -- literals per ProductConfiguration.cs:12-19
)
SELECT 'TAX_INVOICE' AS doc_kind, h.company_id, h.status AS status, h.doc_no, h.doc_date,
       p.product_code, l.description_th, l.tax_code, l.tax_rate, l.line_amount, l.tax_amount
FROM sales.tax_invoice_lines l
JOIN sales.tax_invoices h ON h.tax_invoice_id = l.tax_invoice_id
JOIN exempt_products    p ON p.product_id     = l.product_id
WHERE l.tax_rate > 0
UNION ALL
SELECT 'QUOTATION', h.company_id, h.status, h.doc_no, h.doc_date,
       p.product_code, l.description_th, l.tax_code, l.tax_rate, l.line_amount, l.tax_amount
FROM sales.quotation_lines l
JOIN sales.quotations h ON h.quotation_id = l.quotation_id
JOIN exempt_products  p ON p.product_id   = l.product_id
WHERE l.tax_rate > 0
UNION ALL
SELECT 'SALES_ORDER', h.company_id, h.status, h.doc_no, h.doc_date,
       p.product_code, l.description_th, l.tax_code, l.tax_rate, l.line_amount, l.tax_amount
FROM sales.sales_order_lines l
JOIN sales.sales_orders h ON h.sales_order_id = l.sales_order_id
JOIN exempt_products    p ON p.product_id     = l.product_id
WHERE l.tax_rate > 0
UNION ALL
SELECT 'DELIVERY_ORDER', h.company_id, h.status, h.doc_no, h.doc_date,
       p.product_code, l.description_th, l.tax_code, l.tax_rate, l.line_amount, l.tax_amount
FROM sales.delivery_order_lines l
JOIN sales.delivery_orders h ON h.delivery_order_id = l.delivery_order_id
JOIN exempt_products      p ON p.product_id         = l.product_id
WHERE l.tax_rate > 0
UNION ALL
SELECT 'BILLING_NOTE', h.company_id, h.status, h.doc_no, h.doc_date,
       p.product_code, l.description_th, l.tax_code, l.tax_rate, l.line_amount, l.tax_amount
FROM sales.billing_note_lines l
JOIN sales.billing_notes h ON h.billing_note_id = l.billing_note_id
JOIN exempt_products     p ON p.product_id      = l.product_id
WHERE l.tax_rate > 0
ORDER BY 1, 2, 5;

-- Total over-charged output VAT on POSTED tax invoices only (the legally-issued exposure):
SELECT h.company_id, count(*) AS lines, sum(l.tax_amount) AS overcharged_output_vat
FROM sales.tax_invoice_lines l
JOIN sales.tax_invoices h ON h.tax_invoice_id = l.tax_invoice_id
JOIN master.products    p ON p.product_id     = l.product_id
WHERE h.status = 'POSTED' AND l.tax_rate > 0
  AND p.product_type IN ('EXEMPT_GOOD', 'EXEMPT_SERVICE')
GROUP BY h.company_id;
```

If the second query returns rows, the correction instrument is a Credit Note (ใบลดหนี้) through the
existing `TaxAdjustmentNote` flow — **Ham's decision, not the implementer's, and not in this unit.**

### N1.7 Per-consumer blast radius — every writer and reader of the widened seam

Seam widened: *the inputs `SalesLineBackstop.Resolve` consults* (was: caller code + company VAT
mode/rate; now: + product-master exemption + product default + the company's exempt code set).

**Writers — the 8 `Resolve` call sites (all inherit the new behaviour automatically):**

| consumer (file:line) | change needed | disposition |
|---|---|---|
| `QuotationChainServices.cs:88-95` (Q create draft) | `LoadProductTypesAsync`→`LoadProductDefaultsAsync`; `LoadTaxCodeFlagsAsync`→`LoadTaxCodeMasterAsync` (drop the codes arg) | **EXTEND** — 2 lines |
| `QuotationChainServices.cs:161-168` (Q update draft) | same | **EXTEND** — 2 lines |
| `SalesOrderDeliveryServices.cs:54-61` (SO create draft) | same | **EXTEND** — 2 lines |
| `SalesOrderDeliveryServices.cs:123-130` (SO update draft) | same | **EXTEND** — 2 lines |
| `SalesOrderDeliveryServices.cs:195-214` (SO→DO conversion) | same | **EXTEND** — 2 lines |
| `SalesOrderDeliveryServices.cs:352-369` (standalone DO) | same | **EXTEND** — 2 lines |
| `BillingNoteService.cs:466-473` (BN request-fed) | same | **EXTEND** — 2 lines |
| `TaxInvoiceService.RebuildLinesAndTotalsAsync:415-431` | **special** — see below | **EXTEND** |

`TaxInvoiceService` is the only call site that passes `EmptyProductTypes` (`:29-31`), because it
pre-resolves `ProductType` onto `srcLines` with its own inline `_db.Products` query (`:383-403`).
That inline query and its duplicated enum→string `switch` are **replaced** by one
`SalesLineBackstop.LoadProductDefaultsAsync(_db, reqLines.Select(l => l.ProductId), ct)` call whose
result is used **twice**: to build the `l with { ProductType = … }` override (unchanged semantics —
still applied on the `deriveLineTax:false` path too) and as the `productDefaults` argument to
`Resolve`. The `EmptyProductTypes` field and the duplicated switch are then deleted. Net: fewer
lines than before. Keep the `if (needType.Count > 0)` short-circuit shape.

**Chain-copy paths (`deriveLineTax:false`) — NO CHANGE, deliberately:**
`CreateFromBillingNoteAsync` (`:104-115`), `CreateFromDeliveryOrderAsync` (`:148-158`),
`CreateFromSalesOrderAsync` (`:193-201`), `CreateFromQuotationAsync` (`:226-241`),
`BillingNoteService` TI-grouping (`:504-519`), `QuotationService.ConvertToSalesOrderAsync`.
They inherit an already-normalised pair; re-deriving would double-process. Binding constraint.

**Readers of the seam:**

| consumer | reads | disposition |
|---|---|---|
| `SalesCategorizer.ComputeAsync` (`:42-72`) | line `tax_code` string → master flags; fallback `TaxRate>0` | **NO CHANGE** — newly-exempt lines carry a code that IS in the master, so they land in the EXEMPT bucket (an improvement, M5). No existing row moves (M6). |
| `TaxFilingService` output-VAT register / local `CategoryOf` | same code-string rule | **NO CHANGE** — same reasoning |
| `ProportionalInputVatService` (ม.82/6) | `SalesCategorizer` totals | **NO CHANGE** — consumes the improved buckets |
| `SuggestWhtBaseAsync` and any other `line.ProductType` reader | product type string | **NO CHANGE** — `type` resolution is byte-identical |
| anything reading line `tax_code_id` | nothing (Unit A F1.17) | — |
| `PaperLine` / PDF / `/paper` DTOs | no tax code at all (N.7) | **NO CHANGE** |
| frontend purchase forms via `LineItemsTable` | `taxRateForProductType` | **EXTEND** — signature change only; no purchase backend path uses the backstop |
| MCP line DTOs (`TeasMcpTools.cs:442-446, 610-613, 1509-1512`) | pass `TaxCodeId`/`TaxCode` through | **NO SOURCE CHANGE** — the server resolves; matches the tools' own documented contract |
| `Product.DefaultOutputTaxCodeId` writers (`ProductService.cs:65,86`; MCP `create_product`) | write only | **NO CHANGE** — but see N.3: the settings UI nulls it. Logged in the attempt log, not designed for. |

---

## §Design-N2 — one Tax Invoice per Quotation

### N2.0 The event, and every channel that can cause it

The rule keys on the real-world event **"a Tax Invoice is legally issued against this
quotation"** — not on "somebody wrote `quotation_id`". Channels that write
`tax_invoices.quotation_id`, enumerated (N.15):

| # | channel | reaches | today |
|---|---|---|---|
| C1 | `POST /quotations/{id}/create-tax-invoice` → `CreateFromQuotationAsync` → `CreateDraftCoreAsync` | `TaxInvoiceService.cs:214-247, 353` | unguarded |
| C2 | plain `POST /tax-invoices` (and `POST /api/v1/tax-invoices`) with `quotationId` in the body → `CreateDraftAsync` → `CreateDraftCoreAsync` | `TaxInvoiceEndpoints.cs:14-26`, `ApiV1Endpoints.cs:42-48`, `TaxInvoiceService.cs:353` | unguarded — **the review did not see this one** |
| C3 | MCP `update_tax_invoice_draft` → `UpdateDraftAsync` (re-links or clears `QuotationId` on a draft) | `TeasMcpTools.cs:1499-1534`; `TaxInvoiceService.cs:488` | unguarded |
| C4 | MCP `create_tax_invoice_draft` | `TeasMcpTools.cs:~430-451` → `CreateDraftAsync` → C2 | covered by C2 |
| C5 | raw SQL / seed script | — | out of application scope; the DB index is the backstop |

**Legal issuance itself** happens in exactly one place: `PostCoreAsync` →
`MarkPosted` (`TaxInvoiceService.cs:565-637`), where the document number is allocated. A draft
carries no `DocNo`, appears in no VAT report (`SalesCategorizer.cs:52` filters
`Status == Posted`), and creates no number gap (Unit A F1.25).

### N2.1 Which Tax Invoices count as blocking — and the exit analysis that decides it

**Decision: only `Status == Posted` blocks.**

Evidence forcing this (N.12): a Tax Invoice has **no delete route, no cancel route and no void
route**. `DocumentStatus.Voided` is never assigned to a `TaxInvoice` anywhere in `backend/src`.
Reachable states are `Draft` and `Posted`, and both are permanent.

Now trace the state behind each candidate guard:

| guard counts | state it creates | exit, in the app, without a DBA |
|---|---|---|
| **all statuses** (literal reading of the binding constraint, and what the SO sibling does) | one stray draft TI — a mis-click, an abandoned experiment, an agent's draft — permanently consumes the quotation's only conversion | **NONE.** The draft cannot be deleted (N.12) and the only unlink path is the MCP tool `update_tax_invoice_draft` (C3), which no browser user can reach: there is no `PUT /tax-invoices/{id}` and no draft-edit screen (Unit A F1.24, re-verified). This is the exact trap Unit A §3.4 refused this guard for, and nothing has changed since. |
| **Posted only** (chosen) | a second draft can be built but not posted | **YES, and it is the correct outcome.** The refusal says "this quotation is already invoiced", which is true; the stray draft is abandoned at zero cost — no document number burned (Unit A F1.25), invisible to every report. Identical residual cost to the draft litter Unit A already accepted (§3.0). |

So the compliance guarantee this design ships is: **at most one POSTED Tax Invoice per Quotation,
enforced in the service and closed against races by the database.** Unlimited abandoned drafts are
tolerated, exactly as they are today for every other document.

Bonus property: if a void feature is ever added, `POSTED→VOIDED` is already DB-legal (N.13) and a
voided TI drops out of the partial index automatically, freeing the quotation — the design does not
have to be revisited.

### N2.2 Guard placement — three call sites, one helper

Add to `TaxInvoiceService` (partial class, same file):

```csharp
/// <summary>N2 — ม.86/4: at most ONE POSTED Tax Invoice may be issued against a Quotation.
/// Drafts do not block (a TI has no delete/void path — N.12 — so a stray draft would trap the
/// quotation forever). <paramref name="excludeTaxInvoiceId"/> lets the poster/updater ignore
/// its own row. NEVER counts Draft rows: the guarded event is legal issuance, not linkage.</summary>
private async Task EnsureQuotationNotInvoicedAsync(
    long? quotationId, long? excludeTaxInvoiceId, CancellationToken ct)
{
    if (quotationId is not { } qid) return;
    var blocking = await _db.TaxInvoices.AsNoTracking()
        .Where(t => t.QuotationId == qid
                 && t.Status == Domain.Enums.DocumentStatus.Posted
                 && (excludeTaxInvoiceId == null || t.TaxInvoiceId != excludeTaxInvoiceId))
        .Select(t => new { t.TaxInvoiceId, t.DocNo })
        .FirstOrDefaultAsync(ct);
    if (blocking is null) return;
    throw new DomainException("quotation.already_invoiced",
        $"Quotation {qid} has already been invoiced by Tax Invoice " +
        $"{blocking.DocNo ?? blocking.TaxInvoiceId.ToString()}.");
}
```

Notes the implementer must not "improve":
- The EF tenant filter already scopes `_db.TaxInvoices` to the caller's company (N.8) — **do not**
  add `t.CompanyId == _tenant.CompanyId`; the existing sibling guards
  (`TaxInvoiceService.cs:97-101, 141-145, 186-190`) do add it, but they predate the 2026-07-08
  super-admin-arm removal. Matching the tenant filter alone is correct and is what makes the guard
  agree with the DB index, which has no company column.
- The message names the blocking document so the user knows where to look — a refusal that does not
  say what is blocking is half a trap.

Call sites — **exactly three**:

| # | where | call |
|---|---|---|
| G1 | `CreateDraftCoreAsync`, immediately after the `EnsureVatRegisteredAsync(ct)` call (`TaxInvoiceService.cs:~255`) | `await EnsureQuotationNotInvoicedAsync(req.QuotationId, null, ct);` — covers **C1 and C2** in one place, exactly as `EnsureVatRegisteredAsync` is the single VAT chokepoint |
| G2 | `UpdateDraftAsync`, immediately before `ti.QuotationId = req.QuotationId` (`:488`) | `await EnsureQuotationNotInvoicedAsync(req.QuotationId, taxInvoiceId, ct);` — covers **C3**; the `exclude` argument stops a no-op re-save of the TI's own link from tripping the guard |
| G3 | `PostCoreAsync`, immediately after the `ti` row is loaded and before `MarkPosted` runs (`:571-575`) | `await EnsureQuotationNotInvoicedAsync(ti.QuotationId, ti.TaxInvoiceId, ct);` — the moment of legal issuance. `Status == Posted` in the predicate already excludes this still-Draft row, but pass the exclude id anyway so the guard stays correct if the predicate ever widens |

Do **not** add the guard to `CreateFromQuotationAsync` itself: it funnels through
`CreateDraftCoreAsync`, and a second copy would drift. This deliberately differs in *shape* from the
BN/DO/SO siblings, which must guard in their own method because those link ids are stamped
post-hoc (`ti.BillingNoteId = …` after create); `QuotationId` travels in the request record, so the
chokepoint is available and is the better placement.

### N2.3 Typed error code and HTTP status

- **Code:** `quotation.already_invoiced` (the naming the binding constraint proposed). Sibling
  shapes confirmed in code: `so.invoice_exists` (`:187-190`), `bn.ti_exists` (`:98-101`),
  `do.ti_exists` (`:142-145`), all `DomainException(code, message)`.
- **Status: 409 Conflict.** Add exactly one clause to
  `backend/src/Accounting.Api/Middleware/DomainExceptionMiddleware.cs:36-37`:

  ```csharp
  if (code.EndsWith(".locked_mismatch", StringComparison.Ordinal) || Ends(".body_mismatch")
      || Ends(".cross_bu_not_allowed_for_this_key")
      || Ends(".already_invoiced")) return StatusCodes.Status409Conflict;
  ```
  (keep the existing `Ends` local-function style; the line above is illustrative of the *set*, not
  of the formatting). Verified no existing DomainException code ends in `.already_invoiced`
  (grep over `backend/src` → 0 hits), so **no existing status moves**. It applies to both the
  `/api/v1` envelope branch (`:56-59`) and the RFC-7807 BFF branch (`:101-117`), which share
  `StatusFor`.
- Why 409 and not the siblings' 422: the guard and the race must be **indistinguishable to the
  client**, and a race is a conflict by definition. Making them one code means the frontend has one
  branch to write and the MCP tool one message to surface. The siblings keep their 422 — not
  touched, no regression.

### N2.4 Race closure — the filtered unique index

`backend/src/Accounting.Infrastructure/Persistence/Configurations/Sales/TaxInvoiceConfiguration.cs:77`
changes from

```csharp
b.HasIndex(t => t.QuotationId).HasFilter("quotation_id IS NOT NULL");
```

to

```csharp
// N2 — ม.86/4: at most one POSTED Tax Invoice per Quotation. The status arm is load-bearing,
// not an optimisation: a TI has no delete/cancel/void path (spec §N2.1), so an all-status
// unique index would let one abandoned DRAFT consume a quotation's only conversion forever.
// Status is stored UPPERCASE by the value converter above (:45-50). Name unchanged so the
// generated ix_tax_invoices_quotation_id stays the constraint name the 23505 handler matches.
b.HasIndex(t => t.QuotationId).IsUnique()
    .HasFilter("quotation_id IS NOT NULL AND status = 'POSTED'");
```

The generated index name is unchanged (`ix_tax_invoices_quotation_id`), because EF derives it from
the property list, which did not change. The migration will emit `DropIndex` + `CreateIndex` under
the same name — that is expected and correct.

**Migration** (`dotnet ef migrations add QuotationSingleInvoice` from the real repo path, never from
a `subst` drive — memory `minver-subst-stamping`):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "ix_tax_invoices_quotation_id", schema: "sales", table: "tax_invoices");
    migrationBuilder.CreateIndex(
        name: "ix_tax_invoices_quotation_id", schema: "sales", table: "tax_invoices",
        column: "quotation_id", unique: true,
        filter: "quotation_id IS NOT NULL AND status = 'POSTED'");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(
        name: "ix_tax_invoices_quotation_id", schema: "sales", table: "tax_invoices");
    migrationBuilder.CreateIndex(
        name: "ix_tax_invoices_quotation_id", schema: "sales", table: "tax_invoices",
        column: "quotation_id", filter: "quotation_id IS NOT NULL");
}
```

Accept whatever `ef migrations add` generates if it matches this shape; hand-edit only to match it.
The migrations history table is the custom `sys.__ef_migrations`
(`DependencyInjection.cs:22`, `AccountingDbContextFactory.cs:22`) — this is a normal
`add-migration`, no special handling.

### N2.5 Duplicate-data pre-check — behaviour, and the runtime context that makes it lie

> **This is the highest-risk step in the whole unit.** EF migrations run **at API startup**
> (N.17). A `CREATE UNIQUE INDEX` that finds duplicates raises 23505 inside
> `DbInitializer.InitializeAsync`, which is awaited before `app.Run()` — the API **fails to start**
> and, under pm2/systemd, **crash-loops**. There is no "migration failed, app still up" mode.

**Behaviour on duplicates: FAIL LOUDLY. Do not add a de-duplication step, do not make the index
conditional, do not `IF NOT EXISTS` around it.** Silently dropping or re-pointing a link on a posted
tax document is forbidden. The failure is the correct signal, and the pre-check below exists so it
never happens in production.

**Pre-check, run BEFORE `ef migrations add` on `teas_test`, and again on every target DB before
deploy:**

```sql
-- N2 pre-check: quotations that already carry more than one POSTED tax invoice.
-- MUST return zero rows before the migration is applied anywhere.
-- Run as a BYPASSRLS/superuser role — sales.tax_invoices is FORCE ROW LEVEL SECURITY (N.18);
-- the app role with no app.company_id GUC returns zero rows and looks clean.
-- Probe first:  SELECT count(*) FROM sales.tax_invoices;   -- must match the real row count
SELECT quotation_id, count(*) AS posted_tis, array_agg(doc_no ORDER BY tax_invoice_id) AS doc_nos
FROM sales.tax_invoices
WHERE quotation_id IS NOT NULL AND status = 'POSTED'
GROUP BY quotation_id
HAVING count(*) > 1
ORDER BY 2 DESC;

-- Wider picture (informational — these do NOT block the migration, drafts are not indexed):
SELECT quotation_id, count(*) FILTER (WHERE status = 'POSTED') AS posted,
       count(*) FILTER (WHERE status = 'DRAFT')  AS drafts
FROM sales.tax_invoices
WHERE quotation_id IS NOT NULL
GROUP BY quotation_id
HAVING count(*) > 1
ORDER BY 2 DESC, 3 DESC;
```

- **`teas_test` first.** The shared test DB is bloated (memory `teas-test-fixture-apply-once` —
  ~629 companies) and has been driving `CreateFromQuotationAsync` unguarded since Unit A. If the
  first query returns rows there, `PostgresFixture.MigrateAsync` (`PostgresFixture.cs:99`) fails and
  the **entire suite** goes red at fixture init, not in one test. Resolve before writing the
  migration.
- **If duplicates exist anywhere: STOP and report to Fable.** The remediation is a targeted
  `UPDATE sales.tax_invoices SET quotation_id = NULL WHERE tax_invoice_id = <the wrong one>` — legal
  without disabling any trigger, because `quotation_id` is **not** in trigger 583's frozen-field
  list (N.14) — but *which* TI is the wrong one is a business decision, never the implementer's.
- **Deploy probe (row counts, not exit codes).** After the API restarts on the target:
  `SELECT count(*) FROM pg_indexes WHERE schemaname='sales' AND indexname='ix_tax_invoices_quotation_id' AND indexdef ILIKE '%UNIQUE%';`
  must return 1, and
  `SELECT count(*) FROM sales.tax_invoices WHERE quotation_id IS NOT NULL;`
  must be unchanged from the pre-deploy count. A green HTTP 200 on `/health` is not evidence the
  index exists.
- **DDL and RLS:** `CREATE UNIQUE INDEX` scans the whole heap regardless of RLS and needs table
  ownership, which the app role has (it created these tables via `MigrateAsync` on first startup).
  RLS affects the **pre-check SELECT only** — that is the step that silently lies.

### N2.6 Race → 409 mapping mechanism

A 23505 from the new index can only surface at the moment a row becomes `POSTED`, i.e. inside
`PostCoreAsync`. `PostAsync` already wraps it (N.16); extend that wrapper — **do not add a new
try/catch anywhere else**:

```csharp
catch (Exception ex) when (IsQuotationInvoiceUniqueViolation(ex))
{
    throw new DomainException("quotation.already_invoiced",
        "This quotation was invoiced by another tax invoice moments ago. Reload and try again.");
}
catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsPostedRaceViolation(ex))
{
    throw new DomainException("ti.locked_mismatch", …);   // existing, unchanged
}

/// <summary>N2 — 23505 on ix_tax_invoices_quotation_id ONLY. Constraint-name-scoped so the
/// doc_no collision retry (CRIT-1, NumberedDocumentWriter.IsDocNoCollision) is never masked:
/// that path also raises 23505 and MUST keep its own bounded-retry handling.</summary>
private static bool IsQuotationInvoiceUniqueViolation(Exception ex) =>
    ex is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } pg }
    && pg.ConstraintName == "ix_tax_invoices_quotation_id";
```

**Ordering is load-bearing:** the new clause must come **before** the existing one — not because
the predicates overlap (23505 vs 23514/concurrency), but so a future widening of either predicate
cannot silently swallow the quotation case. And the constraint-name check is not optional:
`NumberedDocumentWriter.AllocateAndSaveAsync` (`TaxInvoiceService.cs:618-622`) relies on catching
its **own** 23505 to re-allocate a document number; a bare `SqlState == "23505"` catch here would
turn a recoverable numbering collision into a permanent 409.

No new catch is needed for G1/G2: nothing at draft-create or draft-update writes `status='POSTED'`,
so the index cannot fire there.

### N2.7 The exit, written out (the guard-safety statement)

| refusal | state the user is in | how they get out, in the app |
|---|---|---|
| G1 — create refused (`POST /quotations/{id}/create-tax-invoice` or a `POST /tax-invoices` carrying `quotationId`) | the quotation already has a posted tax invoice | Nothing to escape: this is the correct terminal state. The message names the blocking `DocNo`; the user opens it. A genuinely new sale needs a new quotation, or a plain tax invoice with no `quotationId` — which is **not** blocked. |
| G2 — draft re-link refused (MCP) | the agent tried to point a draft at an already-invoiced quotation | Send `quotationId: null` (or another quotation) on the same tool call. Unblocked. |
| G3 — post refused | the user holds a draft TI whose quotation was invoiced by a different TI in the meantime | Abandon the draft (costs nothing — no `DocNo`, invisible to reports, Unit A F1.25), **or** clear its `quotationId` via MCP `update_tax_invoice_draft` and post it as a standalone tax invoice. Two exits, one of them purely in the browser. |
| race — 409 at post | two people posted at once; one lost | Reload. The winner's TI is the one that exists; the loser is in the G3 state above, with the same two exits. |

No refusal in this design reaches a state that needs a DBA.

### N2.8 SO-sibling unique-index status report (asked for; scope NOT widened)

Verified in `TaxInvoiceConfiguration.cs`:

| link column | index | unique? | service guard | racy? |
|---|---|---|---|---|
| `quotation_id` (`:77`) | filtered | **no** → becomes yes (POSTED-only) in this unit | none → `quotation.already_invoiced` in this unit | fixed here |
| `sales_order_id` (`:88`) | filtered | **no** | `so.invoice_exists` (`TaxInvoiceService.cs:186-190`), all statuses | **YES — two concurrent `CreateFromSalesOrderAsync` calls both pass the read and both insert** |
| `billing_note_id` (`:82`) | filtered | **no** | `bn.ti_exists` (`:97-101`), all statuses | **YES — same shape** |
| `delivery_order_id` (`:91`) | filtered | **no** | `do.ti_exists` (`:141-145`), all statuses | **YES — same shape** |

All three siblings have the identical race and, additionally, the identical no-exit trap this design
avoids for quotations (their guards count **all** statuses, and a TI still cannot be deleted).
**Not widened here — Fable decides.** Widening them is not a copy-paste: each would need the same
`status = 'POSTED'` decision made explicitly, each would need its own duplicate pre-check on the
same crash-at-startup terms, and `bn.ti_exists` in particular interacts with
`BillingNoteService`'s many-TIs-per-BN grouping table (`billing_note_tax_invoices`) which was not
read this pass.

---

## §Design-N3 — case-insensitive tax-code lookup

### N3.1 Verdict on the Ponytail candidate: **ACCEPTED**

Load every tax code of the caller's company and drop the `codes.Contains(c.Code)` filter entirely.
The existing ignore-case dictionary then does all the work, with **zero** EF-translation risk —
there is nothing left to translate. Mechanism and exact body are already specified in **§N1.3**
(`LoadTaxCodeMasterAsync`); N3 needs no separate code.

Why it holds up:

| concern | resolution |
|---|---|
| Cardinality | 12 rows on a seeded tenant (`MasterDataServices.cs:396-411`). Even a tenant that triples its code master is a sub-millisecond seq scan of a table that is already in cache. Loaded **once per request**, not per line (the existing call sites already hoist it — `TaxInvoiceService.cs:415`, `QuotationChainServices.cs:89`, …). |
| Tenant leakage | `db.TaxCodes` carries the EF tenant filter with **no super-admin arm** since 2026-07-08 (N.8), plus RLS `company_isolation` (N.9). Loading "all" means all *of this company*. This was the one real risk of dropping the filter and it is closed by verified code, not by assumption. |
| Behaviour parity | No `IsActive` filter is added (N.20): a request naming an inactive code still matches, exactly as today. The only behavioural difference vs today is the one N3 is fixing — `exempt-book` now finds `EXEMPT-BOOK`. |
| Determinism | New: `OrderBy(TaxCodeId)` before `GroupBy(Code, OrdinalIgnoreCase)`. The unique index is `(company_id, code)` **case-sensitive** (`TaxCodeConfiguration.cs:29`), so `VAT7` and `vat7` may coexist in one company; today `g.First()` picks whichever row the server returned first. |
| Trap §9.2 (stored casing) | Untouched: the dictionary value's `Code` is still `g.Key`, which is a **master row's** casing, never the caller's. |

### N3.2 The rejected alternative, and why the EF-translatability question is moot

`UPPER(c.Code)`-style translation (`codes.Contains(c.Code.ToUpper())`, or
`EF.Functions.ILike`, or `string.Equals(..., StringComparison.OrdinalIgnoreCase)` — the last of
which does **not** translate at all in EF Core and throws at query time) is rejected because it
solves a smaller problem for more risk: it keeps a filter that provides no measurable benefit at
this cardinality, forces a functional-index discussion, and would still leave N1 needing a
**second** query to resolve `Product.DefaultOutputTaxCodeId` by id. The accepted design serves the
code lookup, the id lookup and the exempt fallback from one rowset.

Per the binding constraint, the proof obligation ("must be proven EF-translatable") applies only
if the Ponytail candidate is rejected. It is accepted, so there is nothing to prove — the query is
`Select` + `ToListAsync` with no predicate.

---

## §Test plan

House rules that apply to every test below: `[Collection(nameof(PostgresCollection))]`,
`[SkippableFact]` + `Skip.If(_fx.SkipReason is not null, _fx.SkipReason)`, a **fresh company** per
test via `TestCompanyFactory.CreateAsync(...)` (never mutate company 1), and `Today()` in the
current month so the period is open. Copy the harness from
`backend/tests/Accounting.Api.Tests/Sales/TaxCodePairIntegrityTests.cs:23-61`.

Products are created through `IProductService.CreateAsync(new CreateProductRequest(...))` resolved
from the test provider — that is the only path that can set `DefaultOutputTaxCodeId` (N.3/N.4), and
it is what the product-default test needs.

### T-N1 — new file `backend/tests/Accounting.Api.Tests/Sales/ExemptProductTaxResolutionTests.cs`

| test | setup | assertions |
|---|---|---|
| `Exempt_product_with_no_tax_code_never_charges_vat` | VAT company; product `EXEMPT_GOOD`, no `DefaultOutputTaxCodeId`; TI draft with one line carrying that `productId`, `taxCode: null`, `taxCodeId: null`, `taxRate: 0.07m` (the client lying, deliberately) | line `TaxRate == 0m`; `TaxAmount == 0m`; `TaxCode` is a code that exists in **this company's** `tax.tax_codes` with `IsExempt == true`; `TaxCodeId` equals that row's id and is **not** 0; header `TaxAmount == 0m`, `TaxableAmount == 0m`, `NonTaxableAmount == LineAmount` |
| `Exempt_product_ignores_a_taxable_code_the_caller_supplied` | same product; line sends `taxCode: "VAT7"` (a real, taxable master code) | `TaxRate == 0m`; `TaxCode != "VAT7"`; stored code `IsExempt` in master. **This is the ladder step 2c assertion — the headline of N1.** |
| `Exempt_product_ignores_a_zero_rated_code_the_caller_supplied` | same product; line sends `taxCode: "VAT-OUT-0-EXP"` | `TaxRate == 0m` **and** the stored code's master row has `IsExempt == true` (not `IsZeroRated`) — proves the ภ.พ.30 bucket decision in 2c, which a rate-only assertion would miss |
| `Exempt_product_honours_an_exempt_code_the_caller_supplied` | same product; line sends `taxCode: "EXEMPT-BOOK"` | `TaxRate == 0m`; `TaxCode == "EXEMPT-BOOK"`; `TaxCodeId` == that company's `EXEMPT-BOOK` id (ladder 2b) |
| `Exempt_product_uses_its_own_default_output_tax_code` | product `EXEMPT_GOOD` created with `DefaultOutputTaxCodeId` = this company's `EXEMPT-BOOK` id; line sends no code | `TaxCode == "EXEMPT-BOOK"`, `TaxCodeId` == that id, `TaxRate == 0m` (ladder 3) |
| `Exempt_product_ignores_a_non_exempt_product_default` | product `EXEMPT_SERVICE` with `DefaultOutputTaxCodeId` = this company's `VAT7` id; line sends no code | `TaxRate == 0m`; `TaxCode != "VAT7"`; stored code `IsExempt` (ladder 3 rejected → 4). **Proves a mis-set master row cannot charge VAT.** |
| `Taxable_product_is_unaffected_by_the_exempt_ladder` | product `GOOD`, **with** `DefaultOutputTaxCodeId` = this company's `VAT-OUT-0-EXP` (zero-rated) id; line sends no code | `TaxRate == companyVatRate` and `TaxCode == "VAT7"` — i.e. **Rule D is genuinely deferred**; the product default is *not* consulted for a taxable product. If this test fails, someone implemented Rule D. |
| `Free_text_line_claiming_exempt_type_still_charges_vat` | no `productId`; `productType: "EXEMPT_GOOD"`, no code | `TaxRate == companyVatRate`, `TaxCode == "VAT7"` — pins the §N1.2 boundary so a later reader cannot "fix" it by accident |
| `Exempt_product_on_a_non_vat_company_stays_on_the_VAT0_sentinel` | non-VAT company (`vatRegistered:false`) via a **fresh** company; exempt product on a Quotation line (a TI cannot be issued by a non-VAT company) | `TaxRate == 0m`, `TaxCode == "VAT0"`, `TaxCodeId == 0` — ladder step 1 still wins, unchanged |
| `Exempt_product_line_keeps_the_journal_balanced` | VAT company; TI with one exempt-product line + one taxable free-text line; post it | `Dr == Cr` on the produced journal entry; the output-VAT credit equals the taxable line's `TaxAmount` exactly; header `TaxableAmount + NonTaxableAmount == SubtotalAmount`. **This is the M4 invariant, asserted as an identity, not as literal numbers.** |

### T-N3 — same file (they share the harness and the master)

| test | setup | assertions |
|---|---|---|
| `Mixed_case_exempt_code_resolves_and_stores_the_master_casing` | free-text line, `taxCode: "exempt-book"` | `TaxRate == 0m`; `TaxCode == "EXEMPT-BOOK"` (the **master's** casing, trap §9.2); `TaxCodeId` == that master row's id. **Today this test is RED** — the case-sensitive `Contains` misses, the ladder falls to step 3, and the line stores `VAT7` at 7%. Write it RED first. |
| `Mixed_case_zero_rated_code_resolves_and_stores_the_master_casing` | free-text line, `taxCode: "vat-out-0-exp"` | `TaxRate == 0m`; `TaxCode == "VAT-OUT-0-EXP"`; matching id. Also RED today. |
| `Exact_case_code_still_resolves_unchanged` | `taxCode: "EXEMPT-BOOK"` | unchanged behaviour — the regression net for the loader rewrite |

### T-N2 — new file `backend/tests/Accounting.Api.Tests/Sales/QuotationSingleInvoiceTests.cs`

| test | setup | assertions |
|---|---|---|
| `Second_tax_invoice_from_an_invoiced_quotation_is_refused` | Q created → `SendAsync` → `AcceptAsync` → `CreateFromQuotationAsync` → `PostAsync`; then `CreateFromQuotationAsync` again | throws `DomainException` with `Code == "quotation.already_invoiced"`; the message contains the first TI's `DocNo` |
| `Second_tax_invoice_from_a_quotation_with_only_a_draft_is_allowed` | Q accepted → `CreateFromQuotationAsync` (leave it **Draft**) → `CreateFromQuotationAsync` again | **succeeds**, two distinct draft ids. This is the anti-trap test: if it fails, someone made the guard count drafts and re-created the no-exit state (§N2.1). |
| `A_draft_cannot_be_posted_once_a_sibling_was_posted` | Q accepted → draft A → draft B → post A → post B | posting B throws `quotation.already_invoiced` (G3), and A stays Posted with its `DocNo` intact |
| `Plain_create_with_a_quotation_id_is_guarded_too` | Q accepted → convert → post; then `CreateDraftAsync(new CreateTaxInvoiceRequest(..., QuotationId: qId))` | throws `quotation.already_invoiced` — covers **C2**, the channel the review missed |
| `Update_draft_cannot_relink_to_an_invoiced_quotation` | Q1 accepted → convert → post; separate standalone draft TI with no quotation → `UpdateDraftAsync` with `QuotationId = q1Id` | throws `quotation.already_invoiced` — covers **C3** |
| `Update_draft_can_re_save_its_own_quotation_link` | Q accepted → convert (Draft) → `UpdateDraftAsync` on that same draft with the same `QuotationId` | **succeeds** — proves the `excludeTaxInvoiceId` argument works and an ordinary draft edit is not bricked |
| `Tax_invoice_with_no_quotation_is_never_blocked` | two plain `CreateDraftAsync` calls with `QuotationId: null`, both posted | both succeed — the partial index's `quotation_id IS NOT NULL` arm |
| `Error_code_maps_to_409` (API-level, `WebApplicationFactory`) | drive `POST /quotations/{id}/create-tax-invoice` twice with the first posted | second response status is **409**, body `title`/`code` is `quotation.already_invoiced`. Per memory `webappfactory-usesetting-minimal-hosting`, override `Jwt`/`ConnectionStrings` via `UseSetting`, never `ConfigureAppConfiguration`, or every request 401s. |

Race behaviour (two concurrent posts hitting the 23505) is **not** unit-tested — it needs two
connections racing inside one test and is flaky on a shared `teas_test`. It is covered by (a) the
constraint-name-scoped catch being unit-reachable via the code path review, and (b) the deploy probe
in §N2.5 proving the unique index exists. State this in the test file's doc comment so a reviewer
does not read the gap as an oversight.

### Existing tests that MUST stay byte-for-byte green (no edits permitted)

| file | why it cannot move |
|---|---|
| `Sales/TaxCodePairIntegrityTests.cs` | every line is **free-text** (`productId: null`, `TaxCodePairIntegrityTests.cs:46, 96`) → `exemptProduct` is false → ladder path 2a/6/7, byte-identical (M2) |
| `Sales/TaxInvoiceRateDerivationTests.cs` | same — rate derivation from codes on non-product lines |
| `Sales/ChainConversionIntegrityTests.cs` | Q→SO→DO→TI conversions on free-text lines (`:118, 164`); its Q→TI test posts **nothing**, so the N2 guard (POSTED-only) cannot fire on it. **If this file goes red, the guard was implemented as all-status.** |
| `Sales/NonVatBillingTests.cs` | non-VAT company → ladder step 1, untouched |
| `Sales/DocumentChainTests.cs`, `Mcp/McpDocumentChainTests.cs`, `Mcp/McpWriteExpansionTests.cs` | BN/DO/SO→TI chain-copy paths (`deriveLineTax:false`) — out of scope by construction |
| `Persistence/PostedLineImmutabilityTests.cs` | creates `GOOD` products (`:54, 132`) → non-exempt path |
| `Hardening/Sprint10ProductTests.cs` | product CRUD only; no `DefaultOutputTaxCodeId` is set anywhere in the suite (verified: 0 hits over `backend/tests`) |

Any red in the list above is a **design violation, not a test to update**. Report it, do not edit
the test.

### Gates

- Baseline **1255 passed / 0 failed / 14 skipped** (Api) + **188** (Domain). A skip-count above 14
  means `TEAS_TEST_PG` did not survive the shell (memory `teas-test-pg-env-per-shell`) — that run is
  a fake green, not a pass.
- `TEAS_REPO_ROOT` must be set for `RbacAuthMapTests` / `RbacMatrixTests`
  (memory `teas-repo-root-rbac-tests`). No new endpoint is added in this unit, so the RBAC
  allowlist should not need touching — but run them.
- Frontend: `npx tsc --noEmit` clean (2 files touched). No new FE unit test is required; if one is
  added, `cd` into the directory first and pass a bare filename — `vitest.cmd` chokes on the
  `(dashboard)` parens (`troubles-wiki.md:1437`).
- **Never edit backend source while `dotnet test` is running** — MSB3027 file lock
  (`troubles-wiki.md:1423`).
- Backend build must be run from the **real** repo path, not a `subst` drive
  (memory `minver-subst-stamping`) — relevant because this unit generates a migration.

---

## §Implementation order + file list

Order matters: **1 → 2 → 3** are independent of the DB; **4** must be preceded by the pre-check.

### Step 1 — resolver (N1 + N3 together; they are one edit)

| # | file | change |
|---|---|---|
| 1 | `backend/src/Accounting.Infrastructure/Sales/SalesLineBackstop.cs` | `TaxCodeFlags` unchanged. Add `ProductTaxDefaults` record struct and `TaxCodeMaster` class. `LoadProductTypesAsync` → `LoadProductDefaultsAsync` (+ `DefaultOutputTaxCodeId` column). `LoadTaxCodeFlagsAsync` → `LoadTaxCodeMasterAsync` (no `Where`, three lookups, `OrderBy(TaxCodeId)` before `GroupBy`). `LoadStandardOutputTaxCodeAsync` **untouched**. `Resolve`: two parameter types change; add ladder steps 2b/2c/3/4/5 with the `// ponytail: Rule D (deferred…)` marker. Add `ExemptOutputVatCode = "EXEMPT"` const next to `StandardOutputVatCode`. **Rewrite the class doc comment and the `Resolve` ladder comment to §N1.1 verbatim** — the comment is the spec for the next reader. |

### Step 2 — call sites (mechanical; 2 lines each)

| # | file | change |
|---|---|---|
| 2 | `backend/src/Accounting.Infrastructure/Sales/QuotationChainServices.cs` | `:88-89` and `:161-162` — rename both loader calls, drop the codes argument |
| 3 | `backend/src/Accounting.Infrastructure/Sales/SalesOrderDeliveryServices.cs` | `:54-55`, `:123-124`, `:195-196`, `:352-353` — same |
| 4 | `backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs` | `:466-467` — same |
| 5 | `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.cs` | **(a) N1:** replace the inline `_db.Products` block (`:383-403`) with `LoadProductDefaultsAsync`, reuse the result for both the `ProductType` override and the `Resolve` argument at `:426-428`; delete `EmptyProductTypes` (`:29-31`); rename the tax-code loader at `:415-416`. **(b) N2:** add `EnsureQuotationNotInvoicedAsync`; call it at G1 (`CreateDraftCoreAsync`, after `EnsureVatRegisteredAsync`), G2 (`UpdateDraftAsync`, before `:488`), G3 (`PostCoreAsync`, before `MarkPosted`); add `IsQuotationInvoiceUniqueViolation` + its catch clause **first** in `PostAsync`'s wrapper (`:551-556`) |

### Step 3 — frontend (independent build; safe to do in parallel with steps 1–2)

| # | file | change |
|---|---|---|
| 6 | `frontend/components/forms/ProductPicker.tsx` | `taxRateForProductType(t, stdRate)` — see §N1.4 |
| 7 | `frontend/components/ui/LineItemsTable.tsx` | `:165` pass `stdRate` |

### Step 4 — schema (do the pre-check FIRST)

| # | file | change |
|---|---|---|
| — | *(no file)* | run §N2.5 pre-check on `teas_test` as a BYPASSRLS role; **zero rows required** before proceeding |
| 8 | `backend/src/Accounting.Infrastructure/Persistence/Configurations/Sales/TaxInvoiceConfiguration.cs` | `:77` → `.IsUnique()` + the status-filtered predicate, with the §N2.4 comment |
| 9 | `backend/src/Accounting.Api/Middleware/DomainExceptionMiddleware.cs` | one clause in `StatusFor` (`:36-37`): `.already_invoiced` → 409 |
| 10 | `backend/src/Accounting.Infrastructure/Migrations/<timestamp>_QuotationSingleInvoice.cs` | **generated** — `dotnet ef migrations add QuotationSingleInvoice`, run from the real repo path |
| 11 | `…_QuotationSingleInvoice.Designer.cs` | generated, unedited |
| 12 | `backend/src/Accounting.Infrastructure/Migrations/AccountingDbContextModelSnapshot.cs` | regenerated, unedited |

### Step 5 — tests

| # | file | change |
|---|---|---|
| 13 | `backend/tests/Accounting.Api.Tests/Sales/ExemptProductTaxResolutionTests.cs` | **new** — T-N1 + T-N3 |
| 14 | `backend/tests/Accounting.Api.Tests/Sales/QuotationSingleInvoiceTests.cs` | **new** — T-N2 |

**14 files. Revised blast cap: 15** (one slot of slack). Files 10–12 are EF-generated. Hitting 16
= stop and re-spec.

**Explicitly out of bounds** (touching any of these = stop): `SalesCategorizer.cs`,
`TaxFilingService.cs`, `ProportionalInputVatService.cs`, `ProductService.cs`, `ProductDtos.cs`,
`frontend/app/(dashboard)/settings/products/page.tsx`, `ProductSearchModal.tsx`,
`ProductQuickCreateModal.tsx`, any purchase-side service, any `TaxInvoiceLine`/`*Line` entity or
configuration (a nullable line column would be a five-table migration — Unit A §11), and any
existing test file.

---

## §Conflicts / Deviations — Fable ratifies before dispatch

Three places where this design departs from the literal wording of the binding constraints. Each is
forced by verified code, not by preference. **Fable ratifies or overrides before dispatch.**

| # | binding constraint said | design does | forced by | if overridden |
|---|---|---|---|---|
| **D1** | "filtered UNIQUE index on `tax_invoices.quotation_id WHERE quotation_id IS NOT NULL`" | adds `AND status = 'POSTED'` to the filter | N.12 — a TI has no delete, cancel or void path, so an all-status unique index makes one abandoned draft consume a quotation permanently; Unit A §3.4 refused this exact guard for this exact reason and nothing has changed | the all-status version needs a **new `DELETE /tax-invoices/{id}` draft-delete route + service method + permission + FE button** as its exit (≈4 more files, and it re-opens Unit A escalation E4). Say the word and I re-spec; do **not** let an implementer ship the all-status index without that exit. |
| **D2** | "Service guard mirroring the SO sibling … when a TaxInvoice already references the quotation" | the guard counts only **Posted** TIs, and lives in `CreateDraftCoreAsync` rather than in `CreateFromQuotationAsync` | same as D1, plus N.15 — `QuotationId` arrives through the request on three channels, so the sibling's per-method placement would leave two of them open | keeping the sibling's shape leaves `POST /tax-invoices {quotationId}` and `update_tax_invoice_draft` unguarded — the review's finding would only be half-fixed |
| **D3** | siblings' `*_exists` codes are 422; the constraint offered "the `.locked_mismatch` family or a designed equivalent" | new suffix `.already_invoiced` → **409** for both the guard and the race | a race is a conflict; making guard and race share one code means the client has one branch. Verified no existing code ends in `.already_invoiced`, so no existing status moves | trivially revertible: drop the `StatusFor` clause and the code falls back to 422. Nothing else in the design depends on the status. |

Also for the record, not a deviation: the constraint asked whether GOOD/SERVICE products with a
`DefaultOutputTaxCodeId` should resolve through it ("probably yes"). The design **defers** that
(Rule D, §N1.2) with the reasoning and the exact 3-line insertion point. If Fable wants it in this
unit, it pulls in 4 more files (`ProductDtos.cs`, `ProductService.cs`, `ProductPicker.tsx`,
`ProductSearchModal.tsx`) plus the product-settings fix, and the blast cap goes to ~20.
