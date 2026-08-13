using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Accounting.Api.Endpoints;

public static class AttachmentEndpoints
{
    // R3/H4 Tier-2 remediation FIX 1 — these three parent types are admin-managed BRAND/
    // IDENTITY assets (company logo, company stamp, a user's saved signature), not documents.
    // They are tenant-wide readable BY DESIGN: the sidebar and every PaperHead/PaperSign
    // preview render them for EVERY user through this exact download route regardless of
    // role. Their ParentReadPermission entries (master.company.manage /
    // master.company_profile.manage / sys.user.manage — see AttachmentService
    // .ParentReadPermission) gate who may CHANGE the asset, not who may view it — reusing
    // them as a download gate 403s the company logo for nearly every non-admin user (even
    // COMPANY_ADMIN, since master.company.manage is SUPER_ADMIN-only). Exempted from the
    // download-path guard ONLY — delete still calls ParentGuard unconditionally, so removing
    // a brand asset stays the manage question.
    private static readonly HashSet<string> TenantWideReadableOnDownload = new(StringComparer.Ordinal)
    {
        "COMPANY_PROFILE", "COMPANY_STAMP", "USER_SIGNATURE",
    };

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var upload = PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.AttachmentUpload;
        var read   = PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.AttachmentRead;
        var g = app.MapGroup("/attachments").WithTags("Attachments");

        // Parent-level read inheritance (§5): caller must hold the parent's
        // read perm (super-admin bypasses). Returns a 403 IResult or null.
        // Split into a sync core + this async wrapper so a caller that already loaded
        // `granted` (the delete route below) can reuse it instead of loading twice.
        static IResult? ParentGuardCore(
            string? parentType, IAttachmentService svc, ITenantContext tenant,
            IReadOnlyList<string> granted)
        {
            if (tenant.IsSuperAdmin || string.IsNullOrEmpty(parentType)) return null;
            var need = svc.ParentReadPermission(parentType);
            if (need is null) return null;
            return granted.Contains(need) ? null : Results.Problem(
                title: "Forbidden",
                detail: $"'{need}' required to attach to / read this parent.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        static async Task<IResult?> ParentGuard(
            string? parentType, IAttachmentService svc, ITenantContext tenant,
            IPermissionLookup perms, CancellationToken ct)
        {
            if (tenant.IsSuperAdmin || string.IsNullOrEmpty(parentType)) return null;
            var (_, granted) = await perms.LoadAsync(tenant.UserId ?? 0, tenant.CompanyId, ct);
            return ParentGuardCore(parentType, svc, tenant, granted);
        }

        g.MapPost("/", async (
            HttpRequest req, IAttachmentService svc, ITenantContext tenant,
            IPermissionLookup perms, IOptions<FileStorageOptions> opts,
            CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { detail = "multipart/form-data required." });
            var form = await req.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { detail = "file is required." });

            var maxBytes = (long)opts.Value.MaxFileSizeMb * 1024 * 1024;
            if (file.Length > maxBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            var parentType = form["parent_type"].ToString();
            var deny = await ParentGuard(parentType, svc, tenant, perms, ct);
            if (deny is not null) return deny;

            await using var s = file.OpenReadStream();
            var res = await svc.UploadAsync(
                parentType, long.TryParse(form["parent_id"], out var pid) ? pid : 0,
                form["category"].ToString(),
                string.IsNullOrWhiteSpace(form["description"]) ? null : form["description"].ToString(),
                file.FileName, file.ContentType, file.Length, s, ct);
            return Results.Created($"/attachments/{res.AttachmentId}", res);
        }).RequireAuthorization(upload).DisableAntiforgery();

        g.MapGet("/", async (
            [FromQuery(Name = "parent_type")] string parentType,
            [FromQuery(Name = "parent_id")] long parentId,
            IAttachmentService svc, ITenantContext tenant,
            IPermissionLookup perms, CancellationToken ct) =>
        {
            var deny = await ParentGuard(parentType, svc, tenant, perms, ct);
            if (deny is not null) return deny;
            return Results.Ok(new { items = await svc.ListAsync(parentType, parentId, ct) });
        }).RequireAuthorization(read);

        g.MapGet("/categories", (IAttachmentService svc) =>
            Results.Ok(svc.Categories())).RequireAuthorization(read);

        // R3/H4 — download/delete are id-only routes (the parent isn't on the request like
        // it is for upload/list), so the SAME ParentGuard used there must first resolve the
        // attachment's own parent from its row, then authorize against THAT — closing the gap
        // where a caller holding only the broadly-granted sys.attachment.read/.delete could
        // reach any attachment in the tenant regardless of parent-document visibility.
        g.MapGet("/{id:long}/download", async (
            long id, IAttachmentService svc, ITenantContext tenant,
            IPermissionLookup perms, CancellationToken ct) =>
        {
            var parent = await svc.ResolveParentAsync(id, ct);
            if (parent is null) return Results.NotFound();
            if (!TenantWideReadableOnDownload.Contains(parent.Value.ParentType))
            {
                IResult? deny = await ParentGuard(parent.Value.ParentType, svc, tenant, perms, ct);
                if (deny is not null) return deny;
            }

            var c = await svc.OpenForDownloadAsync(id, ct);
            return Results.File(c.Content, c.MimeType, c.FileName);
        }).RequireAuthorization(read);

        g.MapDelete("/{id:long}", async (
            long id, IAttachmentService svc, ITenantContext tenant,
            IPermissionLookup perms, CancellationToken ct) =>
        {
            var parent = await svc.ResolveParentAsync(id, ct);
            if (parent is null) return Results.NotFound();

            // R3/H4 Tier-2 remediation ("also fix") — load permissions ONCE and reuse for both
            // the parent guard and the attachment-delete check below. PermissionLookup.LoadAsync
            // opens its own transaction + set_config round-trip per call (§4.7 RLS pinning), so
            // calling it twice per request was needless duplicate DB work. Super-admin still
            // skips the load entirely (matches the pre-fix fast path).
            IReadOnlyList<string> granted = [];
            if (!tenant.IsSuperAdmin)
                (_, granted) = await perms.LoadAsync(tenant.UserId ?? 0, tenant.CompanyId, ct);

            IResult? deny = ParentGuardCore(parent.Value.ParentType, svc, tenant, granted);
            if (deny is not null) return deny;

            var hasDelete = tenant.IsSuperAdmin || granted.Contains(Permissions.Sys.AttachmentDelete);
            await svc.SoftDeleteAsync(id, hasDelete, ct);
            return Results.NoContent();
        }).RequireAuthorization(read);

        return app;
    }
}
