# Performance Review — TEAS — 2026-06-17

## Summary

Overall posture: **Moderate risk.** The async discipline and caching foundations are mostly sound,
but four confirmed patterns will degrade under concurrent load. No catastrophic N+1 loops found;
the worst issue is a **synchronous blocking DB call** on the e-Tax XML path that will stall thread-pool
threads. Several read-heavy GL-posting paths missing `AsNoTracking` add unnecessary tracker pressure,
and a double-query round-trip on every authenticated request is avoidable with a single SQL join.

| Severity | Count |
|----------|-------|
| Critical | 1 |
| High | 3 |
| Medium | 4 |
| Low | 2 |

---

## Findings

---

### [CRITICAL-1] Synchronous DB query blocking the thread pool — ETaxXmlBuilder

**File:** `backend/src/Accounting.Infrastructure/ETax/ETaxXmlBuilder.cs:23-28`

```csharp
public string BuildTaxInvoiceXml(long taxInvoiceId, CancellationToken ct)
{
    var ti = _db.TaxInvoices.IgnoreQueryFilters()
        .Include(t => t.Lines)
        .FirstOrDefault(t => t.TaxInvoiceId == taxInvoiceId)   // <-- sync DB call
        ?? throw new DomainException("etax.ti_missing", …);
```

**Confidence:** [Confirmed]
**Impact:** `FirstOrDefault` (not `FirstOrDefaultAsync`) issues a synchronous Npgsql call on the
ASP.NET thread-pool thread. Under concurrent PDF/e-Tax requests this will starve the pool —
violates CLAUDE.md §5 "never `.Result`/`.Wait()`" spirit and is the most dangerous pattern in
the codebase. The `CancellationToken ct` parameter is accepted but never forwarded.

**Fix:** Change the method signature to `async Task<string>`, switch to `FirstOrDefaultAsync(…, ct)`,
and add `AsNoTracking()` (this is a read-only XML build path). Update `IETaxXmlBuilder` to match.

---

### [HIGH-1] Double-query round-trip on every authenticated request — PermissionLookup

**File:** `backend/src/Accounting.Infrastructure/Identity/PermissionLookup.cs:18-37`

```csharp
var roles = await _db.UserRoles …
    .Select(ur => ur.Role!.RoleCode).Distinct()
    .ToListAsync(ct);                            // round-trip #1

var permissions = await _db.UserRoles …
    .SelectMany(ur => ur.Role!.Permissions)
    .Select(rp => rp.Permission!.PermissionCode).Distinct()
    .ToListAsync(ct);                            // round-trip #2 — identical WHERE clause
```

**Confidence:** [Confirmed]
**Impact:** Every request (JWT validation path calls `LoadAsync`) issues two sequential DB round-trips
against `sys.user_roles` with an identical `WHERE` predicate. Under 100 RPS this is 200 serialised
PG queries where 1 JOIN would suffice. Also missing `AsNoTracking()` on a pure-read path.

**Fix:** Merge into a single query projecting both role code and permission code in one
`SelectMany`, then split in memory. Add `AsNoTracking()`.

---

### [HIGH-2] `ContinueWith(t => t.Result)` — unnecessary wrapping that accesses `.Result`

**File:** `backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs:44-47, 343-348, 379-383, 416-421`

```csharp
public Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct) =>
    db.Branches.OrderBy(…).Select(…)
        .ToListAsync(ct)
        .ContinueWith<IReadOnlyList<BranchDto>>(
            t => t.Result,                          // accesses .Result inside continuation
            TaskContinuationOptions.OnlyOnRanToCompletion);
```

**Confidence:** [Confirmed] — same pattern at lines 47, 348, 383, 421.
**Impact:** `t.Result` inside a `ContinueWith` callback is technically safe (the task is already
completed), but this is a code smell that suppresses exceptions on cancellation/failure paths
(the `OnlyOnRanToCompletion` flag silently abandons the task on cancellation rather than propagating
the `OperationCanceledException`). All four service methods — `BranchService`, `CompanyService`,
`DocumentPrefixService`, `ExpenseCategoryService` — are affected. CLAUDE.md §5 explicitly forbids
`.Result` in request paths; the intent here is benign coercion but the idiom is wrong.

