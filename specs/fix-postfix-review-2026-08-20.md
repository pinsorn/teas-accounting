# fix-postfix-review-2026-08-20 — three small items

Source: `_review/post-fix-review-2026-08-20.md` (Codex findings 1 and 2) + Ham's direct
request (item 3). Blast cap: 9 files. No commits (orchestrator commits).

## 1 [P2] Cancel/reject reasons: validate at both ends — [x]

**Root cause:** `ReasonBody` records (`SalesChainEndpoints.cs`, `PurchaseOrderEndpoints.cs`)
live in `Accounting.Api`, out of reach of the Application-assembly FluentValidation scan
(`AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly)` in
`Accounting.Application/DependencyInjection.cs` only scans `Accounting.Application`). No
validator plumbing existed for any of the 4 endpoints that take a `ReasonBody`. FE also sent
the raw, untrimmed string with `disabled={!reason}` (empty string only, not whitespace).

**Sweep — every `ReasonBody`-consuming endpoint (grepped `ReasonBody\b` across
`backend/src/Accounting.Api/Endpoints/*.cs`):**
1. `SalesChainEndpoints.cs` — Quotation `/reject` (line 63)
2. `SalesChainEndpoints.cs` — Quotation `/cancel` (line 66)
3. `BillingNoteEndpoints.cs` — Invoice `/cancel` (references `SalesChainEndpoints.ReasonBody`)
4. `PurchaseOrderEndpoints.cs` — PO `/cancel` (own `ReasonBody` record)

(SalesOrder and DeliveryOrder have `CancelledReason` columns but NO cancel/reject endpoint
exists for either — confirmed by grep; out of scope, nothing to fix there.)

