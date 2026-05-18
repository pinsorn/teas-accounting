using Accounting.Api.Authorization;
using Accounting.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Sprint 13c — read-only e-Tax submission audit query (consumed by the
/// etax-pipeline-mock e2e + ops). The audit-viewer UI is Phase 2 (spec §11);
/// no storage_path is ever projected. Reuses <c>tax.filing.read</c> — e-Tax is
/// tax-domain and no dedicated e-Tax permission is seeded (mechanism note).
/// </summary>
public static class EtaxEndpoints
{
    public static IEndpointRouteBuilder MapEtaxEndpoints(this IEndpointRouteBuilder app)
    {
        var read = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingRead;

        app.MapGet("/etax/submissions", async (
            [FromQuery(Name = "tax_invoice_id")] long taxInvoiceId,
            IETaxSubmissionAudit audit, CancellationToken ct) =>
            Results.Ok(await audit.ListByInvoiceAsync(taxInvoiceId, ct)))
            .RequireAuthorization(read)
            .WithTags("e-Tax");

        return app;
    }
}
