# U1 — L1-1 payer tax ID in filings (PLAN-fix-findings-r2.md)

Ham GO 2026-08-19. Route: Sonnet implements + Opus reviews (proven in-repo pattern, ladder #4).

## Root cause
Seed 637 repaired `master.companies.tax_id` (co1 placeholder `0000000000000` →
`0105000000012`) but `master.company_profile.tax_id` (co1) still holds the placeholder.
`Pnd1FilingService`/`SsoFilingService` resolve `EmployerTaxId: prof?.TaxId ?? c?.TaxId ?? ""`
— `??` only substitutes on NULL, and `prof.TaxId` is a non-null placeholder string, so the
repaired `companies.tax_id` fallback never fires. Confirmed live via rendered ภ.ง.ด.1 PDF
(findings-r2/findings-leg1.md, L1-1): boxed tax-ID digits render as 13 zeros.

## DB verification (accounting_dev + teas_test, read-only psql)
- `master.companies`: co1=0105000000012 (repaired by 637), co2=0000000000002,
  co3=0000000000003, co4=0100555123455 (real). co2/co3 were NEVER touched by 637 (its WHERE
  clause is the literal string `0000000000000`, which only co1 ever held) — not a defect,
  out of scope for this unit.
- `master.company_profile`: co1=**0000000000000** (still placeholder — the bug), co2=0000000000002,
  co3=0000000000003, co4=0100555123455. Only co1 is desynced from its own `companies` row.
- Neither table has RLS enabled (`pg_class.relrowsecurity = false` for both, verified via psql)
  and `company_profile.tax_id` carries no UNIQUE index or CHECK constraint (only NOT NULL) —
  confirmed via `pg_constraint` / `\d master.company_profile`.

## Design decisions (advisor-reviewed before implementation)
1. **Seed 638** mirrors 637's WHERE clause literally: `UPDATE master.company_profile SET
   tax_id = '0105000000012' WHERE tax_id = '0000000000000'`. SYSTEM script (not added to
   `DbInitializer.DemoScripts` — company-agnostic, no-op on a DB where no row holds the
   placeholder). Unlike 637, does NOT claim "at most one row" (no unique index on this
   column) — states the multi-row possibility and why it's harmless (any match is by
   definition a never-filled placeholder).
2. **Guard helper**: new `Accounting.Infrastructure.Payroll.PayerTaxIdRules` (static class,
   sits next to `FilingNameRules.cs` — established precedent for this namespace), mirrors
   `PaymentVoucherService.IsUsablePayerTaxId` (F10) **verbatim** (blank or all-digits-zero;
   deliberately NOT a checksum check — same F10 comment reasoning, "short" is not separately
   enforced beyond "blank"). **NOT literally shared** with `PaymentVoucherService.cs` —
   duplicated 3-line predicate, because touching Purchase-module code is outside U1's scope
   (dispatch: "Do NOT touch source beyond this unit's scope"). Flagged here for Opus/Fable:
   consolidating into one cross-module implementation is a legitimate follow-up, not done here.
   Error code: `filing.payer_tax_id_missing` (per dispatch's suggested family — fits both
   `IPnd1FilingService`/`ISsoFilingService`, which are literally named "*FilingService*").
3. **Guard placement — Pnd1FilingService**: inline in `BuildPnd1MonthlyAsync` and
   `BuildPnd1aAnnualAsync`, right after resolving `employerTaxId`, before building the model.
   These two methods return the PDF bytes directly — no separate preview step exists, so
   guarding here is unavoidable and correct. (`BuildEmployeeWht50TawiAsync` line 168 has the
   IDENTICAL fallback pattern but is **explicitly excluded** from the dispatch's 3 named call
   sites — NOT guarded here; flagged in the final report for Fable to triage as a follow-up.)
4. **Guard placement — SsoFilingService**: in the two artifact producers
   (`BuildMonthlyPdfAsync`, `BuildMonthlyFileAsync`), **NOT** in `BuildMonthlyAsync` itself —
   mirrors the existing `EnsureEmployerAccount`/`EnsureNamesFilable` precedent in the same
   file (H8/H9), which deliberately guards only the artifacts so the on-screen สปส.1-10
   schedule keeps rendering (pinned by test `T15_sso_pdf_refuses_missing_employer_account_
   but_sso_schedule_still_renders`). The dispatch's "Sso ×1" phrasing is compatible with this
   (one bug/resolution location at BuildMonthlyAsync:69; enforcement follows the file's own
   established pattern rather than introducing a new one). Documented here explicitly for the
   Opus reviewer per advisor guidance — this is a placement judgment call, not a literal
   line-69 guard.

## Checklist
- [x] Read seed 637, Pnd1FilingService.cs, SsoFilingService.cs, F10 precedent
      (PaymentVoucherService.cs:535-536,685-691), WhtPayerTaxIdGuardTests.cs,
      PayrollRunServiceTests.cs (T14-T17 SSO guard patterns), TestCompanyFactory.cs.
- [x] DB-verified accounting_dev + teas_test state (above).
- [x] Advisor design review — confirmed plan (helper NOT shared with PV, Sso guard at
      artifact builders not BuildMonthlyAsync, seed WHERE clause literal, RED-before-638
      sequencing).
- [x] Write tests FIRST (before creating 638 / touching services) — capture RED:
  - [x] (a) T21: all-zero PROFILE tax id (companies.TaxId real) → Pnd1 monthly + Pnd1a annual +
        Sso file + Sso PDF all throw `filing.payer_tax_id_missing`. Pre-fix: RED —
        "Expected a DomainException to be thrown, but no exception was thrown" (no guard exists).
  - [x] (b) T22: company 1, post-seed-638, files clean with `0105000000012` (Pnd1 monthly PDF
        digit-extraction + Pnd1a annual + Sso model.EmployerTaxId). Pre-fix: RED — digit-stripped
        PDF text does not contain "0105000000012" (still renders the 0000000000000 placeholder,
        confirmed by the raw failure diff showing all-zero runs of digits in the boxed field).
  - [x] (c) T23: profile row ABSENT (deleted) → falls back to `c?.TaxId` (real) → builds
        successfully, no throw. PASSED pre-fix too (regression pin, no guard yet to interfere) —
        expected, not a defect in the test.
- [x] Capture RED output in this file (below).
- [x] Create `PayerTaxIdRules.cs`.
- [x] Guard Pnd1FilingService (2 sites: BuildPnd1MonthlyAsync, BuildPnd1aAnnualAsync).
- [x] Guard SsoFilingService (2 artifact-builder sites: BuildMonthlyPdfAsync, BuildMonthlyFileAsync).
- [x] Create seed 638.
- [x] Re-run tests → GREEN, capture output (below).
- [x] Filtered test run (Payroll area + F10/diagnostic) — 0 failed, 12 skipped (all
      `TEAS_DIAG=1`-gated diagnostic opt-ins, unrelated to this change — 0 vs the dispatch's 14
      full-suite baseline is not comparable; this is a scoped subset, not the full suite).
- [x] Final report: files changed, evidence, BuildEmployeeWht50TawiAsync flag.

### GREEN capture (2026-08-19, post-fix)
`TEAS_TEST_PG=... dotnet test --no-build --filter "FullyQualifiedName~PayrollRunServiceTests.T21|...T22|...T23"`
→ `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 8 s`

psql confirms seed 638 applied and company 1 repaired on teas_test:
```
 company_id |    tax_id
------------+---------------
          1 | 0105000000012
                  script_name
------------------------------------------------
 638_repair_all_zero_company_profile_tax_id.sql
```

Regression check — full `PayrollRunServiceTests` class (50 tests, includes T14-T20's
pre-existing SSO/Pnd1 guard tests and the byte-identity filing tests against company 1):
`Passed! - Failed: 0, Passed: 50, Skipped: 0, Total: 50, Duration: 29 s`

Wider filing-area regression check (`Payroll` namespace + F10's `WhtPayerTaxIdGuardTests` +
`TaxFormFillDiagnostic`): `Passed! - Failed: 0, Passed: 73, Skipped: 12, Total: 85, Duration: 47 s`
— the 12 skips are all `TEAS_DIAG=1`-gated diagnostic/visual-emit tests (`Skip.If(Environment.
GetEnvironmentVariable("TEAS_DIAG") != "1", ...)`), unconditionally skipped without that opt-in
flag, unrelated to this change. `WhtPayerTaxIdGuardTests` (F10, untouched `PaymentVoucherService`)
still passes — confirms `PayerTaxIdRules` being a deliberate duplicate rather than a shared
extraction caused no collateral there.

### Follow-ups flagged for Fable/Opus
1. ~~`Pnd1FilingService.cs:168` (`BuildEmployeeWht50TawiAsync`'s `PayerTaxId` fallback)~~ —
   RESOLVED. Fable scope-ruled it IN SCOPE (2026-08-19): same defect class as the three named
   call sites, now guarded with T24. See "Extension" section below.
2. **Still not done, still out of U1's scope (Fable's instruction on the extension):**
   `PayerTaxIdRules.IsUsable`/`EnsureUsable` duplicates `PaymentVoucherService.
   IsUsablePayerTaxId` (F10) verbatim rather than sharing one implementation — touching
   `PaymentVoucherService.cs` remains outside U1's scope. A follow-up could extract both to one
   shared location if Opus/Fable wants single-implementation reuse; this is a review-time
   decision, deliberately left open.

## Attempt log

### RED capture (2026-08-19, pre-fix)
Env footgun hit: `dotnet build` on the shared `bin/` initially failed MSB3027/MSB3021 — PID 41752
(`Accounting.Api.exe`, started 2026-08-18 22:07, ~13h stale, matches troubles-wiki's documented
"stale dev-server" variant, not a live test run). Tried the wiki's isolated-`-o` build workaround
first, but that variant is documented for non-DB tests only — `PostgresFixture.InitializeAsync`
computes `SqlScripts` dir via a hardcoded `../../../../../src/...` climb from
`AppContext.BaseDirectory`, which breaks under a non-standard `-o` output path
(`DirectoryNotFoundException: Z:\temp\src\Accounting.Infrastructure\Migrations\SqlScripts`) for
these Postgres-backed `[SkippableFact]`s. Confirmed via `Get-Process -Id 41752` (13h old, not a
`testhost`) that this was the stale-server variant, not a concurrent legitimate run — U1 is this
wave's designated test-runner slot per PLAN-fix-findings-r2.md, U7 (Haiku, FE-only, mechanical
`problemToast` swap) has no reason to hold a live backend. Killed PID 41752, rebuilt clean against
the shared `bin/`. **New troubles-wiki candidate** (not yet written — folding at final report):
isolated `-o` builds are unsafe for `PostgresFixture`-backed tests specifically because of this
relative-path assumption, not just the "shared Postgres resource" reason already documented.

Command: `TEAS_TEST_PG=... dotnet test --no-build --filter
"FullyQualifiedName~PayrollRunServiceTests.T21|...T22|...T23"`

Result: `Failed! - Failed: 2, Passed: 1, Skipped: 0, Total: 3, Duration: 8 s`
- T21: FAIL — "Expected a DomainException to be thrown, but no exception was thrown." (guard
  missing, exactly the bug — a bad placeholder profile tax id silently renders today).
- T22: FAIL — `monthlyDigits.Should().Contain("0105000000012")` failed (company 1's profile still
  holds the `0000000000000` placeholder pre-638).
- T23: PASS (regression pin, no guard yet to interfere — expected).

## Extension — Fable scope ruling 2026-08-19 (50-tawi in scope)
Fable ruled the flagged follow-up #1 IN SCOPE: the 50-ทวิ certificate is a compliance artifact
handed to employees, same defect class as the three originally-named call sites. Extended U1:

- [x] Guarded `BuildEmployeeWht50TawiAsync` (Pnd1FilingService.cs) with the same
      `PayerTaxIdRules.EnsureUsable` check, placed identically to the other two Pnd1 guards
      (local `employerTaxId` var computed, guarded immediately after, used in place of the old
      inline `prof?.TaxId ?? c?.TaxId` expression). `PayerTaxId` on `Wht50TawiData` is `string?`
      so no `?? ""` needed — the guard already null-coalesces internally.
- [x] Added `T24_employee_wht50tawi_refuses_a_placeholder_company_profile_tax_id`, same repro
      shape as T21 (fresh TestCompanyFactory company, corrupt `company_profile.tax_id` only,
      `companies.tax_id` stays real).
- [x] RED-then-GREEN captured (see below).
- [x] Re-ran the same targeted set as before (Payroll namespace + `WhtPayerTaxIdGuardTests` +
      `TaxFormFillDiagnostic`) — no full suite run.
- [x] Consolidation of `PayerTaxIdRules` vs `PaymentVoucherService.IsUsablePayerTaxId` left
      NOT done, per Fable's instruction — still a review-time decision, correctly flagged in
      the "Follow-ups flagged for Fable/Opus" section above (item 2).

### RED capture (T24, guard temporarily disabled)
Temporarily commented out just the new `PayerTaxIdRules.EnsureUsable(employerTaxId);` call in
`BuildEmployeeWht50TawiAsync` (the other two Pnd1 guards + the Sso guards stayed active),
rebuilt, ran T24 alone:
`Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1, Duration: 1 s`
— "Expected a DomainException to be thrown, but no exception was thrown." Exactly the bug
(a placeholder profile tax id would silently render on the 50-ทวิ certificate).

### GREEN capture (guard restored)
Restored the guard line, rebuilt clean, re-ran the full targeted set:
`TEAS_TEST_PG=... dotnet test --no-build --filter "FullyQualifiedName~Payroll|FullyQualifiedName~WhtPayerTaxIdGuardTests|FullyQualifiedName~TaxFormFillDiagnostic"`
→ `Passed! - Failed: 0, Passed: 74, Skipped: 12, Total: 86, Duration: 51 s`
(was 73/85 before this extension; +1 passed/+1 total = T24, same 12 `TEAS_DIAG=1`-gated
diagnostic skips as before, unrelated to this change — 0 regressions).
