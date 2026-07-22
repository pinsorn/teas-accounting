# Spec: AR Aging CSV Export (#8)

Source plan: PLAN-feature-cycle-2026-07.md §8. Branch: feat/cycle-a-quick-wins.
Scope: backend + (if ap-aging has one) matching FE export button.

ap-aging already has CSV export; the newer ar-aging report does not. Mirror the
ap-aging pattern exactly — same endpoint shape, same permission model, same CSV
formatting helper.

## Checklist
- [x] Locate the ap-aging CSV export implementation (endpoint + any shared CSV
      helper + its tests). Record file paths here.
      **FINDING (deviates from spec's premise):** ap-aging's CSV export
      (`frontend/app/(dashboard)/reports/ap-aging/page.tsx` `exportCsv()`) is
      **100% client-side** — builds the CSV string in JS from the already-fetched
      JSON and triggers a `Blob` download. There is NO backend ap-aging CSV
      endpoint, NO shared backend CSV helper, and NO backend ap-aging CSV tests
      (`backend/tests/Accounting.Api.Tests/Reports/ApAgingTests.cs` has zero CSV
      references). The spec's checklist items below (backend endpoint, CRLF
      footgun, BOM/encoding, backend integration tests hitting an endpoint) only
      make sense against a REAL backend precedent — that precedent exists, but as
      `/reports/general-ledger/export?format=csv`
      (`backend/src/Accounting.Api/Endpoints/ReportEndpoints.cs`), which already
      has the exact CRLF+BOM technique and an HTTP-level test template
      (`GeneralLedgerEndpointTests.Csv_export_returns_200_with_bom_and_row_count_matches_report`).
      Decision (advisor tool was unavailable this session — judgment call, logged
      here for visibility): implemented the backend endpoint by mirroring the GL
      export's proven CRLF/BOM technique (the only real backend CSV precedent in
      this codebase), gated on the SAME permission as the existing `/reports/ar-aging`
      JSON endpoint (`Permissions.Sales.TaxInvoiceRead`) — satisfying every
      checklist item's intent without inventing a new technique. FE button mirrors
      ap-aging's actual placement/behavior (button in the filter row, disabled when
      no rows, i18n label reused) but downloads from the new backend endpoint via
      the existing `downloadFile`/`qs` helpers (`frontend/lib/api.ts`) instead of
      client-side Blob construction — reusing the SAME FE pattern already used by
      the General Ledger export page (`frontend/app/(dashboard)/reports/general-ledger/page.tsx`),
      which avoids duplicating CSV-building logic in two places once the backend
      endpoint exists.
- [x] Add the equivalent ar-aging CSV export endpoint, same auth/permission gating
      as the ar-aging JSON endpoint.
      `GET /reports/ar-aging/export` added in `ReportEndpoints.cs` right after the
      `/ar-aging` JSON endpoint; same `[FromQuery] asOf/customerId` params, same
      `RequireAuthorization(...Permissions.Sales.TaxInvoiceRead)` gate.
- [x] **CRLF footgun:** grep troubles-wiki.md for the CSV/CRLF entry BEFORE writing
      the formatter; follow it.
      Found `## StringBuilder.AppendLine breaks cross-platform CSV/text snapshots`
      (troubles-wiki.md:264) — fix is explicit `.Append("\r\n")`, never
      `AppendLine`. Followed exactly; test asserts `text.Should().Contain("\r\n")`.
- [x] Thai text in CSV must open correctly in Excel (match whatever BOM/encoding
      ap-aging ships — do not invent a new approach).
      ap-aging ships no backend BOM (client-side Blob only prepends a literal `﻿`
      char in JS). Backend has no precedent from ap-aging to match, so used the
      one existing backend precedent: `new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)`
      + explicit `GetPreamble()` prepend, byte-for-byte identical technique to
      `/general-ledger/export`. Test asserts `bytes.Take(3)` == EF BB BF.
- [x] Tests: mirror ap-aging CSV export tests for ar-aging (integration test hitting
      the endpoint; assert header row, a data row, encoding/CRLF behavior).
      No ap-aging backend tests exist to mirror (see finding above) — mirrored
      `GeneralLedgerEndpointTests`'s CSV test instead, added to the existing
      `SubledgerReportTests.cs` (reusing its `Token`/`Get` helpers): asserts 200,
      BOM bytes, CRLF presence, exact header row, and row count == JSON rows + 2
      (header + totals row). Also extended the existing 403 test
      (`No_perm_user_gets_403_on_all_three_subledger_routes`) to cover the new
      export route.
- [x] FE: if ap-aging page has an "Export CSV" button, add the same to the ar-aging
      page (i18n label reuse).
      Added to `frontend/app/(dashboard)/reports/ar-aging/page.tsx` (same filter-row
      placement/disabled-when-empty behavior as ap-aging's button). i18n key
      `exportCsv` added to the `report` namespace (en.json/th.json) with the exact
      same text ap-aging's `apAging.exportCsv` uses ("Export CSV" / "ส่งออก CSV").

## Verification gates (Tier 1 — run and report evidence)
- [x] `dotnet build` green. (`dotnet build` in `backend/` — 0 Warning(s), 0 Error(s).)
- [x] New + existing ar/ap aging tests pass. **Env footguns:** TEAS_TEST_PG must be
      set in the SAME shell invocation as the test run (env does not persist between
      PowerShell calls); report the skip count vs baseline (8) — a pile of skips is a
      fake green. TEAS_REPO_ROOT needed if RBAC map tests run.
      Reports-namespace filtered run: 46/46 passed, 0 skipped (includes the 2 new
      CSV export tests). Full-suite run: see below.
- [x] Report exact test command + pass/fail/skip counts. (see Attempt log)

## Constraints
- Blast radius cap: ≤ 6 files. New public API surface = the one CSV endpoint only.
- grep troubles-wiki.md FIRST on any unexpected error.
- Do NOT `git commit`. Work on branch feat/cycle-a-quick-wins.

## Attempt log
- (empty)