**Fix:** Return `(IReadOnlyList<BranchDto>)await db.Branches.OrderBy(…).Select(…).ToListAsync(ct)`
or cast: `.ToListAsync(ct).ContinueWith<IReadOnlyList<…>>(t => (IReadOnlyList<…>)t.Result!, …)`
is unnecessary — just `await` and return the `List<T>` (which implements `IReadOnlyList<T>`). A
simple `async Task<IReadOnlyList<T>>` wrapper with a cast is cleaner and propagates cancellation.

---

### [HIGH-3] Missing `AsNoTracking()` on read-heavy GL-posting loads

**Files:** `backend/src/Accounting.Infrastructure/Ledger/GlPostingService.cs:42, 69, 88, 148, 229`

```csharp
var ti = await _db.TaxInvoices.Include(t => t.Lines)
    .FirstOrDefaultAsync(t => t.TaxInvoiceId == taxInvoiceId, ct);   // tracked

var pv = await _db.PaymentVouchers.Include(p => p.Lines)
    .FirstOrDefaultAsync(p => p.PaymentVoucherId == paymentVoucherId, ct);  // tracked
```

**Confidence:** [Confirmed]
**Impact:** `GlPostingService` loads full document + lines into EF's change-tracker on every
Post operation. These loads are read-only (the method writes new `JournalEntry` rows, not the
loaded documents). With large line-count invoices this wastes memory allocating shadow state
and slows `SaveChangesAsync` which must scan all tracked entities for changes.

**Fix:** Add `.AsNoTracking()` after each set reference in the posting reads. The loaded entities
are used only for data projection; no `SaveChanges` is called on them.
`GlPostingService` at line 88 also has a conditional branch that re-queries TaxInvoices — same fix.

---

### [MEDIUM-1] Two `SaveChangesAsync` calls in `BillingNoteService.CreateAsync`

**File:** `backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs:58-68`

```csharp
await ApplyLinesAsync(bn, req.Lines, ct);
foreach (var link in await BuildTaxInvoiceLinksAsync(req.TaxInvoiceIds, ct))
    bn.TaxInvoiceLinks.Add(link);
db.BillingNotes.Add(bn);
await db.SaveChangesAsync(ct);          // save #1 — assigns BillingNoteId
activity.Record("BillingNote", bn.BillingNoteId, …);
await db.SaveChangesAsync(ct);          // save #2 — persists activity log
```

**Confidence:** [Confirmed] — same pattern at lines 172+ (UpdateAsync).
**Impact:** Two round-trips where one suffices. The activity log entity can be added to the tracker
before the first `SaveChanges`, and the `BillingNoteId` will be populated by EF after the first save
anyway — or the activity row can be inserted in the same transaction. This is low latency impact per
call but adds up at volume.

**Fix:** Add `activity.Record(…)` to the tracker *before* the first save. If `Record` requires the
assigned ID, accept one save and remove the second by staging the activity row before calling
`db.ActivityLog.Add(activityRow)` inline instead of via a separate helper save.

---

### [MEDIUM-2] `ETaxXmlBuilder` — missing `AsNoTracking()` on read-only XML build

*(Sub-issue of CRITICAL-1; separate item because it applies even after the async fix.)*

**File:** `backend/src/Accounting.Infrastructure/ETax/ETaxXmlBuilder.cs:25-27`

The `Include(t => t.Lines)` load feeds a pure XML serialisation step — no entity mutation.
Once the sync→async fix is applied, add `.AsNoTracking()` to avoid tracking hundreds of
`TaxInvoiceLine` entities that will never be updated.

**Confidence:** [Confirmed]
**Impact:** Medium — tracker overhead on every e-Tax submission.

---

### [MEDIUM-3] `PermissionLookup` — missing `AsNoTracking()` on RBAC read path

**File:** `backend/src/Accounting.Infrastructure/Identity/PermissionLookup.cs:18-37`

Both `ToListAsync` calls are missing `AsNoTracking()` on a pure-read, per-request path.
Combined with HIGH-1 (double query), this adds tracker overhead to the hottest code path in the API.

**Confidence:** [Confirmed]

---

### [MEDIUM-4] `IdempotencyStore.PurgeExpiredAsync` not awaited in cleanup host

