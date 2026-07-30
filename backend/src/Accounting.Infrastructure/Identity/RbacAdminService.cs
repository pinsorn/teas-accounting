using System.Text.RegularExpressions;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Audit;
using Accounting.Application.Identity;
using Accounting.Application.Pdf;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Identity;

/// <summary>
/// Sprint 13k — per-company RBAC admin service. Role / RolePermission are NOT
/// ITenantOwned, so every query filters company_id EXPLICITLY (mirrors
/// BusinessUnitService). A concrete company_id filter also naturally excludes the
/// system-global SUPER_ADMIN row (company_id IS NULL). §4.7 multi-tenant isolation,
/// §4.8 audit trail.
/// </summary>
public sealed class RbacAdminService(
    AccountingDbContext db, ITenantContext tenant, IActivityRecorder activity, IPasswordHasher hasher,
    IAttachmentService attachments)
    : IRbacAdminService
{
    private const string SuperAdmin = Role.SystemRoles.SuperAdmin;

    // Mirror the first-run bootstrap rules so every user-creation path is consistent.
    private const int MinUsernameLen = 3, MaxUsernameLen = 64, MinPasswordLen = 12;
    private static readonly Regex UsernameRx = new("^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);

    /// <summary>Compliance-critical (§4.7). Non-super-admins may only touch their own
    /// company; a mismatching explicit request is a scope violation (→ 403 on /api/v1).
    /// Super-admins act AS the chosen company (default: their own).</summary>
    private int ResolveTargetCompany(int? requested)
    {
        if (tenant.IsSuperAdmin) return requested ?? tenant.CompanyId;
        if (requested is not null && requested != tenant.CompanyId)
            throw new DomainException("rbac.cross_company.scope_required",
                "You may only manage your own company.");
        return tenant.CompanyId;
    }

    /// <summary>Role / RolePermission are NOT ITenantOwned (no EF filter) and now carry a G3
    /// <c>company_isolation</c> RLS policy (<c>company_id IS NULL OR company_id = pinned OR
    /// bypass_rls</c>) — same for <c>audit.activity_log</c>. A super-admin managing a DIFFERENT
    /// company than the one pinned on the session (or looking a role up BY id, whose owning
    /// company isn't known until AFTER the read — chicken-and-egg) needs the LOCAL-only
    /// <c>app.bypass_rls</c> escape hatch, never a data-scope grant. Wraps a method's cross-company
    /// DB work in one short transaction; <c>is_local=true</c> auto-reverts at commit/rollback, so
    /// it never leaks onto the pooled connection. See specs/superadmin-tenant-scope.md D1(B)/D2.</summary>
    private async Task<T> RunWithBypassAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.bypass_rls', 'true', true)", ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    private Task RunWithBypassAsync(Func<Task> work, CancellationToken ct) =>
        RunWithBypassAsync(async () => { await work(); return true; }, ct);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- Phase A: read -----------------------------------------------------

    public Task<IReadOnlyList<RoleListItem>> ListRolesAsync(int? companyId, CancellationToken ct)
    {
        var target = ResolveTargetCompany(companyId);
        var today = Today;

        // Concrete company filter excludes SUPER_ADMIN (company_id NULL). UserCount =
        // distinct users with an ACTIVE user_role for the role in this company; the
        // active predicate is inlined (UserRole.IsActiveOn won't translate to SQL).
        // sys.roles/sys.role_permissions carry RLS (G3) — a super-admin listing a DIFFERENT
        // company's roles than the one pinned on the session needs the LOCAL bypass.
        return RunWithBypassAsync<IReadOnlyList<RoleListItem>>(async () => await db.Roles.AsNoTracking()
            .Where(r => r.CompanyId == target)
            .OrderBy(r => r.RoleCode)
            .Select(r => new RoleListItem(
                r.RoleId, r.RoleCode, r.RoleName, r.Description, r.IsSystem,
                db.UserRoles.Where(ur => ur.RoleId == r.RoleId
                        && ur.CompanyId == target
                        && ur.ValidFrom <= today
                        && (ur.ValidTo == null || ur.ValidTo >= today))
                    .Select(ur => ur.UserId).Distinct().Count(),
                db.RolePermissions.Count(rp => rp.RoleId == r.RoleId)))
            .ToListAsync(ct), ct);
    }

    public Task<RoleDetail> GetRoleAsync(int roleId, CancellationToken ct) =>
        RunWithBypassAsync(async () =>
        {
            // Load by id, then scope-check (mirrors the write methods). A super-admin may
            // open any company's role; a regular admin is pinned to their own company and a
            // mismatch returns .not_found so a cross-company id leaks nothing. SUPER_ADMIN
            // (company_id NULL) is never surfaced through this endpoint.
            // The lookup is BY id — the owning company isn't known until AFTER the read, so it
            // can't be pre-pinned; sys.roles/sys.role_permissions carry RLS (G3) → LOCAL bypass.
            var role = await db.Roles.AsNoTracking()
                .Where(r => r.RoleId == roleId && r.CompanyId != null)
                .Select(r => new { r.RoleId, r.CompanyId, r.RoleCode, r.RoleName, r.Description, r.IsSystem })
                .FirstOrDefaultAsync(ct)
                ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

            if (!tenant.IsSuperAdmin && role.CompanyId != tenant.CompanyId)
                throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

            var codes = await db.RolePermissions.AsNoTracking()
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => rp.Permission!.PermissionCode)
                .OrderBy(c => c)
                .ToArrayAsync(ct);

            return new RoleDetail(role.RoleId, role.CompanyId, role.RoleCode, role.RoleName,
                role.Description, role.IsSystem, codes);
        }, ct);

    // ---- Phase B: write ----------------------------------------------------

    public Task SetRolePermissionsAsync(int roleId, SetRolePermissionsRequest req, CancellationToken ct) =>
        // Role lookup is BY id (owning company unknown until after the read) and every write
        // below touches sys.roles/sys.role_permissions/audit.activity_log — all RLS (G3) →
        // LOCAL bypass for the whole operation (superadmin-tenant-scope.md D1(B)).
        RunWithBypassAsync(async () =>
        {
            var role = await db.Roles
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.RoleId == roleId, ct)
                ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

            GuardEditable(role);
            ResolveTargetCompany(role.CompanyId);   // cross-company → scope_required

            var requested = (req.PermissionCodes ?? []).Distinct().ToArray();

            // Resolve codes → ids; every code MUST exist in the catalog.
            var known = await db.Permissions.AsNoTracking()
                .Where(p => requested.Contains(p.PermissionCode))
                .Select(p => new { p.PermissionId, p.PermissionCode })
                .ToListAsync(ct);
            if (known.Count != requested.Length)
            {
                var unknown = requested.Except(known.Select(k => k.PermissionCode)).ToArray();
                throw new DomainException("rbac.unknown_permission",
                    $"Unknown permission code(s): {string.Join(", ", unknown)}");
            }

            var existingCodes = await db.RolePermissions.AsNoTracking()
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => rp.Permission!.PermissionCode)
                .ToListAsync(ct);

            var added = requested.Except(existingCodes).OrderBy(c => c).ToArray();
            var removed = existingCodes.Except(requested).OrderBy(c => c).ToArray();
            if (added.Length == 0 && removed.Length == 0) return;   // no-op

            // Whole-set replace: drop all, re-add the requested set with the role's company.
            var current = await db.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .ToListAsync(ct);
            db.RolePermissions.RemoveRange(current);
            foreach (var p in known)
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    PermissionId = p.PermissionId,
                    CompanyId = role.CompanyId,   // denormalized owning company
                });

            activity.Record("role", roleId, null, role.CompanyId!.Value, "rbac_grant_change",
                note: DiffNote(added, removed), module: "sys");
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task<int> CreateRoleAsync(CreateRoleRequest req, CancellationToken ct)
    {
        var target = ResolveTargetCompany(req.CompanyId);
        var code = (req.RoleCode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("rbac.role_code_required", "Role code is required.");
        if (string.Equals(code, SuperAdmin, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("rbac.super_admin_locked", "SUPER_ADMIN is reserved.");

        // sys.roles/audit.activity_log carry RLS (G3) — a super-admin creating a role in a
        // DIFFERENT company than the one pinned on the session needs the LOCAL bypass.
        return RunWithBypassAsync(async () =>
        {
            if (await db.Roles.AnyAsync(r => r.CompanyId == target && r.RoleCode == code, ct))
                throw new DomainException("rbac.role_code_duplicate",
                    $"Role code '{code}' already exists in this company.");

            var role = new Role
            {
                CompanyId = target,
                RoleCode = code,
                RoleName = req.NameTh,
                Description = req.Description,
                IsSystem = false,
            };
            db.Roles.Add(role);
            await db.SaveChangesAsync(ct);   // assign RoleId before auditing

            activity.Record("role", role.RoleId, null, target, "role_created",
                note: $"code={code}", module: "sys");
            await db.SaveChangesAsync(ct);
            return role.RoleId;
        }, ct);
    }

    public Task UpdateRoleAsync(int roleId, UpdateRoleRequest req, CancellationToken ct) =>
        // Role lookup is BY id (owning company unknown until after the read); the UPDATE + audit
        // write both touch RLS'd (G3) tables → LOCAL bypass for the whole operation.
        RunWithBypassAsync(async () =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId, ct)
                ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

            GuardEditable(role);                    // SUPER_ADMIN / null-company refused
            ResolveTargetCompany(role.CompanyId);   // cross-company → scope_required

            role.RoleName = req.NameTh;             // rename only — never role_code / company
            role.Description = req.Description;

            activity.Record("role", roleId, null, role.CompanyId!.Value, "role_updated",
                note: $"name={req.NameTh}", module: "sys");
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task DeleteRoleAsync(int roleId, CancellationToken ct) =>
        // Same RLS shape as UpdateRoleAsync — LOCAL bypass for the whole operation.
        RunWithBypassAsync(async () =>
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId, ct)
                ?? throw new DomainException("rbac.role.not_found", $"Role {roleId} not found.");

            GuardEditable(role);
            ResolveTargetCompany(role.CompanyId);

            if (role.IsSystem)
                throw new DomainException("rbac.role_is_system", "System roles cannot be deleted.");

            var today = Today;
            var inUse = await db.UserRoles.AnyAsync(ur => ur.RoleId == roleId
                && ur.ValidFrom <= today
                && (ur.ValidTo == null || ur.ValidTo >= today), ct);
            if (inUse)
                throw new DomainException("rbac.role_in_use",
                    "Role is assigned to active users and cannot be deleted.");

            // Hard delete — grants cascade. Audit BEFORE the row vanishes.
            activity.Record("role", roleId, null, role.CompanyId!.Value, "role_deleted",
                note: $"code={role.RoleCode}", module: "sys");
            db.Roles.Remove(role);
            await db.SaveChangesAsync(ct);
        }, ct);

    // ---- Phase C: user-role assignment -------------------------------------

    public Task<IReadOnlyList<UserListItem>> ListUsersAsync(int? companyId, CancellationToken ct)
    {
        var target = ResolveTargetCompany(companyId);

        // The role-join below (ur.Role!.RoleCode etc.) reads sys.roles, which carries RLS (G3) —
        // a super-admin listing a DIFFERENT company's users needs the LOCAL bypass so the join
        // isn't silently filtered.
        return RunWithBypassAsync(async () =>
        {
            // "Users in company X" = users with ≥1 user_role in this company; list their
            // roles SCOPED to this company (excludes SUPER_ADMIN via the company filter).
            var userIds = await db.UserRoles.AsNoTracking()
                .Where(ur => ur.CompanyId == target)
                .Select(ur => ur.UserId)
                .Distinct()
                .ToListAsync(ct);

            var users = await db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.UserId))
                .OrderBy(u => u.Username)
                .Select(u => new { u.UserId, u.Username, u.FullName, u.IsActive, u.IsSuperAdmin, u.Position })
                .ToListAsync(ct);

            // doc-signature spec (§F2.10) — latest-wins signature attachment per user, in ONE
            // grouped query (no N+1). Mirrors AttachmentService.ListAsync's join style.
            // §16 F4 (Tier-2 remediation) — sys.attachments is a G1 FORCE-RLS table with NO
            // bypass arm (§1.4): inside RunWithBypassAsync's app.bypass_rls transaction, a read
            // here still filters by the SESSION's own app.company_id, not `target` — so when a
            // super-admin lists a DIFFERENT company than the one pinned on this session
            // (target != tenant.CompanyId), this query would silently resolve against the WRONG
            // company's attachments (never reliably right). Only resolve when listing the
            // session's own company; otherwise the column is simply not shown.
            var sigByUser = target == tenant.CompanyId
                ? (await db.Attachments.AsNoTracking()
                        .Where(a => a.ParentType == AttachmentParentType.UserSignature
                            && userIds.Contains(a.ParentId) && a.DeletedAt == null)
                        .OrderByDescending(a => a.UploadedAt)
                        .ThenByDescending(a => a.AttachmentId)   // §16 F5 — deterministic tiebreak
                        .Select(a => new { a.ParentId, a.AttachmentId })
                        .ToListAsync(ct))
                    .GroupBy(a => a.ParentId)
                    .ToDictionary(g => g.Key, g => g.First().AttachmentId)
                : new Dictionary<long, long>();

            // Roles per user, scoped to the target company. Join through user_roles so we
            // only surface the company-scoped roles (a user may have roles in other companies).
            var roleRows = await db.UserRoles.AsNoTracking()
                .Where(ur => ur.CompanyId == target && userIds.Contains(ur.UserId))
                .Select(ur => new { ur.UserId, ur.Role!.RoleId, ur.Role.RoleCode, ur.Role.RoleName })
                .ToListAsync(ct);

            var rolesByUser = roleRows
                .GroupBy(r => r.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.DistinctBy(r => r.RoleId)
                          .OrderBy(r => r.RoleCode)
                          .Select(r => new RoleRef(r.RoleId, r.RoleCode, r.RoleName))
                          .ToArray());

            return (IReadOnlyList<UserListItem>)users
                .Select(u => new UserListItem(u.UserId, u.Username, u.FullName, u.IsActive, u.IsSuperAdmin,
                    rolesByUser.TryGetValue(u.UserId, out var rr) ? rr : [],
                    u.Position,
                    sigByUser.TryGetValue(u.UserId, out var attId) ? $"/attachments/{attId}/download" : null))
                .ToList();
        }, ct);
    }

    public Task SetUserRolesAsync(long userId, SetUserRolesRequest req, CancellationToken ct) =>
        // Target = the company whose role-set we're editing for this user. Reads sys.roles and
        // writes audit.activity_log — both RLS (G3) → LOCAL bypass for the whole operation.
        RunWithBypassAsync(async () =>
        {
            // Super-admins may pass any company (cross-company management); company-admins are
            // pinned to their own (a foreign id → rbac.cross_company.scope_required). §4.7.
            var target = ResolveTargetCompany(req.CompanyId);

            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
                ?? throw new DomainException("rbac.user.not_found", $"User {userId} not found.");

            var requestedIds = (req.RoleIds ?? []).Distinct().ToArray();

            // Anti-lockout (§4.7 compliance): an admin must not strip their own last role
            // in their own company — that would lock them out of administration.
            bool isSelf = tenant.UserId == userId && tenant.CompanyId == target;
            if (isSelf && requestedIds.Length == 0)
                throw new DomainException("rbac.self_lockout",
                    "You cannot remove all of your own roles.");

            // Every requested role MUST belong to the target company; SUPER_ADMIN is never assignable.
            var validRoles = await db.Roles.AsNoTracking()
                .Where(r => requestedIds.Contains(r.RoleId) && r.CompanyId == target)
                .Select(r => new { r.RoleId, r.RoleCode })
                .ToListAsync(ct);
            if (validRoles.Count != requestedIds.Length)
                throw new DomainException("rbac.role_company_mismatch",
                    "One or more roles do not belong to this company.");

            // Whole-set replace of this user's PER-COMPANY role assignments for the target company.
            // CRITICAL (anti-lockout, §4.7): scope by the ROLE's company (ur.Role.CompanyId == target),
            // NOT just ur.CompanyId. A super-admin's user_role -> SUPER_ADMIN row has ur.CompanyId = the
            // company but role.company_id IS NULL; the replacement set (per-company roles only) can never
            // re-include SUPER_ADMIN, so deleting by ur.CompanyId alone would silently strip that user's
            // system-global assignment (their company context). Leave global rows untouched.
            var existing = await db.UserRoles
                .Where(ur => ur.UserId == userId && ur.CompanyId == target && ur.Role!.CompanyId == target)
                .ToListAsync(ct);
            var beforeCodes = await db.UserRoles.AsNoTracking()
                .Where(ur => ur.UserId == userId && ur.CompanyId == target && ur.Role!.CompanyId == target)
                .Select(ur => ur.Role!.RoleCode)
                .OrderBy(c => c)
                .ToListAsync(ct);

            db.UserRoles.RemoveRange(existing);
            var today = Today;
            foreach (var r in validRoles)
                db.UserRoles.Add(new UserRole
                {
                    UserId = userId,
                    RoleId = r.RoleId,
                    CompanyId = target,
                    BranchId = 0,           // 0 = all branches in this company
                    ValidFrom = today,
                    ValidTo = null,
                });

            var afterCodes = validRoles.Select(r => r.RoleCode).OrderBy(c => c).ToArray();
            activity.Record("user", userId, user.Username, target, "user_role_change",
                note: $"[{string.Join(",", beforeCodes)}] -> [{string.Join(",", afterCodes)}]",
                module: "sys");
            await db.SaveChangesAsync(ct);
        }, ct);

    // ---- Phase D: user lifecycle -------------------------------------------

    public Task<long> CreateUserAsync(CreateUserRequest req, CancellationToken ct) =>
        // Target company may differ from the one pinned on the session (super-admin cross-company
        // onboarding). Reads sys.roles and writes audit.activity_log — both RLS (G3) → LOCAL bypass.
        RunWithBypassAsync(async () =>
        {
            // The new user JOINS this company (its roles get assigned). Super-admins target any
            // company; company-admins are pinned to their own (cross-company → scope_required, §4.7).
            var target = ResolveTargetCompany(req.CompanyId);

            var username = (req.Username ?? string.Empty).Trim();
            if (username.Length < MinUsernameLen || username.Length > MaxUsernameLen || !UsernameRx.IsMatch(username))
                throw new DomainException("user.username_invalid",
                    $"Username must be {MinUsernameLen}-{MaxUsernameLen} chars (letters, digits, . _ -).");
            if ((req.Password ?? string.Empty).Length < MinPasswordLen)
                throw new DomainException("user.password_too_short",
                    $"Password must be at least {MinPasswordLen} characters.");
            if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username, ct))
                throw new DomainException("user.username_duplicate", $"Username '{username}' already exists.");

            // Every requested role MUST belong to the target company (SUPER_ADMIN is company NULL →
            // never matches → never assignable here; the new user is never a super-admin).
            var requestedRoleIds = (req.RoleIds ?? []).Distinct().ToArray();
            var validRoles = await db.Roles.AsNoTracking()
                .Where(r => requestedRoleIds.Contains(r.RoleId) && r.CompanyId == target)
                .Select(r => new { r.RoleId, r.RoleCode })
                .ToListAsync(ct);
            if (validRoles.Count != requestedRoleIds.Length)
                throw new DomainException("rbac.role_company_mismatch",
                    "One or more roles do not belong to this company.");

            var now = DateTimeOffset.UtcNow;
            var user = new User
            {
                Username = username,
                Email = string.IsNullOrWhiteSpace(req.Email) ? $"{username}@teas.local" : req.Email.Trim(),
                PasswordHash = hasher.Hash(req.Password!),
                FullName = string.IsNullOrWhiteSpace(req.FullName) ? username : req.FullName.Trim(),
                IsSuperAdmin = false,         // ม.: never mint a super-admin here (bootstrap-only)
                IsActive = req.IsActive,
                FailedLoginCount = 0,
                MustChangePassword = false,
                CreatedAt = now, UpdatedAt = now, Version = 0,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);   // assign UserId before role rows + audit

            var today = Today;
            foreach (var r in validRoles)
                db.UserRoles.Add(new UserRole
                {
                    UserId = user.UserId, RoleId = r.RoleId, CompanyId = target,
                    BranchId = 0, ValidFrom = today, ValidTo = null,
                });

            activity.Record("user", user.UserId, username, target, "user_created",
                note: $"roles=[{string.Join(",", validRoles.Select(r => r.RoleCode).OrderBy(c => c))}]",
                module: "sys");
            await db.SaveChangesAsync(ct);
            return user.UserId;
        }, ct);

    public Task SetUserActiveAsync(long userId, bool isActive, CancellationToken ct) =>
        // audit.activity_log carries RLS (G3) — the audit write below needs the LOCAL bypass
        // whenever a super-admin is acting on a company other than the one pinned on the session.
        RunWithBypassAsync(async () =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
                ?? throw new DomainException("rbac.user.not_found", $"User {userId} not found.");
            await GuardManageUserAsync(user, ct);

            // Anti-lockout (§4.7): never let an admin disable their own account.
            if (!isActive && tenant.UserId == userId)
                throw new DomainException("rbac.self_lockout", "You cannot deactivate your own account.");

            if (user.IsActive == isActive) return;   // no-op
            user.IsActive = isActive;
            activity.Record("user", userId, user.Username, tenant.CompanyId,
                isActive ? "user_activated" : "user_deactivated", module: "sys");
            await db.SaveChangesAsync(ct);
        }, ct);

    public Task ResetUserPasswordAsync(long userId, string newPassword, CancellationToken ct) =>
        // Same RLS shape as SetUserActiveAsync — LOCAL bypass around the audit write.
        RunWithBypassAsync(async () =>
        {
            if ((newPassword ?? string.Empty).Length < MinPasswordLen)
                throw new DomainException("user.password_too_short",
                    $"Password must be at least {MinPasswordLen} characters.");

            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
                ?? throw new DomainException("rbac.user.not_found", $"User {userId} not found.");
            await GuardManageUserAsync(user, ct);

            user.PasswordHash = hasher.Hash(newPassword!);
            user.PasswordChangedAt = DateTimeOffset.UtcNow;
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            // NEVER log the password — only that a reset happened.
            activity.Record("user", userId, user.Username, tenant.CompanyId, "user_password_reset", module: "sys");
            await db.SaveChangesAsync(ct);
        }, ct);

    // doc-signature spec (§E5) — admin-managed signature + ตำแหน่ง. Same GuardManageUserAsync as
    // SetUserActiveAsync/ResetUserPasswordAsync: a company-admin may act on a peer of their own
    // company but never on a super-admin (AttachmentService.ParentExistsAsync's UserRole.CompanyId
    // check alone would not stop that — a super-admin can carry a company-scoped UserRole row too).
    // §16 F2 (Tier-2 remediation) — RunWithBypassAsync + activity.Record, matching the
    // SetUserActiveAsync/ResetUserPasswordAsync sibling shape: a signature/position change prints
    // on legal documents, so it needs the same audit trail (and the same RLS-bypass rationale —
    // audit.activity_log carries RLS (G3), needed whenever a super-admin acts cross-company).
    public Task<string> SetUserSignatureAsync(
        long userId, string fileName, string mimeType, long sizeBytes, Stream content,
        CancellationToken ct) =>
        RunWithBypassAsync(async () =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
                ?? throw new DomainException("rbac.user.not_found", $"User {userId} not found.");
            await GuardManageUserAsync(user, ct);

            SignatureImage.Validate(mimeType, sizeBytes, "user.signature");
            // Tier-2 finding (2026-07-30, MED) — the MIME string alone can lie; verify the real
            // magic number before persisting, so a spoofed Content-Type is rejected with the
            // existing *.bad_mime error at upload time (better UX than a silently-blank box later).
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (!SignatureImage.HasValidImageMagic(bytes))
                throw new DomainException("user.signature.bad_mime",
                    "File content does not match an allowed image format (png/jpeg/webp).");

            var uploaded = await attachments.UploadAsync(
                "USER_SIGNATURE", userId, "OTHER", "User signature",
                fileName, mimeType, bytes.Length, new MemoryStream(bytes), ct);

            activity.Record("user", userId, user.Username, tenant.CompanyId, "user_signature_uploaded",
                note: $"attachment_id={uploaded.AttachmentId}", module: "sys");
            await db.SaveChangesAsync(ct);   // persists the audit row (UploadAsync already saved the attachment)
            return $"/attachments/{uploaded.AttachmentId}/download";
        }, ct);

    public Task SetUserProfileAsync(long userId, string? position, CancellationToken ct) =>
        RunWithBypassAsync(async () =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct)
                ?? throw new DomainException("rbac.user.not_found", $"User {userId} not found.");
            await GuardManageUserAsync(user, ct);

            var trimmed = string.IsNullOrWhiteSpace(position) ? null : position.Trim();
            // §16 F3 (Tier-2 remediation) — a >100-char position is a shape error, not a 500;
            // mirrors the user.username_invalid DomainException style (Attempt log records the
            // actual resolved HTTP status — DomainExceptionMiddleware's code→status map has no
            // dedicated 400 pattern, so this resolves via the same 422 default as
            // user.username_invalid, not literal 400; flagged for Fable to confirm/adjust).
            if (trimmed is { Length: > 100 })
                throw new DomainException("user.position_too_long",
                    "Position must be at most 100 characters.");

            var before = user.Position;
            user.Position = trimmed;
            activity.Record("user", userId, user.Username, tenant.CompanyId, "user_profile_changed",
                note: $"position '{before}' -> '{trimmed}'", module: "sys");
            await db.SaveChangesAsync(ct);
        }, ct);

    /// <summary>A company-admin may only manage users who belong to THEIR company (have a role in
    /// it), and never a super-admin. Super-admins may manage anyone. A foreign user → not_found so
    /// no cross-company existence leaks (§4.7).</summary>
    private async Task GuardManageUserAsync(User user, CancellationToken ct)
    {
        if (tenant.IsSuperAdmin) return;
        if (user.IsSuperAdmin)
            throw new DomainException("rbac.super_admin_locked",
                "Only a super-admin can manage a super-admin account.");
        var inMyCompany = await db.UserRoles.AsNoTracking()
            .AnyAsync(ur => ur.UserId == user.UserId && ur.CompanyId == tenant.CompanyId, ct);
        if (!inMyCompany)
            throw new DomainException("rbac.user.not_found", $"User {user.UserId} not found.");
    }

    // ---- helpers -----------------------------------------------------------

    private static void GuardEditable(Role role)
    {
        if (role.CompanyId is null || string.Equals(role.RoleCode, SuperAdmin, StringComparison.Ordinal))
            throw new DomainException("rbac.super_admin_locked",
                "The system-global SUPER_ADMIN role cannot be modified.");
    }

    private static string DiffNote(string[] added, string[] removed)
    {
        var parts = new List<string>();
        if (added.Length > 0) parts.Add("+" + string.Join(",", added));
        if (removed.Length > 0) parts.Add("-" + string.Join(",", removed));
        return string.Join(" ", parts);
    }
}
