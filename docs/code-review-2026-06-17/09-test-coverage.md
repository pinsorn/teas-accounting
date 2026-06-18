# Test Coverage & Quality Review — TEAS — 2026-06-17

## Summary

**Overall posture: GOOD with targeted gaps.** The suite is unusually disciplined for an active-build
project: TestIds.* discipline is consistently applied, no skipped/disabled tests were found,
compliance citations (`ม.XX`) appear in test file summaries where legally required, and the
critical doc-number, GL-balance, WHT, and multi-tenant paths have solid integration coverage.
The gaps are concentrated in two areas: (a) explicit ม.86/4 eight-field validation on the
sales-side TI (no test asserts all 8 statutory fields are present on a produced TI), and (b)
CN/DN post-flow GL balance (no backend integration test asserts the credit/debit note journal
entry balances). Secondary flakiness risk exists in several tests that hardcode calendar
periods (2026-xx) that will interact with the period-close subsystem on a populated shared DB.

**Counts by severity:**
- Critical: 0
- High: 3
- Medium: 4
- Low: 2

**Compliance-path coverage checklist:**

| Path | Status | Evidence |
|---|---|---|
| Tax Invoice 8-field presence (ม.86/4) | ✗ MISSING | No test walks all 8 statutory fields on a produced TI |
| Posted-doc immutability — TI edit/delete rejected | ✗ MISSING | No endpoint-level PUT/DELETE-on-posted-TI test found |
| Posted-doc immutability — e-Tax XAdES mutation blocked | ✓ | Sprint13cEtaxPipelineTests.cs:269 `.Contain("immutable")` |
| Doc-number sequential/gap-free | ✓ | Sprint1HardeningTests:65 + :197 |
| Doc-number assigned on POST not DRAFT | ~PARTIAL | PO state machine sets DocNo on approval; TI draft→post tested implicitly via Sprint55; no explicit null-DocNo-on-draft assertion |
| Voided numbers not reused | ✗ MISSING | No test for this sub-rule |
| Multi-tenant isolation (company A cannot read B) | ✓ | CrossCompanyBuIsolationTests.cs:117 |
| VAT math — exclusive 7% | ✓ | Sprint55VendorInvoiceTests:119 `.Be(70m)` |
| VAT math — inclusive 7/107 | ✗ MISSING | IsTaxInclusive path in TaxInvoiceService:358 has no dedicated test |
| VAT rate server-side backstop (caller cannot lie) | ✓ | TaxInvoiceRateDerivationTests:154 |
| PV per-line VAT — ม.81 exempt product | ✓ | Sprint87ForeignVendorTests:301,330 |
| PV per-line VAT — ม.82/5 non-recoverable lump | ✓ | Sprint55VendorInvoiceTests:130 |
| WHT 50ทวิ issued on post | ✓ | Sprint1HardeningTests:182 |
| GL double-entry balance | ✓ | Sprint9FinancialReportTests:82; PayrollRunServiceTests:143 |
| Payroll SSO math | ✓ | PayrollMathTests; PayrollRunServiceTests:35-41 |
| Progressive PIT | ✓ | ThaiPitCalculatorTests:22 |
| CN VAT-rate derivation (not trusting caller) | ✓ | TaxInvoiceRateDerivationTests:163 |
| CN/DN GL journal balanced | ✗ MISSING | No backend test found; only e2e smoke via credit-note-corrects-tax-invoice |
| ภ.ง.ด.50/51 CIT filing | ✓ | CitCalculatorTests; Pnd50FilingServiceTests; multiple |
| ภ.พ.30 VAT return finalize + idempotency | ✓ | Sprint9VatComplianceTests:146,153 |
| ม.82/4 input-VAT claim window | ✓ | Sprint55VendorInvoiceTests:188,210 |
| Period-close gate on post | ✓ | Sprint55VendorInvoiceTests:218 |

---

## Findings

---

### [HIGH-1] No test validates all 8 ม.86/4 statutory fields are present on a produced Tax Invoice

**File:** `backend/tests/Accounting.Api.Tests/Sales/InvoiceFlowTests.cs`  
**Confidence:** [Confirmed]  
**Why it matters:** ม.86/4 mandates 8 specific fields on every Tax Invoice (seller name/address/TaxID/branch,
buyer name/address/TaxID/branch when VAT-registered, sequential doc number, per-line description/qty/value,
VAT shown separately from goods value, issue date, and prominent "ใบกำกับภาษี" label). A TI missing any
field exposes the company to penalty under ม.90(3).

