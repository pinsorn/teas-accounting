# O14 — reopen a CLOSED monthly period (design, Fable 2026-07-26)

Ham approved building this. Evidence it's needed: `swarm-findings/army/V3-nonvat-pv-ledger.md` — co6 had all
12 FY2026 months closed by the year-end leg, and because `PaymentVoucherService.CreateDraftAsync` pins
DocDate to Bangkok-today and gates the period at DRAFT time, **no payment voucher (not even a draft) can be
created on that company until the calendar reaches 2027**. Today only `reopen-year` exists and it explicitly
does not touch the monthly locks. Closing a month by mistake is currently irreversible without raw SQL.

## What exists today (verified in code, 2026-07-26)
- `PeriodEndpoints.cs`: `POST /periods/{year}/{month}/close` → `Permissions.Gl.PeriodClose` ·
  `GET /periods/{year}/{month}/status` (authn only) · `POST /periods/{year}/close-year` and
  `POST /periods/{year}/reopen-year` → **both** gated on `Permissions.Gl.YearClose`.
- `PeriodCloseService.CloseAsync` upserts one `AccountingPeriod` row (`Status = Closed`, `ClosedAt`,
  `ClosedBy`, `CloseNotes`) inside a transaction; throws `period.already_closed` on a second close.
  `IsOpenAsync`/`EnsureOpenAsync` are the read side; `EnsureOpenAsync` throws `period.closed`.
- `AccountingPeriod` has ONLY close-side audit columns (`ClosedAt`/`ClosedBy`/`CloseNotes`).
  `PeriodStatus` is just `{ Open, Closed }`.
- `YearCloseService.ReopenAsync` is the pattern to mirror for concurrency: it claims the slot with a
  conditional `ExecuteUpdateAsync` and checks affected-rows, so a racing second reopen loses cleanly
  instead of both proceeding.

## Design
### D1 — permission: reuse `Permissions.Gl.PeriodClose`
The repo's own precedent is that close and reopen of the same scope share one permission
(`close-year`/`reopen-year` both use `Gl.YearClose`). So monthly reopen reuses `Gl.PeriodClose` —
**no new permission code, no seed migration, no RBAC-matrix churn** (and no risk of the
`rbac-seed-ordering-footgun`). Whoever may close a month may reopen it.

### D2 — NO schema change. Audit via the activity log.
Do **not** add `ReopenedAt`/`ReopenedBy` columns. Flip `Status` back to `Open`, null out `ClosedAt`/
`ClosedBy`/`CloseNotes` (the row then reads exactly like a period that was never closed, which is what
`IsOpenAsync` already understands), and record the event with `IActivityRecorder` —
`"AccountingPeriod", periodId, $"{year}-{month:D2}", companyId, action "Reopened",
fromStatus "Closed", toStatus "Open", module "gl"`, carrying the caller-supplied reason as the note.
This mirrors how `PaymentVoucher.Cancel()` and `ExpenseClaim.Cancel()` keep their reason in the activity
note instead of a new column. A new SqlScripts file would force a DB backup + post-deploy assert at
deploy time for no gain.

### D3 — the invariant that actually matters: **never reopen a month inside a CLOSED fiscal year**
If the fiscal year has been closed (a `FiscalYearCloses` row for that year with `ReversedAt == null`),
its revenue/expense accounts have already been swept into retained earnings. Reopening a month inside it
would let a new posting land in a period whose P&L was already closed out — the balance sheet and the
closing entry would silently disagree. **Refuse** with a distinct error (`period.year_closed`, message:
reopen the fiscal year first). Test this explicitly; it is the one way this feature could corrupt a
ledger.

### D4 — `ReopenAsync(int year, int month, string? reason, CancellationToken ct)`
On `IPeriodCloseService` + `PeriodCloseService`:
1. auth guard (mirror `CloseAsync`'s).
2. D3 fiscal-year check → `period.year_closed`.
3. Atomic claim, mirroring `YearCloseService.ReopenAsync`: conditional
   `ExecuteUpdateAsync` over `AccountingPeriods` `WHERE CompanyId/Year/Month match AND Status == Closed`,
   setting `Status = Open, ClosedAt = null, ClosedBy = null, CloseNotes = null`. If 0 rows affected →
   `period.not_closed` ("Period {year}-{month:D2} is not closed."). This makes a concurrent double-reopen
   deterministic: one wins, the other gets a clean 422.
4. Activity record per D2, then `SaveChangesAsync`, all inside one transaction like `CloseAsync`.
- No JE is posted or reversed — a monthly close writes no journal (unlike year-end), so a reopen has
  nothing to reverse. Say so in a comment so nobody later "mirrors" the year-reopen's reversing JE here.

### D5 — endpoint + FE
`POST /periods/{year:int}/{month:int}/reopen` → `ReopenAsync`, `Results.NoContent()`,
`.RequireAuthorization(prefix + Permissions.Gl.PeriodClose)`. Body: optional `{ reason }`.
FE: on the period-close screen, a closed month gets a "เปิดงวดใหม่" action behind a
`PermissionGate scope="gl.period.close"` + `ConfirmActionDialog` whose warning states plainly that
documents can be posted into the period again. i18n keys in BOTH `th.json` and `en.json`.
Also add the two new error codes to `frontend/lib/i18n/problems.ts` in Thai.

### D6 — tests
- close → reopen → a document dated in that month posts successfully (the point of the feature).
- reopen a never-closed month → `period.not_closed`.
- **reopen a month whose fiscal year is closed → `period.year_closed`** (D3, the ledger-safety one).
- concurrent double-reopen → exactly one succeeds (follow whatever pattern the year-reopen concurrency
  test uses; if none exists, an out-of-band `ExecuteUpdate` before the call is enough to prove the
  affected-rows guard).
- a role holding neither `gl.period.close` nor super-admin → 403; a role holding it → 204.
- `RbacAuthMapTests` / `RbacMatrixTests` stay green (`TEAS_REPO_ROOT` must be set) and the generated
  `docs/rbac/endpoint-permission-map.generated.md` is REGENERATED by running those tests, not hand-edited.

## Acceptance (real-world, and it exercises D3 for free)
co6 has both the year AND all 12 months closed. So the live check is: reopen-YEAR first, then reopen the
month, then create a payment voucher — which is exactly the sequence a real accountant would need and
which proves D3's ordering guard is the right shape rather than an obstacle.

## Out of scope
Nothing here changes `EnsureOpenAsync`, the DocDate pinning (§10), or year-end closing. If a future item
wants "reopen just this document" instead of the whole month, that is a different feature.
