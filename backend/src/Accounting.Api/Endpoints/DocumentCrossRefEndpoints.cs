using Accounting.Api.Authorization;
using Accounting.Application.Sales;

namespace Accounting.Api.Endpoints;

// Sprint 13h P8 — single read-only endpoint surface for the FE useCrossReferences
// hook. Routed under /document-cross-refs so it sits alongside the other sales
// endpoints. Each docType requires the matching sales.*.read permission.

public static class DocumentCrossRefEndpoints
{
    public static void MapDocumentCrossRefEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/document-cross-refs").WithTags("DocumentCrossRefs");

        group.MapGet("/tax-invoice/{id:long}", async (
            long id, IDocumentCrossRefService svc, CancellationToken ct) =>
            await svc.GetForTaxInvoiceAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.TaxInvoiceRead);

        // Receipt reads share the ReceiptCreate policy (legacy: no separate Read code).
        group.MapGet("/receipt/{id:long}", async (
            long id, IDocumentCrossRefService svc, CancellationToken ct) =>
            await svc.GetForReceiptAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.ReceiptCreate);

        // Adjustment notes (CN+DN share the same endpoint surface).
        group.MapGet("/adjustment-note/{id:long}", async (
            long id, IDocumentCrossRefService svc, CancellationToken ct) =>
            await svc.GetForAdjustmentNoteAsync(id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Sales.CreditNoteCreate);

        // cont.69 Phase 3 (D7) — unified full-chain resolver. One endpoint serves every
        // sales detail page (Q→SO→DO→Invoice→TI→RC + CN/DN). Read-only + tenant-scoped;
        // requires authentication only — the chain spans doc types whose per-type read
        // permissions differ (and VAT vs non-VAT companies), so a single fine-grained
        // policy would wrongly block valid reads. The query itself enforces company isolation.
        var docs = app.MapGroup("/documents").WithTags("Documents");
        docs.MapGet("/chain", async (
            string type, long id, IDocumentCrossRefService svc, CancellationToken ct) =>
            await svc.GetChainAsync(type, id, ct) is { } d ? Results.Ok(d) : Results.NotFound())
            .RequireAuthorization();
    }
}
