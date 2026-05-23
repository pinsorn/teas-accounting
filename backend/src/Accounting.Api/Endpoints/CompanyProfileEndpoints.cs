using Accounting.Api.Authorization;
using Accounting.Application.Master;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Sprint 13d P6 — Company Profile (hybrid lock, Phase 1).
///  * GET    /company-profile       — any authenticated role (invoice header,
///                                     nav branding).
///  * PUT    /company-profile/soft  — admin (master.company.manage). Soft
///                                     fields only.
///  * PUT    /company-profile/hard  — 501 Not Implemented. Hard fields are
///                                     ภ.พ.20-bound; Phase 2 adds a 2-person
///                                     approval flow. Body explains the
///                                     current workaround.
///
/// NOTE: project convention is Minimal-API endpoint modules (not MVC
/// controllers); the Sprint-13d spec said "CompanyProfileController" — built
/// here as endpoints to match the codebase (flagged in Report-Backend21).
/// </summary>
public static class CompanyProfileEndpoints
{
    public static IEndpointRouteBuilder MapCompanyProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/company-profile").WithTags("Company Profile");

        // Read — any authenticated user.
        g.MapGet("/", async (ICompanyProfileService svc, CancellationToken ct) =>
                await svc.GetAsync(ct) is { } p ? Results.Ok(p) : Results.NotFound())
            .RequireAuthorization();

        // Soft update — admin only.
        g.MapPut("/soft", async ([FromBody] UpdateCompanyProfileSoftRequest req,
            IValidator<UpdateCompanyProfileSoftRequest> v,
            ICompanyProfileService svc, CancellationToken ct) =>
        {
            var val = await v.ValidateAsync(req, ct);
            if (!val.IsValid) return Results.ValidationProblem(val.ToDictionary());
            await svc.UpdateSoftAsync(req, ct);
            return Results.NoContent();
        }).RequireAuthorization(
            PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CompanyManage);

        // Hard update — Phase-1 not implemented (ภ.พ.20-bound, see body).
        g.MapPut("/hard", () => Results.Json(new
        {
            type = "urn:teas:error:company_profile.hard_locked",
            title = "company_profile.hard_locked",
            detail =
                "Legal company fields (legal name, tax id, registered address, "
                + "VAT registration date, branch code) are bound to ภ.พ.20 and "
                + "cannot be edited via the API in Phase 1. Workaround: file "
                + "ภ.พ.09 with the Revenue Department, then update via a DB "
                + "script with a matching audit.activity_log entry. Phase 2 "
                + "will add a 2-person approval flow.",
            status = StatusCodes.Status501NotImplemented,
        }, statusCode: StatusCodes.Status501NotImplemented))
            .RequireAuthorization(
                PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CompanyManage);

        // Sprint 13h P10 — Logo upload (multipart/form-data, png/jpeg/svg/webp,
        // max 1 MB). Stored via the polymorphic attachments table; the new URL
        // is written back to CompanyProfile.LogoUrl.
        g.MapPost("/logo", async (HttpRequest http,
            ICompanyProfileService svc, CancellationToken ct) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { detail = "Expected multipart/form-data." });
            var form = await http.ReadFormAsync(ct);
            var file = form.Files["file"] ?? (form.Files.Count > 0 ? form.Files[0] : null);
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { detail = "Missing 'file' part." });
            await using var s = file.OpenReadStream();
            var url = await svc.UpdateLogoAsync(
                file.FileName, file.ContentType, file.Length, s, ct);
            return Results.Ok(new { logoUrl = url });
        }).RequireAuthorization(
            PermissionPolicyProvider.PolicyPrefix + Permissions.Master.CompanyManage)
          .DisableAntiforgery();

        return app;
    }
}
