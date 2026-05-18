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
    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        var upload = PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.AttachmentUpload;
        var read   = PermissionPolicyProvider.PolicyPrefix + Permissions.Sys.AttachmentRead;
        var g = app.MapGroup("/attachments").WithTags("Attachments");

        // Parent-level read inheritance (§5): caller must hold the parent's
        // read perm (super-admin bypasses). Returns a 403 IResult or null.
        static async Task<IResult?> ParentGuard(
            string? parentType, IAttachmentService svc, ITenantContext tenant,
            IPermissionLookup perms, CancellationToken ct)
        {
            if (tenant.IsSuperAdmin || string.IsNullOrEmpty(parentType)) return null;
            var need = svc.ParentReadPermission(parentType);
            if (need is null) return null;
            var (_, granted) = await perms.LoadAsync(tenant.UserId ?? 0, tenant.CompanyId, ct);
            return granted.Contains(need) ? null : Results.Problem(
                title: "Forbidden",
                detail: $"'{need}' required to attach to / read this parent.",
                statusCode: StatusCodes.Status403Forbidden);
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

        g.MapGet("/{id:long}/download", async (
            long id, IAttachmentService svc, CancellationToken ct) =>
        {
            var c = await svc.OpenForDownloadAsync(id, ct);
            return Results.File(c.Content, c.MimeType, c.FileName);
        }).RequireAuthorization(read);

        g.MapDelete("/{id:long}", async (
            long id, IAttachmentService svc, ITenantContext tenant,
            IPermissionLookup perms, CancellationToken ct) =>
        {
            var hasDelete = tenant.IsSuperAdmin;
            if (!hasDelete)
            {
                var (_, granted) = await perms.LoadAsync(tenant.UserId ?? 0, tenant.CompanyId, ct);
                hasDelete = granted.Contains(Permissions.Sys.AttachmentDelete);
            }
            await svc.SoftDeleteAsync(id, hasDelete, ct);
            return Results.NoContent();
        }).RequireAuthorization(read);

        return app;
    }
}
