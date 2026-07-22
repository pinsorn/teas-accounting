# Design — H4 + M11: intersect MCP OAuth scopes with the user's RBAC

Owner-designed (Opus). Implementer: Sonnet. Reviewer: Opus (auth lens — security-critical).
Source findings: `_review/2026-07-04/opus-verify.md` §H4, §M11.

H4 and M11 **share one primitive** (`granted = scopes ∩ current-RBAC`). Build the shared helper
first (§0), then wire H4 (§1) and M11 (§2). Do not implement them independently.

---

## Context / footguns (read before touching code)

- **Tests connect as a Postgres SUPERUSER → RLS is bypassed** (memory: "RLS masked by superuser
  tests"). Neither H4 nor M11 depends on RLS behaviour — both are *app-layer* RBAC filtering — so a
  superuser-connected integration test is a valid proof here. Do NOT add a `SET ROLE teas` requirement.
- `IPermissionLookup.LoadAsync` (`Accounting.Application/Identity/LoginService.cs:107-111`, impl
  `Accounting.Infrastructure/Identity/PermissionLookup.cs`) **pins `app.company_id` inside its own
  LOCAL transaction** (`set_config(..., true)`, auto-reverts on commit). It is the ONE sanctioned way
  to read a user's effective permissions for a company. Reuse it; invent nothing. It is registered
  **Scoped** (`Accounting.Infrastructure/DependencyInjection.cs:44`).
- `PermissionLookup` opens `BeginTransactionAsync`. Never call it while another explicit transaction
  is open on the same `AccountingDbContext`. (Both call sites here have no ambient tx — safe.)
- The MCP token is built to look **exactly** like an X-Api-Key principal
  (`McpPrincipalFactory.cs:43` `is_api_key=true`, `:44` the `scopes` CSV). `PermissionHandler`
  (`Authorization/PermissionRequirement.cs:20-29`) gates every /mcp tool on **the token's `scopes`
  CSV claim ALONE** — it never consults the user's live RBAC. That is exactly why the scope set baked
  at consent (H4) and re-baked at refresh (M11) MUST already be RBAC-filtered.
- **Two scope representations must stay in sync on the principal.** `McpPrincipalFactory` sets BOTH
  `principal.SetScopes(...)` (the OpenIddict `scope` claim / refresh-token grant set) AND
  `identity.SetClaim(TenantClaims.Scopes, csv)` (the CSV claim `PermissionHandler` actually reads,
  `TenantClaims.Scopes == "scopes"`). Any code that re-derives the grant (M11) MUST update **both**,
  or the CSV (authority-bearing) and the OAuth scopes drift.
- **The token NEVER carries `is_super_admin`** (`McpPrincipalFactory.cs:48`;
  `McpBearerClaimsTransform.cs:35-38` rejects any Bearer principal that does). So M11 cannot read
  super-admin status from the token — it must **reload it from the DB** (which is also more correct:
  it re-checks *current* super status).
- OpenIddict version = **7.5.0** (`backend/Directory.Packages.props:21`). Server registered in
  `Program.cs:106-159` via `AddOpenIddict().AddServer(o => …)`. `UseReferenceRefreshTokens()` (`:123`)
  ⇒ the full principal (company_id/branch_id/scopes) is stored server-side and rehydrated on refresh.

---

## Blast-radius cap

- **Max 7 files.** 3 new source, 2 edits, ≤2 test files. Stop and re-spec if it grows.
  - NEW `backend/src/Accounting.Api/OAuth/McpConsentScopes.cs`
  - NEW `backend/src/Accounting.Api/OAuth/RefreshTokenRevalidationHandler.cs`
  - EDIT `backend/src/Accounting.Api/OAuth/OAuthEndpoints.cs`
  - EDIT `backend/src/Accounting.Api/Program.cs`
  - NEW/EDIT tests under `backend/tests/Accounting.Api.Tests/OAuth/`
- **No DB schema / migration / SqlScript changes.** No EF model changes.
- **No public-API signature changes** to existing types. `McpConsentScopes` is `internal`. The only
  signature touch is adding a DI parameter to the `/oauth/authorize` POST lambda (internal wiring).
- `McpScopes.cs`, `Permissions.cs`, `PermissionCatalog.cs`, `McpPrincipalFactory.cs`,
  `PermissionRequirement.cs` are **read-only references** — do not edit them.

---

## §0 — Shared primitive + the scope↔RBAC parity table (BUILD FIRST)

### Parity finding (evidence-backed)

`McpScopes.All` (`Accounting.Application/Abstractions/McpScopes.cs:11-23`, 18 scopes) vs the RBAC
permission codes in `Permissions.All` (`Accounting.Api/Authorization/Permissions.cs`). **15 of 18 are
byte-identical permission codes; 3 are NOT and need translation:**

| MCP scope (`McpScopes.All`) | RBAC permission required to consent | Note |
|---|---|---|
| `sales.tax_invoice.read` / `.create` | same string | identity |
| `sales.receipt.read` / `.create` | same string | identity |
| `master.customer.read` / `.manage` | same string | identity |
| `master.product.read` / `.manage` | same string | identity |
| `master.vendor.manage` | same string | identity |
| `purchase.purchase_order.read` / `.create` | same string | identity |
| `purchase.vendor_invoice.read` / `.create` | same string | identity |
| `purchase.payment_voucher.read` / `.create` | same string | identity |
| **`sales.quotation.read`** | **`sales.quotation.manage`** | RBAC has NO granular quotation perm (`Permissions.cs:64`); manage covers read+create |
| **`sales.quotation.create`** | **`sales.quotation.manage`** | same |
| **`sys.system_info.read`** | **none** (always grantable) | no such RBAC permission exists; **no MCP tool gates on it** (not in `TeasMcpTools.cs` policy consts) — a harmless public read scope |

**Why a naive `granted.Where(userPerms.Contains)` is WRONG here:** it would strip `sales.quotation.read`,
`sales.quotation.create`, and `sys.system_info.read` from **every** user (nobody holds those exact
permission strings) — breaking legitimate quotation MCP access for users who hold
`sales.quotation.manage`. The translation table above is mandatory, not cosmetic.

> Layering note: `McpScopes` lives in `Accounting.Application` and cannot reference
> `Accounting.Api.Authorization.Permissions`. The mapping is plain strings and both call sites (H4
> endpoint, M11 handler) live in `Accounting.Api`, so the helper belongs in the **Api** layer.

### New file — `backend/src/Accounting.Api/OAuth/McpConsentScopes.cs`

```csharp
namespace Accounting.Api.OAuth;

/// <summary>
/// H4/M11 — filters a normalized MCP grant down to the scopes the CONSENTING/REFRESHING user is
/// actually authorized for, by mapping each scope to the RBAC permission that authorizes it and
/// keeping only the scopes whose mapped permission the user holds. Most scopes ARE their permission
/// code (identity); the exceptions are enumerated (RBAC has no granular quotation perm; system_info
/// is a public read). A super-admin is NOT filtered here — callers short-circuit that (see spec §0).
/// </summary>
internal static class McpConsentScopes
{
    /// <param name="grantedScopes">Already ∩ McpScopes.All (i.e. the result of McpScopes.Normalize).</param>
    /// <param name="userPermissions">The user's EFFECTIVE permission codes for the target company
    /// (IPermissionLookup.LoadAsync .Permissions), as an Ordinal set.</param>
    public static IReadOnlyList<string> FilterToRbac(
        IReadOnlyList<string> grantedScopes, IReadOnlySet<string> userPermissions) =>
        grantedScopes
            .Where(s => RequiredPermission(s) is not { } perm || userPermissions.Contains(perm))
            .ToArray();

    /// <summary>The RBAC permission a user must hold to be granted <paramref name="scope"/>.
    /// <c>null</c> ⇒ no permission required (public read).</summary>
    private static string? RequiredPermission(string scope) => scope switch
    {
        "sales.quotation.read"   => "sales.quotation.manage",
        "sales.quotation.create" => "sales.quotation.manage",
        "sys.system_info.read"   => null,
        _                        => scope,   // identity: scope code == permission code
    };
}
```

### The super-admin decision (KEY — same rule at both call sites)

**Decision: super-admin ⇒ grant all requested VALID scopes (`McpScopes.Normalize` result) WITHOUT
the RBAC filter. Regular user ⇒ intersect via `McpConsentScopes.FilterToRbac`.**

Justification (mirrors how the app authorizes super-admins everywhere else):
- A super-admin holds **no explicit permission rows** for an arbitrary company —
  `PermissionHandler.cs:31-40` bypasses per-permission checks on the `is_super_admin` flag, and
  `CompanySwitchService.cs:49-51` treats a super-admin's loaded perms as *"informational … never a
  privilege source."* So `scopes ∩ perms` would yield **ZERO** scopes for a super-admin and break
  super-admin MCP consent entirely.
- This does **not** widen the token's authority: even a super-admin's MCP token still (a) omits
  `is_super_admin` (`McpPrincipalFactory.cs:48` + `McpBearerClaimsTransform` guard), and (b) is
  capped at `McpScopes.All` (no `*.post/approve/issue/void/…`). A super-admin MCP token can do at
  most what the read+create scope set allows — never the interactive super surface.
- **Super-admin detection differs by call site:**
  - **H4 (consent):** `tenant.IsSuperAdmin` — already computed and used at `OAuthEndpoints.cs:97`.
  - **M11 (refresh):** the token cannot carry it → **reload `User.IsSuperAdmin` from the DB** (§2).

---

## §1 — H4: intersect consent grant with the user's RBAC

**File:** `backend/src/Accounting.Api/OAuth/OAuthEndpoints.cs`, POST `/oauth/authorize` handler.

### Insertion points

1. **Add a DI parameter** to the POST lambda (currently `OAuthEndpoints.cs:78-81`). Append
   `IPermissionLookup permissions` to the parameter list, e.g. after `IActivityRecorder activity`:
   ```csharp
   HttpContext http, ITenantContext tenant, ICompanyService companies,
   AccountingDbContext db, IActivityRecorder activity, IPermissionLookup permissions,
   IOptions<AppOptions> opt, CancellationToken ct) =>
   ```
   Add `using Accounting.Application.Identity;` if not already imported (it is not — add it).

2. **Replace the block at `OAuthEndpoints.cs:111-114`** (the current `Normalize` + empty-guard):
   ```csharp
   // Authoritative grant: requested ∩ McpScopes (structurally drops unknown + every *.post).
   var granted = McpScopes.Normalize(request.GetScopes());
   if (granted.Count == 0)
       return Results.BadRequest(new { error = "invalid_scope" });
   ```
   with:
   ```csharp
   // Authoritative grant: requested ∩ McpScopes (structurally drops unknown + every *.post).
   var granted = McpScopes.Normalize(request.GetScopes());
   if (granted.Count == 0)
       return Results.BadRequest(new { error = "invalid_scope" });

   // H4 — a token may never carry authority the consenting user lacks interactively. A super-admin
   // holds no explicit permission rows (they bypass RBAC everywhere: PermissionHandler.cs:36,
   // CompanySwitchService.cs:49-51), so intersecting would zero their grant — grant them the full
   // valid set instead. A regular user's grant is capped to their effective RBAC for THIS company.
   if (!tenant.IsSuperAdmin)
   {
       var (_, perms) = await permissions.LoadAsync(userId, companyId, ct);
       granted = McpConsentScopes.FilterToRbac(granted, perms.ToHashSet(StringComparer.Ordinal));
       if (granted.Count == 0)
           return Results.BadRequest(new { error = "invalid_scope" });
   }
   ```

Everything downstream (`:118` `McpPrincipalFactory.Build(..., granted, ...)`, `:127-132` audit note,
`:123` offline_access) is unchanged and consumes the now-filtered `granted`.

### Edge cases (H4)

- `userId` and `companyId` are already in scope (`:86`, `:92`) and validated (`userId>0`;
  `companyId>0` + membership at `:97-101`). For a **regular** user, `companyId == tenant.CompanyId`
  is enforced at `:99`, so `LoadAsync(userId, companyId)` reads the user's own-company perms — correct.
- `LoadAsync` self-pins `app.company_id` in a local reverting tx; the outer request's pin is untouched.
  No ambient tx is open here (the handler's only write is `db.SaveChangesAsync` at `:133`, after this).
- A user with only `*.read` requesting `*.create`/`*.manage` → those scopes dropped; if the request
  was *entirely* unauthorized scopes → `granted.Count==0` → `invalid_scope` (no token minted).
- Quotation: a user holding `sales.quotation.manage` who requests `sales.quotation.read`/`.create`
  keeps both (translation table). A user without it loses both.

---

## §2 — M11: revalidate the user on the refresh-token grant

**Extensibility point (OpenIddict 7.5.0):** a custom server event handler on
`OpenIddict.Server.OpenIddictServerEvents.ProcessSignInContext`, registered via
`OpenIddictServerBuilder.AddEventHandler<T>(…).UseScopedHandler<T>()`. This is the event that fires
when OpenIddict signs in the principal it rehydrated from the refresh token — `context.Principal` is
populated and mutable, and `context.Reject(...)` denies the grant. (The token endpoint is NOT passed
through, so this handler — not a minimal endpoint — is the hook, matching the `Program.cs:148` intent.)

### New file — `backend/src/Accounting.Api/OAuth/RefreshTokenRevalidationHandler.cs`

```csharp
using System.Globalization;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.OAuth;

/// <summary>
/// M11 — the refresh-flow hardening handler referenced by Program.cs. On grant_type=refresh_token
/// ONLY, it reloads the subject user and REJECTS the grant if the user is inactive or no longer a
/// member of the token's baked company_id, then re-derives the scope set against the user's CURRENT
/// RBAC (shares H4's McpConsentScopes primitive). Access tokens live ≤10 min, but a refresh token
/// lives 8h absolute — without this a disabled/off-boarded user keeps MCP access for up to 8h.
/// Untouched: authorization-code sign-in (fresh consent is already RBAC-filtered in OAuthEndpoints)
/// and all non-token sign-ins.
/// </summary>
public sealed class RefreshTokenRevalidationHandler
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    private readonly AccountingDbContext _db;
    private readonly IPermissionLookup _permissions;

    public RefreshTokenRevalidationHandler(AccountingDbContext db, IPermissionLookup permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        // Scope strictly to the token endpoint's refresh grant.
        if (context.EndpointType != OpenIddictServerEndpointType.Token ||
            !context.Request.IsRefreshTokenGrantType())
            return;

        var principal = context.Principal;
        if (principal is null) { Reject(context); return; }

        if (!long.TryParse(principal.GetClaim(Claims.Subject), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var userId) || userId <= 0 ||
            !int.TryParse(principal.GetClaim(TenantClaims.CompanyId), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var companyId) || companyId <= 0)
        { Reject(context); return; }

        // Reload the user (global table, no company RLS — same read login does anonymously).
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.IsActive, u.IsSuperAdmin })
            .FirstOrDefaultAsync(context.CancellationToken);
        if (user is null || !user.IsActive) { Reject(context); return; }

        // Split offline_access (an OpenIddict scope, never a tool scope) from the tool scopes.
        var current = principal.GetScopes();                       // ImmutableArray<string>
        var hadOffline = current.Contains(Scopes.OfflineAccess);
        var toolScopes = current.Where(s => s != Scopes.OfflineAccess).ToArray();

        IReadOnlyList<string> granted;
        if (user.IsSuperAdmin)
        {
            // Super still member of an ACTIVE company? (mirror the consent membership rule)
            var companyActive = await _db.Companies.IgnoreQueryFilters()
                .AnyAsync(c => c.CompanyId == companyId && c.IsActive, context.CancellationToken);
            if (!companyActive) { Reject(context); return; }
            granted = McpScopes.Normalize(toolScopes);             // no RBAC filter (see spec §0)
        }
        else
        {
            var (roles, perms) = await _permissions.LoadAsync(userId, companyId, context.CancellationToken);
            if (roles.Count == 0) { Reject(context); return; }     // off-boarded: no active role in company
            granted = McpConsentScopes.FilterToRbac(
                McpScopes.Normalize(toolScopes), perms.ToHashSet(StringComparer.Ordinal));
        }

        if (granted.Count == 0) { Reject(context); return; }

        // Re-bake BOTH scope representations + destinations (keep the CSV claim = the tool authority).
        var finalScopes = hadOffline ? granted.Append(Scopes.OfflineAccess) : granted;
        principal.SetScopes(finalScopes);
        principal.SetClaim(TenantClaims.Scopes, string.Join(',', granted));   // CSV PermissionHandler reads
        principal.SetDestinations(static _ => [Destinations.AccessToken]);

        static void Reject(OpenIddictServerEvents.ProcessSignInContext ctx) => ctx.Reject(
            error: Errors.InvalidGrant,
            description: "The user is inactive or no longer authorized for this company.",
            uri: null);
    }
}
```

### Wiring — `backend/src/Accounting.Api/Program.cs`

Inside `.AddServer(o => { … })` (block `:107-153`), after the passthrough/`UseAspNetCore()`
configuration, register the handler:
```csharp
o.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(builder =>
    builder.UseScopedHandler<RefreshTokenRevalidationHandler>()
           // Run BEFORE OpenIddict prepares the per-token principals so the re-derived scope set
           // lands in the issued access token. Verified empirically by the M11 proving test.
           .SetOrder(int.MinValue + 100_000));
```
Add `using OpenIddict.Server;` to `Program.cs`. Update the stale `:147-149` comment to state the
handler now exists (name it) — remove the "T4 … hooks the server's token-request event" aspirational
wording so the comment matches reality.

> The handler is **scoped** (`UseScopedHandler`) so `AccountingDbContext` + `IPermissionLookup`
> resolve from the request scope. No explicit DI registration of the handler class is required —
> `UseScopedHandler<T>` registers it; but if the build reports it unresolved, add
> `builder.Services.AddScoped<RefreshTokenRevalidationHandler>();` next to the OpenIddict block.

### Edge cases / footguns (M11)

- **Ordering is the one live risk.** If the proving test shows the mutated scope did NOT reach the
  issued token, the handler ran too late — lower the order further (it must precede
  OpenIddict's `Prepare*TokenPrincipal`). The empirical test is the gate, not inspection.
- `principal.SetClaim` (OpenIddict extension) **replaces** the existing `scopes` claim — do not also
  `RemoveClaim`, and do not leave the old CSV in place (that is the authority claim).
- `_db.Users` is global (login reads it anonymously) → no `app.company_id` pin needed for the user
  reload. `_permissions.LoadAsync` handles its own company pin for the role/perm read.
- Do NOT touch the authorization-code path: `context.Request.IsRefreshTokenGrantType()` is false for
  code exchange, so fresh consents (already filtered in §1) pass through untouched.
- `context.Reject` surfaces as an OAuth `invalid_grant` error to the client — the correct signal for
  a revoked/no-longer-valid grant; the reference refresh-token family is not reissued.

---

## Verification gates (run in order; all must pass)

1. `dotnet build backend/Accounting.sln -c Debug` → 0 errors, 0 new warnings.
2. `dotnet test backend/tests/Accounting.Api.Tests` filtered to `OAuth` + `Mcp` → green, and the
   pre-existing `McpScopesTests` / `BearerMcpRoundTripTests` / `McpBearerClaimsTransformTests` still pass
   (no regression to the Normalize/*.post invariants).
3. Both new proving tests (below) pass.
4. `grep "ম"` on changed files before commit (memory: Thai ม / Bengali ম glyph pitfall) — N/A for
   ASCII-only code, but run it on any Thai string you add (none expected here).

## Proving tests (one per finding — REQUIRED)

Place under `backend/tests/Accounting.Api.Tests/OAuth/`. Mirror the harness in the existing
`BearerMcpRoundTripTests.cs` (full authorize→token→/mcp round-trip) and `McpScopesTests.cs` (pure unit).

- **§0 unit — `McpConsentScopesTests`** (fast, no DB):
  - user perms `{sales.tax_invoice.read}`, scopes `{sales.tax_invoice.read, sales.tax_invoice.create}`
    → result = `{sales.tax_invoice.read}` only.
  - user perms `{sales.quotation.manage}`, scopes `{sales.quotation.read, sales.quotation.create}`
    → both kept (translation).
  - any perms, scopes `{sys.system_info.read}` → kept (null-permission mapping).
  - empty perms, scopes `{master.customer.manage}` → empty.
- **H4 integration** (`BearerMcpRoundTripTests` style): seed a user whose role grants ONLY
  `sales.tax_invoice.read` (NOT `.create`) in company C; drive POST `/oauth/authorize` (approve,
  company_id=C) requesting `sales.tax_invoice.read sales.tax_invoice.create`; exchange the code;
  **assert the issued access token's `scopes` claim contains `sales.tax_invoice.read` and NO
  `*.create` / `*.manage`.** Add a super-admin variant: super consenting for an active company with
  the same request keeps `sales.tax_invoice.create` (super not filtered).
- **M11 integration**: complete a consent to obtain a refresh token; flip the user `IsActive=false`
  (persist); POST the token endpoint with `grant_type=refresh_token` → **assert the response is an
  OAuth error (`invalid_grant`), not a new access token.** Add a second case: user stays active but
  their `sales.tax_invoice.create` permission is revoked → refresh succeeds but the new token's
  `scopes` claim no longer contains `sales.tax_invoice.create` (proves re-derivation + ordering).

---

## OPEN QUESTIONS (do not guess — raise to Fable if hit)

1. **`context.Reject` overload / `SetOrder` value.** The exact `Reject(error, description, uri)`
   overload and whether `int.MinValue + 100_000` is early enough are OpenIddict-7.5 specifics —
   confirm via IntelliSense + the M11 proving test. If ordering cannot be made to work as an event
   handler, the sanctioned **fallback** is to `EnableTokenEndpointPassthrough()` and add a minimal
   `/oauth/token` endpoint that authenticates the refresh principal, runs the identical
   reload+filter, and re-`Results.SignIn`s — mirroring the existing `/oauth/authorize` passthrough
   (proven in-repo pattern). Prefer the event handler (matches the Program.cs intent); use the
   fallback only if the handler cannot be wired.
2. **Membership signal for regular users.** This design treats "has ≥1 active `UserRole` in the baked
   company" (`LoadAsync` returns a non-empty `Roles`) as membership. If the product has a membership
   concept independent of roles (a user who is a member but holds no role), confirm before relying on
   `roles.Count == 0` as the off-boarded test. No such separate table was found in the code read.
3. **`sys.system_info.read` has no gated tool today.** Mapping it to "always grantable" is safe now.
   If a future MCP tool is gated on it and should require a permission, revisit the translation table.

---

## RESOLUTION (implemented 2026-07-04, Sonnet — see `fix-review-findings-2026-07-04.md` Unit-3)

1. **Resolved empirically, no fallback needed.** `SetOrder(int.MinValue + 100_000)` worked on the
   FIRST test run — the M11 revoke-permission proving test showed the re-derived scopes correctly
   reaching the issued access token. `context.Reject(error:, description:, uri:)` is the
   `BaseValidatingContext.Reject(string,string,string)` overload (confirmed via the OpenIddict.Server
   7.5.0 NuGet package XML docs) — compiles and behaves as designed. The token-endpoint-passthrough
   fallback was never needed.
2. **Resolved as proposed.** Used `roles.Count == 0` (from `PermissionLookup.LoadAsync`) as the
   off-boarded signal. No separate membership concept exists in the code.
3. Unchanged — no future work triggered this pass.

**Plumbing gap the design didn't anticipate:** `McpConsentScopesTests` (the §0 proving test) needs to
call the `internal McpConsentScopes.FilterToRbac` from the separate `Accounting.Api.Tests` assembly,
but no `InternalsVisibleTo` existed anywhere in the repo. Added `Accounting.Api/AssemblyInfo.cs` with
`[assembly: InternalsVisibleTo("Accounting.Api.Tests")]` as the 3rd new-source file (the blast-radius
cap already budgeted "3 new source" while naming only 2).

**Testing method note:** OpenIddict access tokens are encrypted JWEs (5-segment) — not decodable
client-side in a black-box test. The H4/M11 integration tests prove scope reach BEHAVIORALLY via
real `/mcp` tool calls (a missing scope hides the tool from `ListToolsAsync` and an explicit call
throws), mirroring `McpServerSmokeTests.Mcp_key_with_read_only_scopes_hides_create_tools`.
