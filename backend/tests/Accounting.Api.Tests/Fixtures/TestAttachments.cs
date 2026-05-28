using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;

namespace Accounting.Api.Tests.Fixtures;

/// <summary>
/// Pin: VendorInvoiceService.PostAsync now requires ≥1 non-deleted attachment under
/// (VendorInvoice, viId) — the vendor's ใบกำกับภาษีซื้อ file is the documentary evidence
/// ม.86/4 + ม.82/4 audit needs. Every BE integration test that calls
/// IVendorInvoiceService.PostAsync MUST seed an attachment between CreateDraft and Post,
/// or it gets `vi.attachment_required`.
///
/// This lives in Api.Tests/Fixtures (not Accounting.TestKit) because TestKit is policy-
/// pinned to "no production deps" — the helper here intentionally pulls Domain +
/// Infrastructure to construct the Attachment row, so it stays one layer up.
///
/// Inserts a minimal Attachment row pointing at a fictional storage path — the row is
/// what the guard counts; no file is read from disk in unit-test paths. StoragePath is
/// suffix-randomised so concurrent runs against the shared teas_test DB never collide.
/// </summary>
public static class TestAttachments
{
    public static Attachment SeedViAttachment(
        this AccountingDbContext db, long viId, int companyId = 1, long uploadedBy = 1,
        AttachmentCategory category = AttachmentCategory.TaxInvoice)
    {
        var suffix = TestIds.Suffix()[..8];
        var att = new Attachment
        {
            CompanyId   = companyId,
            ParentType  = AttachmentParentType.VendorInvoice,
            ParentId    = viId,
            Category    = category,
            FileName    = $"vendor-ti-{suffix}.pdf",
            MimeType    = "application/pdf",
            SizeBytes   = 1024,
            StoragePath = $"test/{companyId}/vi/{viId}/{suffix}.pdf",
            UploadedAt  = DateTimeOffset.UtcNow,
            UploadedBy  = uploadedBy,
        };
        db.Attachments.Add(att);
        return att;
    }
}