**Evidence:** `InvoiceFlowTests.cs` creates and posts TIs (lines 90–208) and asserts structural copy
fields (`CustomerId`, `BillingNoteId`, line counts, subtotal), but contains no assertion that checks:
`SellerTaxId`, `SellerAddress`, `BuyerTaxId`/`BuyerBranchCode` (required for VAT buyer), the
"ใบกำกับภาษี" label on the PDF/DTO, or that `VatAmount` is stored separately from `SubtotalAmount`.
`OnboardingFoundingAddressTests.cs:73` only checks the company-profile row exists, not that it flows
into TI output. `TaxInvoiceRateDerivationTests` checks rates but not field completeness.

```csharp
// InvoiceFlowTests.cs:137-141 — asserts copy, not 8-field completeness
ti.BillingNoteId.Should().Be(invId);
ti.CustomerId.Should().Be(cust);
ti.Lines.Should().HaveCount(1);
ti.Lines.Single().DescriptionTh.Should().Be("งานทดสอบ");
ti.Lines.Single().Quantity.Should().Be(3m);
// SellerTaxId, BuyerTaxId, BuyerBranch, label, VatSeparate — NOT asserted
```

**Fix:** Add one integration test (or extend `InvoiceFlowTests`) that posts a TI for a VAT buyer and
asserts: `SellerTaxId != null`, `SellerBranchCode == "00000"`, `BuyerTaxId != null` (for VAT buyer),
`VatAmount > 0 && SubtotalAmount != TotalAmount`, `DocNo != null` (assigned on post not draft). Cite
`// ม.86/4 #2,#3,#6` in the assertion block.

---

### [HIGH-2] No backend integration test for CN/DN GL journal balance after post

**Files:** `backend/tests/Accounting.Api.Tests/Sales/` — no CN/DN GL balance test found  
**Confidence:** [Confirmed]  
**Why it matters:** Credit Notes and Debit Notes generate journal entries that must satisfy the
double-entry invariant. The only CN post-flow evidence is a Playwright e2e smoke test
(`credit-note-corrects-tax-invoice.spec.ts:6`) against seed data — it does not assert JE totals.
If a CN/DN GL branch is silently broken, no integration gate catches it.

**Evidence:** Searching all backend test files for `CreditNote.*GL`, `DebitNote.*GL`,
`credit_note.*journal`, and `note.*balance` returns zero results. The RBAC inventory lists CN/DN
endpoints (`RbacEndpointInventory.cs:56-67`), but only RBAC gate tests exercise them — no test calls
`PostAsync` on a CN and then asserts `TotalDebit == TotalCredit`. The pattern used for TI (Sprint55
line 125: `je.TotalDebit.Should().Be(je.TotalCredit)`) is absent for CN/DN.

**Fix:** Add an integration test in `Sales/` that creates a posted TI, issues a CN against it, posts
the CN, then reads the resulting journal entry and asserts `je.TotalDebit == je.TotalCredit`. Mirror
the Sprint55/Sprint86 pattern. Cite `// ม.86/9 + double-entry invariant`.

---

### [HIGH-3] Tax-inclusive 7/107 VAT path (`IsTaxInclusive = true`) has no dedicated test

**File:** `backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.cs:350-358`  
**Confidence:** [Confirmed]  
**Why it matters:** The 7/107 formula is the legally correct extraction for VAT-inclusive prices
(ม.79/5). If the `inclusive && input.TaxRate > 0` branch computes incorrectly, every inclusive-price
TI carries a wrong VAT amount — an RD audit failure.

**Evidence:** `TaxInvoiceService.cs:20` documents "7/107 convention for inclusive prices" and
`TaxInvoiceService.cs:358` shows the branch `if (inclusive && input.TaxRate > 0)`. No test file
contains `IsTaxInclusive`, `7/107`, `TaxInclusive`, or the numeric `107m`. The existing VAT math
tests all use exclusive prices (e.g., Sprint55:119 `VatAmount.Should().Be(70m)` where amount × 7% = 70
implies exclusive basis).

```csharp
// TaxInvoiceService.cs:350-358 — untested branch
private static TaxInvoiceLine BuildLine(TaxInvoiceLineInput input, int lineNo, bool inclusive)
{
    ...
    if (inclusive && input.TaxRate > 0)   // <── no test covers this path
        // 7/107 extraction
```

**Fix:** Add a unit or integration test with `IsTaxInclusive = true`, amount = 10700, taxRate = 0.07.
Assert `SubtotalAmount = 10000m`, `VatAmount = 700m`. Cite `// ม.79/5 (7/107 inclusive extraction)`.

---

### [MEDIUM-1] Hardcoded `VatClaimPeriod = 202605` in ApAgingTests could collide with period-close on a reseeded or mature DB

**File:** `backend/tests/Accounting.Api.Tests/Reports/ApAgingTests.cs:61`  
**Confidence:** [Confirmed]  
**Why it matters:** Per CLAUDE.md §8 and the memory note `relative-date-seed-temporal-tests.md`, the
period-close seed closes `prev-month` per `CURRENT_DATE`. If May 2026 is already closed on the shared
`teas_test` and a query or trigger checks `vat_claim_period` for validity, seeds with `202605` may
be rejected on future runs.

