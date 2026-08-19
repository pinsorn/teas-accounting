# SPEC — U5: fixed-asset disposal date validation (L3-9)

Author: Fable (dispatch prompt, this session). Implementer: Sonnet. Blast cap: 4 files.

## Problem
`findings-r2/findings-leg3.md` L3-9: `DisposeCoreAsync` (shared by Dispose and WriteOff) had no
check that the disposal/write-off date is on/after the asset's own `AcquireDate` or
`DepreciationStartDate`. Live repro: asset acquired 2026-08-10, disposed 2026-07-15 (26 days
BEFORE acquisition) — HTTP 200, a real balanced JE posted, no error.

## Design
`DisposeCoreAsync` (`backend/src/Accounting.Infrastructure/FixedAsset/FixedAssetService.cs`), right
after the existing `fixed_asset.not_active` status guard (asset is loaded by then) and BEFORE any
money/JE mechanics:
- `date < asset.AcquireDate` → `DomainException("fixed_asset.disposal_date_invalid", ...)`
- `date < asset.DepreciationStartDate` → same code, different message.
- `date == AcquireDate` (or `== DepreciationStartDate`) is the inclusive boundary — allowed.

Error code follows the existing `fixed_asset.*` family (`not_active`, `locked_mismatch`,
`account_invalid`, ...) — no dedicated validator class exists for `DisposeFixedAssetRequest`
cross-field checks (FluentValidation only checks request SHAPE — `Proceeds >= 0` etc.; it cannot
see the loaded asset's dates), so the check lives in the service after `LoadAsync`, same place as
the sibling `not_active` guard. `DomainExceptionMiddleware`'s default rule maps any code with no
special suffix (`.not_found`, `.scope_required`, `auth.*`) to HTTP 422 — confirmed at
`backend/src/Accounting.Api/Middleware/DomainExceptionMiddleware.cs:104-107`.

Untouched: the existing closed-period guard (`period.EnsureOpenAsync(date, ct)`, runs BEFORE
`LoadAsync` — order preserved, new check inserted AFTER `LoadAsync`/status guard, same position as
the money mechanics it now gates). The pre-existing bad dev row (R2L3-F) is NOT cleaned by this
unit — the co1 wipe+reseed at batch end handles it.

## Checklist
- [x] `fixed_asset.disposal_date_invalid` guard added in `DisposeCoreAsync`
      (`FixedAssetService.cs:250-261`), covers both Dispose and WriteOff (shared method).
- [x] RED: 2 new tests fail with "No exception was thrown" pre-fix (confirms L3-9 reproduces).
- [x] GREEN: same 2 tests pass post-fix; boundary test (disposal == acquire) passes both
      pre- and post-fix (no regression risk there).
- [x] Existing disposal/write-off/closed-period tests still green (40/40 FixedAsset-area tests).

## Tests added (`backend/tests/Accounting.Api.Tests/FixedAsset/FixedAssetServiceTests.cs`)
- `Dispose_refuses_when_disposal_date_is_before_acquire_date` — acquire next month, dispose today
  → refused, asset stays `Active` (no mutation on refusal).
- `Dispose_refuses_when_disposal_date_is_before_depreciation_start_date` — acquire today,
  depreciation start next month, dispose today (>= acquire, < depStart) → refused.
- `Dispose_succeeds_when_disposal_date_equals_acquire_date` — boundary, disposal == acquire date
  → 200/success (`Disposed`).

## Attempt log
1. (2026-08-19, Sonnet) Read `DisposeCoreAsync`, confirmed no cross-date check existed. Wrote 3
   RED tests (disposal-before-acquire, disposal-before-depStart, boundary==). Ran targeted filter
   `FullyQualifiedName~FixedAssetServiceTests&(...)` — 2 failed with "No exception was thrown"
   (RED, correct reason — matches L3-9's "accepted with 200" symptom exactly), boundary test
   already passed (no guard needed there). Added the 2-clause guard after the `not_active` check.
   Rebuilt + reran the same filter — 3/3 GREEN. Ran the full FixedAsset-area filter
   (`FullyQualifiedName~FixedAsset`) — 40/40 GREEN (0 regressions in existing disposal/write-off/
   closed-period/depreciation tests).

## Evidence
```
dotnet test .../Accounting.Api.Tests.dll --filter "FullyQualifiedName~FixedAsset"
Passed!  - Failed: 0, Passed: 40, Skipped: 0, Total: 40, Duration: 53 s
```
