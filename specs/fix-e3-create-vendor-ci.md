# fix-e3-create-vendor-ci

## Symptom
`McpServerSmokeTests.E3_create_vendor_returns_id_code_name` fails at
`result.IsError.Should().NotBe(true)` (line 1233), including in complete
isolation (`--filter FullyQualifiedName~E3_create_vendor_returns_id_code_name`).
Failing since ~v1.21.5 era, across unrelated diffs (troubles-wiki.md L591-595).

## Actual error text (verbatim, captured via `--logger "console;verbosity=detailed"`)
```
warn: Accounting.Api.Mcp.McpErrorSurfacingFilter[0]
      "create_vendor" rejected: [mcp.validation] TaxId: vendor.vat_registered_requires_taxid; TaxId: Invalid Thai Tax ID (13 digits + checksum).
      FluentValidation.ValidationException: Validation failed:
       -- TaxId: vendor.vat_registered_requires_taxid Severity: Error
       -- TaxId: Invalid Thai Tax ID (13 digits + checksum). Severity: Error
         at ... FluentValidation.AbstractValidator`1.RaiseValidationException(...)
         at ... Accounting.Api.Mcp.TeasMcpTools.CreateVendorAsync(...) in TeasMcpTools.cs:line 968
```
The MCP error-surfacing filter (WP1.13, 2026-07-13) is working as designed —
it correctly surfaced the FluentValidation failure as `IsError=true` with the
real message in `TextContentBlock`. The test just never read that payload
(only asserted `IsError != true`), so the actual cause was invisible until
now.

## Root cause (evidence)
- `git log --oneline -- backend/src/Accounting.Application/Master/VendorDtos.cs`
  shows commit `65b9b2b` "feat(purchase): WP1 money/compliance — non-VAT
  non-recoverable, percent-UI, category backfill (F13/F15/F20/F27)" added a
  new `CreateVendorValidator` rule (VendorDtos.cs:62-66):
  ```csharp
  RuleFor(x => x.TaxId)
      .NotEmpty().WithMessage("vendor.vat_registered_requires_taxid")
      .Must(t => ThaiTaxId.TryParse(t, out _))
      .WithMessage("Invalid Thai Tax ID (13 digits + checksum).")
      .When(x => x.VatRegistered && !x.IsForeign);
  ```
  A domestic (`IsForeign=false`) `VatRegistered=true` vendor now MUST carry a
  valid 13-digit Thai Tax ID (checksum-validated) — needed to claim input VAT
  (ภ.พ.30). This is a deliberate, correct business rule (F13), independently
  unit-tested in `Hardening/VendorVatTaxIdValidatorTests.cs` (added in the
  same WP1 commit, still green).
- The E3 test (`McpServerSmokeTests.cs`, first introduced in `06fc16f`, LONG
  before WP1/65b9b2b existed) sends `vendorType/vatRegistered = true` with
  `taxId = (string?)null` and no `isForeign` (defaults false). This request
  shape was valid when the test was written but has been invalid since WP1
  landed (2026-07-14) — a stale test fixture, not a product regression.
  Confirmed: request has `IsForeign` unset (defaults to `false` per
  `CreateVendorRequest`'s optional param), so the `!x.IsForeign` guard does
  not exempt it.
- Classification: **test/fixture bug** — fix the test, not the product.
  `create_vendor`'s validation-rejection behavior itself is correct and
  covered by its own dedicated unit tests.

## Fix applied
`backend/tests/Accounting.Api.Tests/Mcp/McpServerSmokeTests.cs`: E3 request's
`taxId` changed from `(string?)null` to `"0105556123453"` — the same
mod-11-valid Thai Tax ID constant already reused across ~19 other files in
this test suite (Sprint55/Sprint87 vendor seeds, `TestCompanyFactory`,
`VendorVatTaxIdValidatorTests.ValidTaxId`, several SQL seed scripts). No new
constant introduced; matches established convention.

## Attempt log
1. Read troubles-wiki.md L591-595 (E3 entry, unconfirmed root cause) + grepped
   wiki for other vendor/create_vendor/McpServerSmokeTests hits — no other
   relevant entries.
2. Read E3 test body (McpServerSmokeTests.cs:1210-1241) and `CreateVendorAsync`
   MCP tool handler (TeasMcpTools.cs:960-971) + `VendorService.CreateAsync`
   (MasterDataServices.cs:51-78) — no obvious bug, request looked plausible.
3. Ran isolated repro with `--logger "console;verbosity=detailed"` to capture
   the actual `TextContentBlock` error instead of guessing — surfaced the
   FluentValidation message above on first try.
4. Traced `vendor.vat_registered_requires_taxid` to `VendorDtos.cs:62-66`
   (`CreateVendorValidator`), git-blamed to WP1 commit `65b9b2b`
   (2026-07-14) — postdates the E3 test's introduction (`06fc16f`).
5. Confirmed the rule is intentional + independently tested
   (`VendorVatTaxIdValidatorTests.cs`, same commit) → root cause is stale test
   data, not a product bug. In scope per dispatch (test/fixture fix).
6. Found the established valid-TaxId convention (`"0105556123453"`, reused
   19× across the suite) via `VendorVatTaxIdValidatorTests.cs`'s own comment
   ("Sprint55/Sprint87 vendor seeds, TestCompanyFactory's demo customer") —
   reused it rather than inventing a new checksum-valid string.
7. Applied one-line fix. Gate: isolated E3 rerun + full
   `McpServerSmokeTests` class rerun (see EVIDENCE in worker report).

## Checklist
- [x] Root-caused with verbatim error text (not guessed)
- [x] Fix classified (test/fixture, not product) with evidence
- [x] Minimal fix applied (1 line, reuses existing convention)
- [x] E3 test green in isolation — `dotnet test ... --filter "FullyQualifiedName~E3_create_vendor_returns_id_code_name"` → `Passed: 1, Failed: 0`
- [x] Full `McpServerSmokeTests` class green — `--filter "FullyQualifiedName~Accounting.Api.Tests.Mcp.McpServerSmokeTests"` → `Total tests: 36, Passed: 36, Failed: 0`
- [x] Full `Accounting.Api.Tests` suite green — `Passed: 897, Failed: 0, Skipped: 8, Total: 905` (baseline was 890 pass / 8 skip / 1 fail / 899 total; skip count matches exactly, failures dropped 1→0, pass count grew — expected drift from other work landed on main since the baseline was recorded, not a regression introduced here)
- [x] troubles-wiki.md E3 entry (L591-595 originally) updated with confirmed root cause + fix + lesson