**Evidence:**
```csharp
// ApAgingTests.cs:61 — hardcoded period
VatClaimPeriod = 202605,
```
The AP aging query itself is read-only and may never validate `VatClaimPeriod`, but the seed inserts
directly into `vendor_invoices` and the value is frozen in time. The same pattern appears in
`PurchaseChainServiceTests.cs:75` (`VatClaimPeriod = 202605`).

**Fix:** Replace hardcoded `202605` with a comment `// VatClaimPeriod is not asserted by this test; any non-null value suffices` and use `TestIds.FuturePeriod()` or just a placeholder known-open period derived from the current date, consistent with how `Sprint9VatComplianceTests:141` uses `TestIds.FuturePeriod()`.

---

### [MEDIUM-2] Sprint6VatRegisterTests uses only hardcoded 2026 dates — no TestIds.FuturePeriod guard

**File:** `backend/tests/Accounting.Api.Tests/Hardening/Sprint6VatRegisterTests.cs:108-179`  
**Confidence:** [Confirmed]  
**Why it matters:** This test seeds four VIs with explicit dates in March/April/June/July 2026 and
queries by those exact months. Since the VAT register filters by `vat_claim_period`, these tests
pass only if those periods are open on `teas_test`. The comment in `Sprint55VendorInvoiceTests:236`
acknowledges this pattern ("tolerate an already-closed period") but Sprint6VatRegisterTests has no
such guard.

**Evidence:**
```csharp
// Sprint6VatRegisterTests.cs:108-110 — all hardcoded to 2026
new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 1), 202603);
new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 1), 202604);
// no TestIds.FuturePeriod, no period-closed guard
```
`grep -n 'FuturePeriod|TestIds' Sprint6VatRegisterTests.cs` returned no output.

**Fix:** Refactor to derive test months from `DateOnly.FromDateTime(DateTime.Today)` and use at most
`+1` month offsets to ensure they stay in the future (open). Alternatively add a `try/catch` guard
similar to Sprint55:237 for each seeded period. The register query does not depend on a specific
calendar year, only on relative period assignment.

---

### [MEDIUM-3] Posted TI edit/delete rejection is not tested at the API endpoint layer

**File:** No HTTP PUT/DELETE-on-posted-TI test found  
**Confidence:** [Confirmed]  
**Why it matters:** ม.86/4 + CLAUDE.md §4.2 state that a posted TI is immutable. The e-Tax mutation
guard is tested in `Sprint13cEtaxPipelineTests:269` (service layer, checks `.Contain("immutable")`),
but there is no HTTP-level test that sends a `PUT /tax-invoices/{id}` or `DELETE /tax-invoices/{id}`
against a posted TI and asserts a 409/403/422 response. If the endpoint layer lacks the guard, a
direct API call could bypass the service check.

**Evidence:** Searching production endpoints for `MapPut.*tax` and `MapDelete.*tax` returned no
results — suggesting TI may not expose update/delete endpoints at all. But if they exist (via a
catch-all or partial-update route), there is no test. Search of tests for `UpdateInvoice`, `PatchInvoice`,
`put.*invoice`, `StatusCode.*409` on TI context returned no matches. The only immutability assertion
found is `Sprint13cEtaxPipelineTests.cs:269` which is service-layer only.

**Fix:** Confirm whether `PUT /tax-invoices/{id}` exists; if it does, add an endpoint test asserting
`4xx` when TI status is Posted. If the endpoint intentionally does not exist, add a comment in
`RbacEndpointInventory` or add a test that `GET /tax-invoices/{id}` on a posted TI returns the read
DTO without a corresponding writable endpoint exposed (inventory check).

---

### [MEDIUM-4] Doc-number assigned on POST not DRAFT — no explicit null-DocNo assertion on draft state

**File:** No test found asserting `DocNo == null` on a freshly-created draft  
**Confidence:** [Confirmed]  
**Why it matters:** CLAUDE.md §4.3: "Number assigned only on POST/Issue, never on Draft." The
Sprint1HardeningTests gapless/concurrency tests (line 65) validate the number-sequence allocation
mechanism, but none of the TI or VI draft-creation tests explicitly asserts `DocNo == null` before
post. If a code change accidentally assigns a number on `CreateDraftAsync`, no test would catch it.

**Evidence:** `grep 'DocNo.*null|null.*DocNo|Draft.*DocNo' tests/ *.cs` returned no output. The
closest is `PurchaseOrderStateMachineTests:22` which asserts DocNo is set on approval — this is the
positive case. There is no corresponding negative assertion (draft has no DocNo).