**File:** `backend/src/Accounting.Infrastructure/Identity/IdempotencyStore.cs:60`
and `backend/src/Accounting.Api/BackgroundServices/IdempotencyCleanupHostedService.cs`

```csharp
public Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken ct) =>
    _db.IdempotencyKeys.Where(k => k.ExpiresAt < now).ExecuteDeleteAsync(ct);
```

`PurgeExpiredAsync` correctly returns a `Task`. However, if the `IdempotencyCleanupHostedService`
caller does not `await` it (fire-and-forget), exceptions from `ExecuteDeleteAsync` are silently
swallowed and the idempotency table will grow unbounded. Verification needed in the hosted service
that this Task is properly awaited.

**Confidence:** [Suspected] — the return signature is correct; the risk is at the call-site.
**Impact:** If the caller fire-and-forgets, PG connection leaks and unbounded table growth.

---

### [LOW-1] `user_roles` table — single index `ix_user_roles_role_id` missing compound `(user_id, company_id)`

**File:** `backend/src/Accounting.Infrastructure/Migrations/20260616130322_InitialCreate.cs:2959`

The `PermissionLookup.LoadAsync` WHERE clause filters on `(user_id, company_id, valid_from, valid_to)`.
Only `ix_user_roles_role_id` is defined. A compound `(user_id, company_id)` index would make the
per-request RBAC lookup a covered index scan rather than a heap scan.

**Confidence:** [Confirmed] — migration shows only `ix_user_roles_role_id`.
**Impact:** Low at small user counts; degrades linearly with `sys.user_roles` row count.

**Fix:** Add a migration with `HasIndex(ur => new { ur.UserId, ur.CompanyId })` on `UserRole`.

---

### [LOW-2] `tax_invoices` — no index on `status` column used by list-filter queries

**File:** Migration `20260616130322_InitialCreate.cs` — confirmed index names:
`ix_tax_invoices_company_id_doc_date`, `ix_tax_invoices_customer_id_doc_date`.
No `(company_id, status)` or `(company_id, status, doc_date)` index exists.

**Confidence:** [Suspected] — query filter patterns on status are expected from list endpoints
(e.g., `WHERE company_id = $1 AND status = 'POSTED'`) but endpoint query code was not fully traced.
**Impact:** Low currently; as invoices accumulate this becomes a table-scan on large tenants.

**Fix:** Evaluate list-endpoint queries and add `(company_id, status, doc_date DESC)` composite
index if status-filter queries are confirmed.

---

## Verified GOOD (efficient patterns confirmed)

1. **`CompanyTaxConfigService`** (`Master/CompanyTaxConfigService.cs:17-33`) — request-scoped
   instance caches the company row in `_cached` after first fetch, uses `AsNoTracking()` and a
   `.Select()` projection. Only 3 columns fetched. Correct.

2. **Payroll PDF and PND-1 filing reads** (`Payroll/PayslipPdfService.cs:53`, `Pnd1FilingService.cs:18`)
   — all use `AsNoTracking()` correctly on read-only aggregation paths.

3. **Index coverage on hot tables** — `journal_entries` has `(company_id, doc_no)`,
   `(company_id, status, doc_date)`; `payment_vouchers` has `(company_id, doc_date)`,
   `(vendor_id, doc_date)`; `receipts` has `(customer_id, doc_date)`. Core financial query
   columns are well-covered.

4. **React Query global config** (`frontend/components/providers/query-provider.tsx:13`) —
   `staleTime: 30_000` and `refetchOnWindowFocus: false` are sensible defaults that prevent
   waterfall refetches on window focus, which is the most common React Query performance pitfall.

5. **No unbounded list endpoints on core document tables** — list queries on `TaxInvoice`,
   `PurchaseOrder`, `PaymentVoucher` all use `.Where(company_id == …)` + EF global filter,
   keeping result sets tenant-scoped. True unbounded lists were found only on reference-data
   tables (Branches, DocumentPrefixes, ExpenseCategories) where the row count is inherently
   bounded by business configuration, not transactions.

6. **Async discipline is broadly upheld** — no `.Wait()`, `.GetAwaiter().GetResult()`, or
   `async void` found outside the one confirmed sync `FirstOrDefault` in `ETaxXmlBuilder`.
   `CancellationToken` is threaded through virtually all service method signatures.
