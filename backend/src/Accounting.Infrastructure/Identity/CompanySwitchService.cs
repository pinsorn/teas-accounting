using System.Security.Claims;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Identity;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Onboarding-switcher spec (2026-06-16). Super-admin only. Validates the target company,
/// resolves its HQ (or first active) branch, re-issues a JWT scoped to that company, and
/// audits the switch. The endpoint is the primary 403 gate (anon→401, authed-non-super→403);
/// this service re-checks <see cref="ITenantContext.IsSuperAdmin"/> as defence in depth.
///
/// 2026-07 review fix F-A — ALSO the authoritative enforcement point for the WP2.1 absolute
/// session cap (<see cref="AbsoluteSessionCap"/>): re-issuing a token here without checking it
/// let a super-admin (or a stolen super-admin cookie) slide a session past
/// AbsoluteSessionCapHours forever by repeatedly switching company. The check runs here (not
/// only at the endpoint) so no future caller of <see cref="ICompanySwitchService"/> can skip
/// it, and the ORIGINAL auth_time is carried forward onto the re-issued token instead of being
/// reset to "now".
/// </summary>
public sealed class CompanySwitchService(
    AccountingDbContext db,
    ITenantContext tenant,
    IPermissionLookup permissions,
    IJwtTokenIssuer tokens,
    IActivityRecorder activity,
    ClaimsPrincipal principal,
    IClock clock,
    IOptionsMonitor<JwtOptions> jwtOptions) : ICompanySwitchService
{
    public async Task<AccessToken> SwitchAsync(int targetCompanyId, CancellationToken ct)
    {
        // §10 — switching is a super-admin-only privilege; it never grants anything beyond
        // what the caller already holds (the re-issued token keeps is_super_admin = caller's).
        if (!tenant.IsSuperAdmin || tenant.UserId is not { } userId)
            throw new DomainException("auth.forbidden", "Only a super-admin may switch company.");

        // F-A — same absolute-cap check /auth/refresh enforces, before we ever mint a token.
        // Throws SessionAbsoluteCapExceededException (caught by the endpoint → 403) rather than
        // silently re-issuing a fresh full-TTL token past the cap.
        var authTime = AbsoluteSessionCap.CheckOrThrow(
            principal, clock, jwtOptions.CurrentValue.AbsoluteSessionCapHours);

        // Target must exist AND be active. IgnoreQueryFilters + the explicit company predicate:
        // the global filter is bypassed for super-admin anyway, but the predicate keeps the read
        // pinned to the requested tenant (never an arbitrary row). §4.7.
        var company = await db.Companies.IgnoreQueryFilters()
            .Where(c => c.CompanyId == targetCompanyId && c.IsActive)
            .Select(c => new { c.CompanyId, c.NameTh })
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("company.not_found",
                $"Company {targetCompanyId} not found or inactive.");

        // HQ-first, then first active branch of the TARGET company (explicit company predicate —
        // IgnoreQueryFilters strips the tenant filter, so we re-pin it here). Falls back to 0 when
        // the company has no active branch (mirrors LoginService's no-assignment path).
        // master.branches carries RLS (G2, 600_superadmin_scoped_rls.sql) keyed on the SESSION's
        // pinned app.company_id, which is the CALLER's current company, not necessarily the
        // target being switched INTO — so this cross-company read needs its own LOCAL-only
        // app.bypass_rls pin (never derived from a user's identity, auto-reverts at commit).
        int branchId;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.bypass_rls', 'true', true)", ct);
            branchId = await db.Branches.IgnoreQueryFilters()
                .Where(b => b.CompanyId == targetCompanyId && b.IsActive)
                .OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.BranchId)
                .Select(b => (int?)b.BranchId)
                .FirstOrDefaultAsync(ct) ?? 0;
            await tx.CommitAsync(ct);
        }

        // Perms for the target company (mirrors login). Super-admin gets the PermissionHandler
        // bypass regardless, so this is informational on the token, never a privilege source.
        var (roles, perms) = await permissions.LoadAsync(userId, targetCompanyId, ct);

        var token = tokens.Issue(new TokenClaims(
            UserId: userId,
            Username: tenant.Username ?? string.Empty,
            CompanyId: targetCompanyId,
            BranchId: branchId,
            IsSuperAdmin: true,
            Roles: roles,
            Permissions: perms,
            AuthTime: authTime));

        // §4.8 — audit the privileged action. The row's company_id is set deliberately to the
        // target company being switched INTO. audit.activity_log carries RLS (G3, since 585) keyed
        // on the SESSION's pinned company_id — the caller's current one, not the target — so this
        // cross-company write needs the same LOCAL app.bypass_rls pin as the branch read above.
        activity.Record(
            entityType: "company", entityId: targetCompanyId, docNo: null,
            companyId: targetCompanyId, action: "company_switch",
            note: $"super-admin user {userId} switched into company {targetCompanyId} ('{company.NameTh}')",
            module: "identity");
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.bypass_rls', 'true', true)", ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return token;
    }
}