**Fix:** Add one line to any existing draft-creation test: `draft.DocNo.Should().BeNullOrEmpty("draft must not consume a number before post — ม.86/4 §4.3")`. The Sprint55 test (line 91-118) creates a draft and posts it but does not read the draft DTO back to check DocNo.

---

### [LOW-1] Voided-number-not-reused rule has no integration test

**File:** No test found  
**Confidence:** [Confirmed]  
**Why it matters:** CLAUDE.md §4.3 states voided numbers stay (status VOIDED), never reused. The
`v_number_gaps` view is tested against rolled-back allocations (Sprint1:233-238) but not against
a VOID status. A voided TI's sequence slot should appear in `v_number_gaps` only if voiding itself
creates a gap — the intent is that VOIDED entries hold the slot, preventing a gap, but there is no
test that confirms a VOIDED doc prevents reuse of that sequence number.

**Fix:** Add a test: allocate number N, mark the doc VOIDED, allocate another doc and assert it gets
N+1 (not N again). This validates that VOIDED docs hold their position in the sequence.

---

### [LOW-2] E2e `billing-note-flow.spec.ts` uses `test.skip` conditioned on seed state — silent false-green risk

**File:** `frontend/e2e/billing-note-flow.spec.ts:50`  
**Confidence:** [Confirmed]  
**Why it matters:** The test silently skips if no posted TI exists for the chosen customer in seed.
On a fresh DB or after a co2 reseed (noted in memory `co2-demo-loadbearing-pl-polluted.md`), this
test will always skip rather than fail — a false-green.

**Evidence:**
```typescript
// billing-note-flow.spec.ts:50
test.skip(available === 0, 'no posted TI for this customer in seed');
```

**Fix:** Either (a) seed a posted TI as part of the test setup (programmatically via API) so the skip
condition is never true in CI, or (b) change `test.skip` to `expect(available).toBeGreaterThan(0)`
with a descriptive error so CI fails loudly rather than silently skipping.

---

## Verified WELL-TESTED (areas with solid coverage)

- **Doc-number sequential + gap-free + concurrency:** `Sprint1HardeningTests:65,197` — two separate
  facts test both gapless allocation under concurrency and that a rolled-back allocation leaves no gap,
  querying the `v_number_gaps` view. [Confirmed excellent]

- **GL double-entry balance:** Asserted post every material post-flow in `Sprint55`, `Sprint6`, `Sprint86`,
  `Sprint87`, `Sprint8BusinessUnitTests`, `Sprint9FinancialReportTests:82`, and `PayrollRunServiceTests:143`.
  The trial-balance `Balanced` property provides a catch-all. [Confirmed excellent]

- **WHT 50ทวิ certificate issuance:** `Sprint1HardeningTests:182` verifies certificate is issued when
  WHT > 0. [Confirmed]

- **VAT rate server-side backstop:** `TaxInvoiceRateDerivationTests` (cases 1–7) systematically covers
  rate derivation across company VAT modes, DO-→TI rate override, and CN/DN inheriting from original TI.
  Caller cannot supply a wrong rate. [Confirmed]

- **ม.81 exempt-product VAT guard:** `Sprint87ForeignVendorTests:301,330` — reject VAT on exempt product
  and accept zero-VAT on exempt product. `Sprint9VatComplianceTests:71` validates `ม.81(1)(ข)` category
  and legal ref. [Confirmed]

- **Multi-tenant isolation:** `CrossCompanyBuIsolationTests:117` — super-admin accessing company-B BU
  list is still scoped to company-B only. [Confirmed]

- **Progressive PIT + SSO math:** `ThaiPitCalculatorTests` (5 facts/theories), `PayrollMathTests`,
  `PayrollRunServiceTests` (pinning SSO statutory params). [Confirmed]

- **Period-close / ม.82/4 window:** `Sprint55VendorInvoiceTests:190,218` cover the +6-month window and
  the closed-period rejection with next-open hint. [Confirmed]

- **TestIds discipline:** Broadly applied across all integration tests. Zero hardcoded
  customer/vendor/product codes found (`grep` returned only TestIdsTests.cs self-test). [Confirmed]

- **No skipped/disabled tests:** No `[Fact(Skip=...)]`, `[Theory(Skip=...)]`, or `[Ignore]` attributes
  found anywhere in the backend test suite. [Confirmed]

- **ภ.ง.ด.50/51 CIT:** Deep coverage across `CitCalculatorTests`, `CitLossCarryForwardTests`,
  `Pnd50FilingServiceTests`, `Pnd50LadderTests`, `Pnd50ScheduleTests`, `Pnd50SheetTests`,
  `Pnd50BalanceSheetMapTests`, `Pnd51WorksheetTests`, `Pnd51FilingServiceTests`. [Confirmed]
