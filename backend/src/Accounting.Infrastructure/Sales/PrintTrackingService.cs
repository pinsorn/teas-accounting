using System.Text.Json;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Audit;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

// Sprint 13j-FE — stamps OriginalPrintedAt / PrintCount and writes an
// audit.activity_log row per print. Document lookup relies on the EF global
// query filter (RLS): a super-admin can print cross-company, a tenant user is
// scoped — same visibility as the detail endpoints (no manual company filter).
public sealed class PrintTrackingService(AccountingDbContext db, ITenantContext tenant)
    : IPrintTrackingService
{
    public async Task<PrintMarkResult?> MarkPrintedAsync(
        PrintDocType docType, long id, bool isCopy, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Returns (companyId, docNo, wasReprint, printCount) after mutating, or null.
        switch (docType)
        {
            case PrintDocType.TaxInvoice:
            {
                var e = await db.TaxInvoices.FirstOrDefaultAsync(x => x.TaxInvoiceId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                Log(e.CompanyId, "TaxInvoice", id, e.DocNo, isCopy || was, was, e.PrintCount, now);
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            case PrintDocType.Receipt:
            {
                var e = await db.Receipts.FirstOrDefaultAsync(x => x.ReceiptId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                Log(e.CompanyId, "Receipt", id, e.DocNo, isCopy || was, was, e.PrintCount, now);
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            case PrintDocType.CreditNote:
            case PrintDocType.DebitNote:
            {
                var e = await db.TaxAdjustmentNotes.FirstOrDefaultAsync(x => x.NoteId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                var entityType = docType == PrintDocType.CreditNote ? "CreditNote" : "DebitNote";
                Log(e.CompanyId, entityType, id, e.DocNo, isCopy || was, was, e.PrintCount, now);
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            case PrintDocType.Quotation:
            {
                var e = await db.Quotations.FirstOrDefaultAsync(x => x.QuotationId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                Log(e.CompanyId, "Quotation", id, e.DocNo, isCopy || was, was, e.PrintCount, now);
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            case PrintDocType.SalesOrder:
            {
                var e = await db.SalesOrders.FirstOrDefaultAsync(x => x.SalesOrderId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                Log(e.CompanyId, "SalesOrder", id, e.DocNo, isCopy || was, was, e.PrintCount, now);
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            case PrintDocType.DeliveryOrder:
            {
                var e = await db.DeliveryOrders.FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                Log(e.CompanyId, "DeliveryOrder", id, e.DocNo, isCopy || was, was, e.PrintCount, now);
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            case PrintDocType.BillingNote:
            {
                var e = await db.BillingNotes.FirstOrDefaultAsync(x => x.BillingNoteId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                // Audit EntityType stays "BillingNote" (internal name, D5); UI shows ใบแจ้งหนี้/Invoice.
                Log(e.CompanyId, "BillingNote", id, e.DocNo, isCopy || was, was, e.PrintCount, now);
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            // Sprint 13j-PURCH Phase C (D4) — Purchase docs that print; audit
            // rows carry module="purchase".
            case PrintDocType.PurchaseOrder:
            {
                var e = await db.PurchaseOrders.FirstOrDefaultAsync(x => x.PurchaseOrderId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                Log(e.CompanyId, "PurchaseOrder", id, e.DocNo, isCopy || was, was, e.PrintCount, now, "purchase");
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            case PrintDocType.PaymentVoucher:
            {
                var e = await db.PaymentVouchers.FirstOrDefaultAsync(x => x.PaymentVoucherId == id, ct);
                if (e is null) return null;
                var was = e.OriginalPrintedAt is not null;
                if (!isCopy && e.OriginalPrintedAt is null) e.OriginalPrintedAt = now;
                e.PrintCount++;
                Log(e.CompanyId, "PaymentVoucher", id, e.DocNo, isCopy || was, was, e.PrintCount, now, "purchase");
                await db.SaveChangesAsync(ct);
                return new PrintMarkResult(e.OriginalPrintedAt, e.PrintCount, was);
            }
            default:
                return null;
        }
    }

    private void Log(int companyId, string entityType, long id, string? docNo,
        bool isCopy, bool wasReprint, int printCount, DateTimeOffset at, string module = "sales")
    {
        db.Set<ActivityLog>().Add(new ActivityLog
        {
            CompanyId = companyId,
            UserId = tenant.UserId,
            ActivityAt = at,
            ActivityType = isCopy ? "PrintedCopy" : "PrintedOriginal",
            Module = module,
            EntityType = entityType,
            EntityId = id,
            EntityDocNo = docNo,
            MetadataJson = JsonSerializer.Serialize(new { copyType = isCopy ? "copy" : "original", wasReprint, printCount }),
        });
    }
}
