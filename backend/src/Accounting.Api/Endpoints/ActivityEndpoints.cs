using Accounting.Api.Authorization;
using Accounting.Application.Audit;

namespace Accounting.Api.Endpoints;

// Sprint 13j-FE D1/D2 — GET /{docType}/{id}/activity for the 8 sales doctypes.
// Read-only audit trail (audit.activity_log), chronological, tenant-scoped.
// All routes share the sales.audit.read permission. Explicit per-doctype
// routes (not a greedy {docType} segment) to avoid route ambiguity with the
// existing /{docType}/{id} resource GETs.
public static class ActivityEndpoints
{
    // route segment → audit EntityType
    private static readonly (string Route, string EntityType)[] Docs =
    {
        ("quotations", "Quotation"),
        ("sales-orders", "SalesOrder"),
        ("delivery-orders", "DeliveryOrder"),
        ("tax-invoices", "TaxInvoice"),
        ("receipts", "Receipt"),
        ("credit-notes", "CreditNote"),
        ("debit-notes", "DebitNote"),
        ("billing-notes", "BillingNote"),
    };

    public static void MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("").WithTags("Activity");

        foreach (var (route, entityType) in Docs)
        {
            group.MapGet($"/{route}/{{id:long}}/activity", async (
                long id, IActivityQueryService svc, CancellationToken ct) =>
                Results.Ok(await svc.GetForDocumentAsync(entityType, id, ct)))
                .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.AuditRead);
        }
    }
}