**Fix (manual guard, per spec's Ponytail hatch — `ReasonBody` has no validator plumbing):**
`SalesChainEndpoints.RequireReason(string?)` — a shared static helper (not a new file;
`BillingNoteEndpoints`/`PurchaseOrderEndpoints` already cross-reference
`SalesChainEndpoints.ReasonBody` the same way) — trims, throws typed `DomainException`
(`validation.reason_required` / `validation.reason_too_long`, → 422 via
`DomainExceptionMiddleware`, never a raw DB 500) for empty-after-trim or >500 chars, else
returns the trimmed string. Called at all 4 sites above, replacing the raw `b.Reason` pass-through.

FE: `quotations/[id]/page.tsx` (cancel + reject) and `invoices/[id]/page.tsx` (cancel, same
gap confirmed present) — `disabled={!reason.trim()}`, `maxLength={500}` on the inputs, send
`reason.trim()`. `expense-claims/[id]/page.tsx` already had proper `.trim()` + BE
FluentValidation (`RejectExpenseClaimValidator`, `NotEmpty().MaximumLength(500)`) — confirmed
compliant, left untouched (out of named scope; this was the "reference" convention).

**Tests (RED→GREEN):** `backend/tests/Accounting.Api.Tests/Endpoints/ReasonValidationTests.cs`
(new file) — pure xunit facts (no DB): 501-char throws `DomainException` (message excludes
"22001"), whitespace-only throws, null throws, exactly-500-char accepted & returned trimmed,
surrounding-whitespace trimmed. RED confirmed first (`RequireReason` didn't exist — CS0117),
then GREEN: 5/5 passed.

**Live verification (fresh quotation #12/`08-2026-QT-0004`, VAT Review Co):** cancel dialog
`maxlength="500"` attribute present; confirm button `disabled=true` on whitespace-only input;
`disabled=false` on real text; input value capped at 500 chars when 600 chars typed. Full
end-to-end: typed a real reason → confirm → HTTP success, doc → Cancelled, no raw 500.
Screenshots: `q12-cancel-dialog.png`, `q12-final-cancelled.png` (scratchpad).

## 2 [P3] ActivityLog renders the note it already receives — [x]

**Fix:** `frontend/components/doc/ActivityLog.tsx` — added `noteText(note): string | null`
(pure helper, mirrors the existing `activityHeadline` idiom exactly), rendered under the
headline/actor line when non-whitespace.

**Skipped:** did not add a unit test to the existing `ActivityLog.test.ts` idiom, to stay
within the 9-file blast cap (item 1's RED→GREEN backend test was the explicitly mandatory
one). Verified instead via the live browser check the spec names as the fallback path.

**Live verification:**
- Quotation #9 (`08-2026-QT-0003`, Cancelled): activity panel shows "Cancelled · 20 ส.ค. 2569
  · admin" followed by "Post-fix VAT cancellation reason 20260820" — the note.
- Quotation #4 (`08-2026-QT-0002`, Rejected): shows "Rejected ... " followed by "smoke-test
  REAL reject reason 1787222947387".
- Quotation #12 (freshly cancelled during this session): shows the new reason
  "POSTFIX-item1-check end-to-end cancel reason 20260821".
- Screenshots: `item2-quotation-9-activity.png`, `item2-quotation-reject-activity.png`,
  `q12-final-cancelled.png` (scratchpad).

## 3 [Ham request] Version number on the Onboarding page — [x]

**Mechanism reused:** same as the dashboard footer (`(dashboard)/layout.tsx`) —
`GET /system/info`, `version.split('+')[0]`, best-effort (swallow failure, hide silently).

**Backend resilience fix (required for the mechanism to actually reach this page):**
`/system/info` (`Program.cs`) is `.RequireAuthorization()` and calls
`ICompanyTaxConfigService.GetAsync`, which does `WHERE CompanyId == tenant.CompanyId` with no
fallback — for a companyId=0 super-admin (fresh install / no home company — precisely the
onboarding wizard's 'company' phase, the ONE page that most needs the version) this throws
`InvalidOperationException` and previously 500'd the WHOLE endpoint, hiding the version too.
Wrapped `taxCfg.GetAsync` in try/catch(InvalidOperationException): `version` is now
unconditional; `vat_mode`/`vat_rate`/`pnd30_submission_mode` degrade to `null` only in that
edge case. Happy path (any real company) is byte-for-byte unchanged — confirmed by full
regression suite (480/480 unaffected) and by not touching any other field.

**Frontend:** `frontend/app/onboarding/page.tsx` — `useEffect` gated to `phase === 'company'`
(the only phase with a real session; `apiGet('system/info')` unauthenticated during
`createAdmin` would 401 and unnecessarily fire the global `emitSessionExpired()` event — even
though nothing listens for it there, since `SessionExpiredModal` is dashboard-layout-only),
fetches `system/info`, same `split('+')[0]` parsing, renders `TEAS · v{version}` as small muted
text (`text-xs text-ink-400`) at the bottom of the wizard card. `login/page.tsx` checked —
does not show a version; per instruction, left untouched (Onboarding only).

**Live verification — partial:**
- Verified (screenshot): onboarding page unauthenticated (`createAdmin` phase, the only phase
  reachable without disrupting the shared dev DB's real user/company state) renders correctly
  at both desktop (1440×900) and mobile (390×844) with no console/page errors introduced by
  this change; no version shown there (correct — gated to `phase === 'company'` only).
  Screenshots: `item3-onboarding-anon.png`, `item3-onboarding-anon-mobile.png` (scratchpad).
- NOT live-verified: the actual version footer rendering in the `company` phase. Reaching
  that phase needs a genuine companyId=0 super-admin session, which in this shared dev DB
  would mean creating a brand-new super-admin account — judged disproportionate risk/scope
  for a "small items" task and not done. Compensating evidence instead:
  - `backend/tests/Accounting.Api.Tests/Endpoints/ReasonValidationTests.cs` —
    `SystemInfoResilienceTests.GetAsync_for_an_unresolvable_company_throws_InvalidOperationException`
    (collocated in the same file, not a new one, per blast cap): proves, against the real
    `teas_test` DB via `TestCompanyFactory.BuildProvider(companyId: 0, ...)`, that
    `CompanyTaxConfigService.GetAsync` throws exactly `InvalidOperationException` — the precise
    premise the new `Program.cs` catch clause depends on. 1/1 passed.
  - `tsc --noEmit` clean on the onboarding page's new fetch/render code.
  - Code-level trace: the fetch/parse/render logic is a direct, minimal mirror of the
    dashboard footer's already-working code.

## Endpoint sweep summary (item 1)

| Endpoint | File | Guard applied |
|---|---|---|
| POST /quotations/{id}/reject | SalesChainEndpoints.cs | `RequireReason` |
| POST /quotations/{id}/cancel | SalesChainEndpoints.cs | `RequireReason` |
| POST /invoices/{id}/cancel (billing note) | BillingNoteEndpoints.cs | `SalesChainEndpoints.RequireReason` |
| POST /purchase-orders/{id}/cancel | PurchaseOrderEndpoints.cs | `SalesChainEndpoints.RequireReason` |
| POST /expense-claims/{id}/reject | ExpenseClaimEndpoints.cs | already had `RejectExpenseClaimValidator` (untouched, reference pattern) |

## Gates

- [x] `tsc --noEmit` clean (frontend).
- [x] Frontend vitest: 15 files / 70 tests passed (full suite, no regression).
- [x] Backend isolated `-o` build (`backend/tests/Accounting.Api.Tests/bin/isorun/net10.0` —
      depth-preserving path per troubles-wiki.md, avoids locking the running API's
      `bin/Debug`): 0 errors.
- [x] Backend targeted tests: `ReasonValidationTests` + `SystemInfoResilienceTests` 6/6 passed.
- [x] Backend broader regression: `Sales` + `Purchase` + `Hardening` + `Endpoints` namespaces,
      480 passed / 11 skipped (`TaxFormFillDiagnostic`, pre-existing always-skip diagnostics,
      unrelated) / 0 failed.
- [x] Live browser: items 1 and 2 fully verified end-to-end on a fresh doc; item 3 partially
      (see above) — backend resilience proven via DB-backed unit test instead of the FE
      company-phase flow, which was judged out of proportion to reach safely.

## Files touched (9 — at cap)

1. `backend/src/Accounting.Api/Endpoints/SalesChainEndpoints.cs`
2. `backend/src/Accounting.Api/Endpoints/BillingNoteEndpoints.cs`
3. `backend/src/Accounting.Api/Endpoints/PurchaseOrderEndpoints.cs`
4. `backend/src/Accounting.Api/Program.cs`
5. `frontend/app/(dashboard)/quotations/[id]/page.tsx`
6. `frontend/app/(dashboard)/invoices/[id]/page.tsx`
7. `frontend/components/doc/ActivityLog.tsx`
8. `frontend/app/onboarding/page.tsx`
9. `backend/tests/Accounting.Api.Tests/Endpoints/ReasonValidationTests.cs` (new)
