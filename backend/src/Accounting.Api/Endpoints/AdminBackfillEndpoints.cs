using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// R1/C6 (WP-2, specs/fix-breakit-r1-ledger-integrity.md §3.2.5) — non-VAT AR backfill for
/// invoices issued before WP-1 shipped. Mirrors <c>/tax-filings/pnd30?mode=preview|finalize</c>'s
/// mode-param shape. Authorization: explicit <c>IsSuperAdmin</c> claim check (NOT a permission
/// policy — this is a one-time, irreversible-once-applied prod-data operation, not a role-
/// grantable feature). Mirrors InstanceSetupEndpoints' first-run super-admin gate exactly.
/// No <c>companyId</c> parameter by design — the target is always the caller's OWN tenant
/// (<see cref="ITenantContext.CompanyId"/>), set by TenantMiddleware from the caller's active
/// company; a super-admin runs this once per company, from inside that company's session.
/// </summary>
public static class AdminBackfillEndpoints
{
    public static IEndpointRouteBuilder MapAdminBackfillEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/nonvat-ar-backfill", async (
            [FromQuery] string? mode,
            ITenantContext tenant, INonVatArBackfillService svc, CancellationToken ct) =>
        {
            if (!tenant.IsSuperAdmin)
                return Results.Problem(title: "forbidden",
                    detail: "Only a super-admin may run the non-VAT AR backfill.",
                    statusCode: StatusCodes.Status403Forbidden);

            // "required" per spec — no silent default, unlike TaxFilingEndpoints' ParseMode.
            if (!string.Equals(mode, "preview", StringComparison.OrdinalIgnoreCase)
             && !string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase))
                return Results.Problem(title: "validation",
                    detail: "mode is required and must be 'preview' or 'apply'.",
                    statusCode: StatusCodes.Status400BadRequest);

            var result = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase)
                ? await svc.ApplyAsync(ct)
                : await svc.PreviewAsync(ct);
            return Results.Ok(result);
        })
        .WithTags("Admin")
        .RequireAuthorization();   // authenticated; super-admin enforced in handler (claims-only)

        return app;
    }
}
