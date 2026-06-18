# Backend Architecture & Quality Review — TEAS — 2026-06-17

## Summary

**Overall posture: GOOD with two Critical async-safety issues that must be fixed before they surface under load.**

Clean Architecture boundaries are honoured at the project-reference level: Domain has zero external dependencies, Application depends only on Domain, Infrastructure implements Application abstractions, and Api is the composition root. No circular project-level dependencies were found. DI lifetimes are broadly correct with one captive-dependency risk noted.

**Roslyn tools run:**
- `get_project_graph` — confirmed 5-project dependency graph
- `detect_circular_dependencies` (scope: projects) — **0 cycles**
- `detect_antipatterns` — 150 violations scanned; significant findings below
- `find_dead_code` — 55 symbols flagged; most are EF `IEntityTypeConfiguration` types (false positives from Roslyn not seeing EF's reflection-based discovery); one genuine dead class identified

**Severity counts:**
| Severity | Count |
|---|---|
| Critical | 2 |
| High | 2 |
| Medium | 3 |
| Low | 2 |

---

## Findings

---

### [CRITICAL-1] Sync-over-async via `ContinueWith(t => t.Result)` — four locations in MasterDataServices

**File:** `backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs`
**Lines:** 47, 348, 383, 421
**Confidence:** [Confirmed] — Roslyn AP002 + direct code read

**Evidence (representative, line 47):**
```csharp
public Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct) =>
    db.Branches.OrderBy(b => b.BranchCode)
        .Select(b => new BranchDto(...))
        .ToListAsync(ct).ContinueWith<IReadOnlyList<BranchDto>>(
            t => t.Result,                          // ← .Result on a Task
            TaskContinuationOptions.OnlyOnRanToCompletion);
```

Same pattern at:
- Line 348: `CompanyService.ListAsync`
- Line 383: `DocumentPrefixService.ListAsync`
- Line 421: `ExpenseCategoryService.ListAsync`

**Why it matters:** `.Result` inside `ContinueWith` is synchronous blocking on a `ThreadPool` thread. Under ASP.NET Core's async-only request pipeline this causes thread-pool starvation under concurrent load. CLAUDE.md §5 explicitly forbids `.Result` in request paths. The `OnlyOnRanToCompletion` flag also means any EF exception causes the returned `Task` to silently stay in `WaitingForActivation` rather than propagating — a fault-masking bug on top of the deadlock risk.

**Fix:** Replace with a direct `async`/`await` pattern:
```csharp
public async Task<IReadOnlyList<BranchDto>> ListAsync(CancellationToken ct) =>
    await db.Branches.OrderBy(b => b.BranchCode)
        .Select(b => new BranchDto(...))
        .ToListAsync(ct);
```

---

### [CRITICAL-2] Empty catch block silently swallows exceptions in `ApiKeyResolver`

**File:** `backend/src/Accounting.Infrastructure/Identity/ApiKeyResolver.cs`
**Line:** 70
**Confidence:** [Confirmed] — Roslyn AP007 + direct code read

**Evidence:**
```csharp
try
{
    await _db.ApiKeys.IgnoreQueryFilters()
        .Where(k => k.ApiKeyId == apiKeyId)
        .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);
}
catch { /* best-effort telemetry; never fail auth on this */ }
```

**Why it matters:** The intent (best-effort last-used timestamp update) is legitimate, but a bare `catch { }` with no logging swallows **any** exception — including `OperationCanceledException` (cancellation tokens), `ObjectDisposedException` (scoped DbContext after request ends), and connection exhaustion errors. These exceptions vanishing silently will make infrastructure failures invisible in production logs. The comment acknowledges intent but the implementation is wrong.

**Fix:** Log the swallowed exception at Debug/Warning level and narrow the catch to non-fatal types:
```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _logger.LogWarning(ex, "Best-effort ApiKey last-used update failed for key {ApiKeyId}", apiKeyId);
}
```

---

### [HIGH-1] `ClockDateExt` — dead internal extension class never used outside its file

**File:** `backend/src/Accounting.Infrastructure/Sales/QuotationChainServices.cs`
**Line:** 307
**Confidence:** [Confirmed] — Roslyn find_dead_code + Grep found zero call sites in `src/`

**Evidence:**
```csharp
internal static class ClockDateExt
{
    public static DateOnly ToDateOnly(this DateTime dt) => DateOnly.FromDateTime(dt);
}
```

**Why it matters:** `DateOnly.FromDateTime(dt)` is a single BCL call — the wrapper adds no value, is never called, and indicates either a refactoring residue or forgotten utility. Dead code increases cognitive load and may mislead future maintainers into thinking this is the canonical conversion point.

**Fix:** Delete the class. Use `DateOnly.FromDateTime(dt)` directly at any call site if needed.

---

### [HIGH-2] `ValidationException` from non-`/api/v1` paths not handled — falls through to ASP.NET default 500

**File:** `backend/src/Accounting.Api/Middleware/DomainExceptionMiddleware.cs`
**Lines:** 48–55 (v1 path), 74–90 (root/BFF path)
**Confidence:** [Confirmed] — code read

**Evidence:**
```csharp
catch (ValidationException vex) when (IsV1(ctx))      // ← only catches v1
{
    ...
}
catch (DomainException ex) when (IsV1(ctx))            // ← only catches v1
{
    ...
}
catch (Exception ex) when (IsV1(ctx))                  // ← only catches v1
{
    ...
}
catch (DomainException ex)                             // ← root/BFF DomainException OK
{
    ...
}
// No catch for ValidationException on root/BFF paths
```

The middleware handles `ValidationException` only for `/api/v1/*`. Any FluentValidation exception thrown by a root/BFF endpoint (e.g. auth endpoints under `/`) will escape all middleware branches and produce an unformatted 500 from the ASP.NET default handler, leaking a stack trace in development or returning an opaque 500 in production instead of the expected 400.

**Fix:** Add a parallel `catch (ValidationException vex)` branch (without the `when (IsV1(ctx))` guard) after the `catch (DomainException ex)` root branch:
```csharp
catch (ValidationException vex)
{
    ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
    ctx.Response.ContentType = "application/problem+json";
    // same RFC-7807 shape as DomainException branch
    ...
}
```

---

### [MEDIUM-1] EF `IEntityTypeConfiguration` classes reported as dead by Roslyn — false positive but flags a discoverability risk

**File:** Many files across `Accounting.Infrastructure/` (e.g. `Audit/ActivityLogConfiguration.cs`, `Sales/SalesChainConfigurations.cs`, etc.)
**Confidence:** [Confirmed false positive for EF] — Roslyn cannot see EF's `ApplyConfigurationsFromAssembly` reflection call

**Why it matters (medium, not low):** Roslyn's dead-code report listed 54 `*Configuration` classes as unreferenced. All are legitimate EF fluent configuration types discovered at runtime via reflection. However if a new developer applies the dead-code report naively they will delete real schema configuration. The codebase has no `// EF type configuration — discovered via reflection` XML comment or `[ReSharper::UsedImplicitly]`-equivalent to suppress. Consider adding a suppression comment or an architecture test asserting `IEntityTypeConfiguration<T>` implementors are never zero.

**Fix (low-effort):** Add a file-header comment to each configuration class: `// Discovered by EF Core via ApplyConfigurationsFromAssembly — not dead.` Or add a single xUnit fact in Domain.Tests that counts implementations.

---

### [MEDIUM-2] `IFileStorageService` registered as Singleton but implementation (`LocalDiskFileStorage`) may hold path state

**File:** `backend/src/Accounting.Infrastructure/DependencyInjection.cs`
**Line:** 78
**Confidence:** [Suspected] — DI registration confirmed; `LocalDiskFileStorage` internals not fully read

**Evidence:**
```csharp
services.AddSingleton<Application.Abstractions.IFileStorageService, Storage.LocalDiskFileStorage>();
```

**Why it matters:** A Singleton service that holds a root-path string derived from `IConfiguration` at construction time is fine. But if `LocalDiskFileStorage` injects `IWebHostEnvironment` or any Scoped service in its constructor, it creates a captive dependency. This is [Suspected] pending an internal read of the class.

**Fix:** Verify `LocalDiskFileStorage`'s constructor takes only value types or Singleton-safe services (e.g. `IOptions<T>`, `ILogger<T>`). If it takes `IWebHostEnvironment`, that is also Singleton-safe. Flag for a quick review.

---

### [MEDIUM-3] Antipattern AP006 — missing `CancellationToken` forwarding in several service methods

**Confirmed by:** Roslyn AP006 violations in the antipatterns report across Infrastructure services.
**Confidence:** [Confirmed] — Roslyn output; specific file:line list in the persisted Roslyn JSON at `tool-results/toolu_011UZPssg3o3fvjeTkk1Rfra.json`

**Why it matters:** EF Core async methods accept a `CancellationToken`; not passing one means client disconnects and request timeouts do not abort in-flight DB queries, wasting DB connections. This is a systematic pattern across multiple services, not isolated.

**Fix:** Systematically audit all `ToListAsync()`, `FirstOrDefaultAsync()`, `SaveChangesAsync()` calls that do not pass `ct`. Use `grep -rn 'Async()' src/Accounting.Infrastructure` as a starting point.

---

### [LOW-1] No project-level circular dependencies — confirmed clean

Roslyn `detect_circular_dependencies` returned **0 cycles** at the project scope. The graph is strictly layered:
```
Domain ← Application ← Infrastructure ← Api
                                       ← Workers
```
TestKit correctly has no project references (pure helpers).

---

### [LOW-2] `LoginService` registered as `AddScoped` in Application layer — unusual

**File:** `backend/src/Accounting.Application/DependencyInjection.cs`
**Line:** 11
**Confidence:** [Confirmed]

**Evidence:**
```csharp
services.AddScoped<ILoginService, LoginService>();
```

Application layer registering its own implementation is a minor Clean Architecture smell — the Application layer should define the interface and let Infrastructure (or Api) provide the concrete registration. In this case `LoginService` is likely a pure orchestrator with no EF dependency, which would make Transient or Scoped both fine. Not a bug, but worth noting for future consistency.

---

## Verified SOUND

| Area | Finding |
|---|---|
| **Project dependency graph** | Strictly layered: Domain → Application → Infrastructure → Api/Workers. No upward references. [Confirmed by `get_project_graph`] |
| **Circular dependencies** | Zero cycles at project level. [Confirmed by `detect_circular_dependencies`] |
| **Domain isolation** | `Accounting.Domain.csproj` has NO package references to EF Core, Npgsql, or any persistence library. Comment in .csproj: `<!-- Pure domain layer — NO external dependencies except primitive .NET libs -->`. [Confirmed] |
| **Application isolation** | `Accounting.Application.csproj` references only Domain + FluentValidation + Mapster + Microsoft.Extensions abstractions. No Infrastructure concretions. [Confirmed] |
| **DomainException → ProblemDetails pipeline** | `DomainExceptionMiddleware` correctly maps `DomainException` to RFC-7807 (root/BFF) and `ErrorEnvelope` (v1 API), with consistent code→HTTP-status mapping. Never rethrows raw exceptions to the client in production. [Confirmed] |
| **DI lifetimes — DbContext** | `AccountingDbContext` registered via `AddDbContext` (Scoped by default). All Infrastructure services consuming it are `AddScoped`. No Singleton consuming DbContext was found. [Confirmed] |
| **DI lifetimes — Singletons** | `IClock`, `IPasswordHasher`, `ITotpService`, `IJwtTokenIssuer` are stateless infrastructure primitives — correct as Singletons. [Confirmed] |
| **Tenant isolation wiring** | `ITenantContext` registered `AddScoped<ITenantContext, HttpTenantContext>` in Program.cs. Infrastructure services receive it via constructor injection (Scoped → Scoped, safe). [Confirmed] |
| **async/await discipline** | Outside the four `ContinueWith(t => t.Result)` locations in MasterDataServices, no other `.Result` or `.Wait()` calls were found in `src/` by Grep. [Confirmed] |
| **DateTimeOffset — no `DateTime.Now`** | `DateTime.Now` grep returned zero matches in `src/`. All timestamps observed use `DateTimeOffset.UtcNow`. [Confirmed] |
| **Error handling — no swallowed non-trivial exceptions** | The only empty/silent catch is the `ApiKeyResolver` best-effort touch (CRITICAL-2 above). All other exception handling either rethrows or routes through middleware. [Confirmed] |
| **EF `IEntityTypeConfiguration` dead code** | 54 flagged classes are all legitimate EF configurations discovered via reflection — not genuine dead code. [Confirmed by architecture knowledge; false positives from Roslyn] |
