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
            // R3/H4 Tier-2 remediation — Receipt fell through to `_ => null` (fail-open: any
            // sys.attachment.read holder, granted to every role, could reach any receipt's
            // attachment). sales.receipt.read exists (330_seed_receipt_adjnote_rbac.sql).
            AttachmentParentType.Receipt        => "sales.receipt.read",
            AttachmentParentType.TaxInvoice     => "sales.tax_invoice.read",
            // R3/H4 Tier-2 remediation — TaxAdjustmentNote (CN+DN, one parent type for both)
            // fell through the same way. No single existing code covers both
            // sales.credit_note.read AND sales.debit_note.read, and this method is keyed by
            // parent TYPE alone (not the note's own NoteType, which would need a DB lookup) —
            // sales.tax_invoice.read is granted to the IDENTICAL role list as both
            // credit_note.read and debit_note.read (320_seed_chapter3_rbac.sql /
            // 330_seed_receipt_adjnote_rbac.sql: COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/
            // AR_CLERK/SALES_STAFF/AUDITOR), so this is a correction against the invoice it
            // adjusts with no over/under-restriction versus a dedicated code.
            AttachmentParentType.TaxAdjustmentNote => "sales.tax_invoice.read",
            AttachmentParentType.JournalEntry   => "gl.journal.read",
            AttachmentParentType.Quotation      => "sales.quotation.manage",
            AttachmentParentType.SalesOrder     => "sales.sales_order.manage",
            AttachmentParentType.DeliveryOrder  => "sales.delivery_order.manage",
            AttachmentParentType.BillingNote    => "sales.billing_note.read",
            // doc-signature spec §E3 / Tier-2 (R3/H4) FIX 1 — these entries gate who may
            // CHANGE the asset (upload/delete), NOT who may view it: CompanyProfile/
            // CompanyStamp/UserSignature are tenant-wide readable BY DESIGN (rendered on every
            // page/document for every role) and are exempted from THIS permission on the
            // download route only — see AttachmentEndpoints.TenantWideReadableOnDownload
            // (backend/src/Accounting.Api/Endpoints/AttachmentEndpoints.cs).
            // Delete keeps the full gate below (removing a brand asset IS the manage question).
            AttachmentParentType.CompanyProfile => "master.company.manage",
            // Cycle C — Expense Claims.
            AttachmentParentType.ExpenseClaim   => "expense.claim.read",
            AttachmentParentType.UserSignature  => "sys.user.manage",
            AttachmentParentType.CompanyStamp   => "master.company_profile.manage",
            // R3/H4 Tier-2 remediation — BankStatement fell through the same way. Mapped to
            // bank.statement.import: StatementImportEndpoints' OWN list ("/") and lines
            // ("/{id}/lines") routes are ALSO gated on this code (not bank.reconcile or
            // bank.report.read) — it is already the real "view bank statement data" gate in
            // this app, so this is consistency, not a new restriction. StatementImportService
            // .ImportAsync calls IAttachmentService.UploadAsync directly (bypasses this HTTP
            // endpoint's guard entirely), so upload is unaffected by this mapping.
            AttachmentParentType.BankStatement  => "bank.statement.import",
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
        // Bank reconciliation B2 (D11) — statement_imports.attachment_id reuses this infra.
        AttachmentParentType.BankStatement     => await db.StatementImports.AnyAsync(x => x.StatementImportId == id, ct),
        // Cycle C — Expense Claims (specs/expense-claims.md §5). Attachments parent to the
        // stable HEADER id (FOOTGUN 7 — line ids churn on every draft edit).
        AttachmentParentType.ExpenseClaim      => await db.ExpenseClaims.AnyAsync(x => x.ExpenseClaimId == id, ct),
        // doc-signature spec §E3 — a signature may only be attached to a user who is a MEMBER
        // OF THE CALLER'S COMPANY (UserRole.CompanyId). sys.users is cross-tenant, so without
        // this a caller could stamp a signature onto ANY user id in the instance. Paired with
        // ParentReadPermission above (sys.user.manage), this is what stops one employee forging
        // a colleague's signature onto a legal document.
        // §16 F1 (Tier-2 remediation, Fable-decided) — a NARROW super-admin SELF arm:
        // CompanySwitchService.SwitchAsync performs no membership check, so a super-admin
        // operating as a company they hold no sys.user_roles row in is the one legitimate case
        // of a non-member being a document actor there. Bounded to id == tenant.UserId ONLY —
        // a super-admin still cannot stamp anyone ELSE who isn't a member (the forgery bound
        // must not widen further than "may sign for themselves").
        AttachmentParentType.UserSignature     => id > 0 && (
            (tenant.IsSuperAdmin && id == (tenant.UserId ?? 0))
            || await db.UserRoles.AnyAsync(r => r.UserId == id && r.CompanyId == tenant.CompanyId, ct)),
        AttachmentParentType.CompanyStamp      => id == tenant.CompanyId
            && await db.CompanyProfiles.AnyAsync(x => x.CompanyId == tenant.CompanyId, ct),
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

    public async Task<(string ParentType, long ParentId)?> ResolveParentAsync(long id, CancellationToken ct)
    {
        Auth();
        var a = await db.Attachments.AsNoTracking()
            .Where(x => x.AttachmentId == id && x.DeletedAt == null)
            .Select(x => new { x.ParentType, x.ParentId })
            .FirstOrDefaultAsync(ct);
        return a is null ? null : (AttachmentCodes.ToDb(a.ParentType), a.ParentId);
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
