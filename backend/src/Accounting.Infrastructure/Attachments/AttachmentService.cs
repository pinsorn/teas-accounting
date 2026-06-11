using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Attachments;

/// <summary>
/// Sprint 11 — polymorphic attachment service. Tenant-scoped via the global
/// query filter. Validates parent_type/category, parent-row existence, mime +
/// size; soft-delete only. Perm-code strings are literals here because the Api
/// Permissions class is not referenceable from Infrastructure (same constraint
/// that once forced the TaxConfig/VatModeOptions label split — mechanism note;
/// VAT mode itself is now per-company via ICompanyTaxConfigService).
/// </summary>
public sealed class AttachmentService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    IFileStorageService storage, IOptions<FileStorageOptions> opts)
    : IAttachmentService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public IReadOnlyList<string> Categories() => AttachmentCodes.CategoryValues;

    public string? ParentReadPermission(string parentType) =>
        AttachmentCodes.TryParent(parentType, out var pt) ? pt switch
        {
            AttachmentParentType.VendorInvoice  => "purchase.vendor_invoice.read",
            AttachmentParentType.PaymentVoucher => "purchase.payment_voucher.read",
            AttachmentParentType.TaxInvoice     => "sales.tax_invoice.read",
            AttachmentParentType.JournalEntry   => "gl.journal.read",
            AttachmentParentType.Quotation      => "sales.quotation.manage",
            AttachmentParentType.SalesOrder     => "sales.sales_order.manage",
            AttachmentParentType.DeliveryOrder  => "sales.delivery_order.manage",
            AttachmentParentType.BillingNote    => "sales.billing_note.read",
            AttachmentParentType.CompanyProfile => "master.company.manage",
            // Receipt / CN-DN have no dedicated .read perm — rely on
            // sys.attachment.read + tenant isolation (documented).
            _ => null,
        } : null;

    private async Task<bool> ParentExistsAsync(
        AttachmentParentType pt, long id, CancellationToken ct) => pt switch
    {
        AttachmentParentType.VendorInvoice     => await db.VendorInvoices.AnyAsync(x => x.VendorInvoiceId == id, ct),
        AttachmentParentType.PaymentVoucher    => await db.PaymentVouchers.AnyAsync(x => x.PaymentVoucherId == id, ct),
        AttachmentParentType.Receipt           => await db.Receipts.AnyAsync(x => x.ReceiptId == id, ct),
        AttachmentParentType.TaxInvoice        => await db.TaxInvoices.AnyAsync(x => x.TaxInvoiceId == id, ct),
        AttachmentParentType.TaxAdjustmentNote => await db.TaxAdjustmentNotes.AnyAsync(x => x.NoteId == id, ct),
        AttachmentParentType.JournalEntry      => await db.JournalEntries.AnyAsync(x => x.JournalId == id, ct),
        AttachmentParentType.Quotation         => await db.Quotations.AnyAsync(x => x.QuotationId == id, ct),
        AttachmentParentType.SalesOrder        => await db.SalesOrders.AnyAsync(x => x.SalesOrderId == id, ct),
        AttachmentParentType.DeliveryOrder     => await db.DeliveryOrders.AnyAsync(x => x.DeliveryOrderId == id, ct),
        AttachmentParentType.PurchaseOrder     => false,   // Sprint 12 — no table yet
        AttachmentParentType.BillingNote       => await db.BillingNotes.AnyAsync(x => x.BillingNoteId == id, ct),
        AttachmentParentType.CompanyProfile    => await db.CompanyProfiles.AnyAsync(x => x.CompanyId == (int)id, ct),
        _ => false,
    };

    public async Task<AttachmentUploaded> UploadAsync(
        string parentType, long parentId, string category, string? description,
        string fileName, string mimeType, long sizeBytes, Stream content,
        CancellationToken ct)
    {
        Auth();
        if (!AttachmentCodes.TryParent(parentType, out var pt))
            throw new DomainException("attachment.bad_parent_type",
                $"Unknown parent_type '{parentType}'.");
        if (!AttachmentCodes.TryCategory(category, out var cat))
            throw new DomainException("attachment.bad_category",
                $"Unknown category '{category}'.");
        if (cat == AttachmentCategory.Other && string.IsNullOrWhiteSpace(description))
            throw new DomainException("attachment.description_required",
                "category=OTHER requires a description.");

        var maxBytes = (long)opts.Value.MaxFileSizeMb * 1024 * 1024;
        if (sizeBytes > maxBytes)
            throw new DomainException("attachment.too_large",
                $"File exceeds the {opts.Value.MaxFileSizeMb} MB limit.");
        if (!opts.Value.AllowedMimeTypes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            throw new DomainException("attachment.bad_mime",
                $"MIME type '{mimeType}' is not allowed.");

        if (!await ParentExistsAsync(pt, parentId, ct))
            throw new DomainException("attachment.parent_not_found",
                $"{parentType} {parentId} not found in this tenant.");

        var rel = await storage.SaveAsync(
            tenant.CompanyId, AttachmentCodes.ToDb(pt), parentId, content, fileName, ct);

        var now = clock.UtcNow;
        var e = new Attachment
        {
            CompanyId = tenant.CompanyId, ParentType = pt, ParentId = parentId,
            Category = cat, FileName = SanitizeName(fileName), MimeType = mimeType,
            SizeBytes = sizeBytes, StoragePath = rel,
            UploadedAt = now, UploadedBy = tenant.UserId ?? 0,
            Description = description,
        };
        db.Attachments.Add(e);
        await db.SaveChangesAsync(ct);
        return new AttachmentUploaded(e.AttachmentId, e.FileName, e.MimeType, e.SizeBytes, e.UploadedAt);
    }

    private static string SanitizeName(string n)
    {
        n = Path.GetFileName(n ?? "");
        return string.IsNullOrWhiteSpace(n) ? "file" : (n.Length > 200 ? n[^200..] : n);
    }

    public async Task<IReadOnlyList<AttachmentDto>> ListAsync(
        string parentType, long parentId, CancellationToken ct)
    {
        Auth();
        if (!AttachmentCodes.TryParent(parentType, out var pt))
            throw new DomainException("attachment.bad_parent_type",
                $"Unknown parent_type '{parentType}'.");
        var rows = await (
            from a in db.Attachments.AsNoTracking()
            where a.ParentType == pt && a.ParentId == parentId && a.DeletedAt == null
            join u in db.Users.AsNoTracking() on a.UploadedBy equals u.UserId into uj
            from u in uj.DefaultIfEmpty()
            orderby a.UploadedAt descending
            select new { a.AttachmentId, a.Category, a.FileName, a.MimeType,
                         a.SizeBytes, a.UploadedAt, a.UploadedBy,
                         Name = u != null ? u.FullName : "—",
                         a.Description, a.PageCount })
            .ToListAsync(ct);
        return rows.Select(r => new AttachmentDto(
            r.AttachmentId, AttachmentCodes.ToDb(r.Category), r.FileName, r.MimeType,
            r.SizeBytes, r.UploadedAt, r.UploadedBy, r.Name, r.Description, r.PageCount))
            .ToList();
    }

    public async Task<AttachmentContent> OpenForDownloadAsync(long id, CancellationToken ct)
    {
        Auth();
        var a = await db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttachmentId == id && x.DeletedAt == null, ct)
            ?? throw new DomainException("attachment.not_found", $"Attachment {id} not found.");
        var stream = await storage.OpenReadAsync(a.StoragePath, ct);
        return new AttachmentContent(a.FileName, a.MimeType, stream);
    }

    public async Task SoftDeleteAsync(long id, bool callerHasDeletePerm, CancellationToken ct)
    {
        Auth();
        var a = await db.Attachments
            .FirstOrDefaultAsync(x => x.AttachmentId == id && x.DeletedAt == null, ct)
            ?? throw new DomainException("attachment.not_found", $"Attachment {id} not found.");
        // §5 — delete perm OR own upload.
        if (!callerHasDeletePerm && a.UploadedBy != (tenant.UserId ?? 0))
            throw new DomainException("attachment.delete_forbidden",
                "Need sys.attachment.delete or be the uploader.");
        a.DeletedAt = clock.UtcNow;
        a.DeletedBy = tenant.UserId;
        await db.SaveChangesAsync(ct);   // file stays on disk (Phase-2 GC)
    }
}
