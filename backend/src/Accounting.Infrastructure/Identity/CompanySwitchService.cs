using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Identity;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Onboarding-switcher spec (2026-06-16). Super-admin only. Validates the target company,
/// resolves its HQ (or first active) branch, re-issues a JWT scoped to that company, and
/// audits the switch. The endpoint is the primary 403 gate (anon→401, authed-non-super→403);
/// this service re-checks <see cref="ITenantContext.IsSuperAdmin"/> as defence in depth.
/// </summary>
public sealed class CompanySwitchService(
    AccountingDbContext db,
    ITenantContext tenant,
    IPermissionLookup permissions,
    IJwtTokenIssuer tokens,
    IActivityRecorder activity) : ICompanySwitchService
{
    public async Task<AccessToken> SwitchAsync(int targetCompanyId, CancellationToken ct)
    {
        // §10 — switching is a super-admin-only privilege; it never grants anything beyond
        // what the caller already holds (the re-issued token keeps is_super_admin = caller's).
        if (!tenant.IsSuperAdmin || tenant.UserId is not { } userId)
            throw new DomainException("auth.forbidden", "Only a super-admin may switch company.");

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
        var branchId = await db.Branches.IgnoreQueryFilters()
            .Where(b => b.CompanyId == targetCompanyId && b.IsActive)
            .OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.BranchId)
            .Select(b => (int?)b.BranchId)
            .FirstOrDefaultAsync(ct) ?? 0;

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
            Permissions: perms));

        // §4.8 — audit the privileged action. activity_log has no RLS (append-only triggers only),
        // so the row's company_id is set deliberately to the target company being switched INTO.
        activity.Record(
            entityType: "company", entityId: targetCompanyId, docNo: null,
            companyId: targetCompanyId, action: "company_switch",
            note: $"super-admin user {userId} switched into company {targetCompanyId} ('{company.NameTh}')",
            module: "identity");
        await db.SaveChangesAsync(ct);

        return token;
    }
}
