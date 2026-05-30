using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

public sealed partial class PaymentVoucherService
{
    public async Task<CursorPage<PaymentVoucherListItem>> ListAsync(
        long? cursor, int limit, CancellationToken ct, bool incompleteOnly = false)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var lim = Math.Clamp(limit, 1, 100);
        var q = _db.PaymentVouchers.AsNoTracking().AsQueryable();
        if (cursor is { } c) q = q.Where(p => p.PaymentVoucherId < c);

        // Cursor paging is computed on the RAW query (Take(lim+1)) BEFORE the in-memory
        // completeness filter, so the cursor stays stable. NOTE: with incompleteOnly the
        // returned page may contain FEWER than `limit` items (even 0) while HasMore=true —
        // completeness is a post-materialization filter (cont.76 D2, advisory-only).
        var raw = await q.OrderByDescending(p => p.PaymentVoucherId).Take(lim + 1)
            .Select(p => new
            {
                p.PaymentVoucherId, p.DocNo, p.DocDate, p.VendorName, p.VendorTaxId,
                p.SubPrefix, p.TotalPaid, p.WhtAmount, p.Status, p.CurrencyCode,
                p.VendorId, p.VendorInvoiceId,
            })
            .ToListAsync(ct);

        var more = raw.Count > lim;
        if (more) raw.RemoveAt(raw.Count - 1);
        long? next = more ? raw[^1].PaymentVoucherId : null;

        // Batch-load the completeness inputs for the POSTED rows on this page (no N+1).
        var posted = raw.Where(r => r.Status == DocumentStatus.Posted).ToList();
        var pvIds       = posted.Select(r => r.PaymentVoucherId).ToList();
        var vendorIds   = posted.Select(r => r.VendorId).Distinct().ToList();
        var linkedViIds = posted.Where(r => r.VendorInvoiceId is not null)
            .Select(r => r.VendorInvoiceId!.Value).Distinct().ToList();

        var vatRegByVendor = vendorIds.Count == 0
            ? new Dictionary<long, bool>()
            : await _db.Vendors.AsNoTracking()
                .Where(v => vendorIds.Contains(v.VendorId))
                .ToDictionaryAsync(v => v.VendorId, v => v.VatRegistered, ct);

        var postedViIds = linkedViIds.Count == 0
            ? new HashSet<long>()
            : (await _db.VendorInvoices.AsNoTracking()
                .Where(v => linkedViIds.Contains(v.VendorInvoiceId)
                         && v.Status == DocumentStatus.Posted)
                .Select(v => v.VendorInvoiceId).ToListAsync(ct)).ToHashSet();

        var pvWithCert = pvIds.Count == 0
            ? new HashSet<long>()
            : (await _db.WhtCertificates.AsNoTracking()
                .Where(w => w.PaymentVoucherId != null
                         && pvIds.Contains(w.PaymentVoucherId!.Value)
                         && w.Direction == "P")
                .Select(w => w.PaymentVoucherId!.Value).ToListAsync(ct)).ToHashSet();

        var pvWithReceipt = pvIds.Count == 0
            ? new HashSet<long>()
            : (await _db.Attachments.AsNoTracking()
                .Where(a => a.ParentType == AttachmentParentType.PaymentVoucher
                         && a.Category == AttachmentCategory.Receipt
                         && a.DeletedAt == null
                         && pvIds.Contains(a.ParentId))
                .Select(a => a.ParentId).ToListAsync(ct)).ToHashSet();

        var items = new List<PaymentVoucherListItem>(raw.Count);
        foreach (var r in raw)
        {
            var complete = true;
            if (r.Status == DocumentStatus.Posted)
            {
                var vatReg     = vatRegByVendor.TryGetValue(r.VendorId, out var vr) && vr;
                var hasPostedVi = r.VendorInvoiceId is { } vid && postedViIds.Contains(vid);
                var missingVi      = vatReg && !hasPostedVi;
                var missingCert    = r.WhtAmount > 0m && !pvWithCert.Contains(r.PaymentVoucherId);
                var missingReceipt = !pvWithReceipt.Contains(r.PaymentVoucherId);
                complete = !(missingVi || missingCert || missingReceipt);
            }
            if (incompleteOnly && complete) continue;
            items.Add(new PaymentVoucherListItem(
                r.PaymentVoucherId, r.DocNo, r.DocDate, r.VendorName, r.VendorTaxId,
                r.SubPrefix, r.TotalPaid, r.WhtAmount, r.Status.ToString(), r.CurrencyCode,
                complete));
        }

        return new CursorPage<PaymentVoucherListItem>(items, next, more);
    }

    public async Task<PaymentVoucherDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        var p = await _db.PaymentVouchers.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PaymentVoucherId == id, ct);
        if (p is null) return null;

        // Sprint 13j-PURCH Flag-2 — downward ref: WHT certificate(s) (50ทวิ) issued
        // from this PV. WhtCertificate is ITenantOwned so the global filter scopes by
        // company. Direction='P' (payable) carries our PaymentVoucherId.
        var whtCerts = await _db.WhtCertificates.AsNoTracking()
            .Where(w => w.PaymentVoucherId == id)
            .OrderBy(w => w.WhtCertificateId)
            .Select(w => new PaymentVoucherWhtCertificate(
                w.WhtCertificateId, w.DocNo, w.Status.ToString()))
            .ToListAsync(ct);

        var completeness = await ComputeCompletenessAsync(p, ct);

        return new PaymentVoucherDetail(
            p.PaymentVoucherId, p.DocNo, p.Status.ToString(), p.DocDate,
            p.VendorId, p.VendorName, p.VendorTaxId, p.VendorBranchCode, p.VendorAddress,
            p.ExpenseCategoryId, p.SubPrefix, p.PaymentMethod.ToString(), p.ChequeNo,
            p.ChequeDate, p.BankAccountId, p.CurrencyCode, p.ExchangeRate,
            p.SubtotalAmount, p.VatAmount, p.WhtAmount, p.TotalPaid, p.TotalAmountThb,
            p.Description, p.Notes, p.VendorInvoiceId,
            p.ApprovedBy, p.ApprovedAt, p.PostedAt,
            p.SelfWithholdMode, p.RequiresPnd36ReverseCharge,
            p.Lines.OrderBy(l => l.LineNo).Select(l => new PaymentVoucherLineView(
                l.LineNo, l.ExpenseAccountId, l.Description, l.Amount, l.VatRate,
                l.VatAmount, l.IsRecoverableVat, l.WhtTypeId, l.WhtRate, l.WhtAmount,
                l.ProductType)).ToList(),
            whtCerts, completeness);
    }

    /// <summary>
    /// cont.76 — advisory completeness for a single PV. Evaluated ONLY for POSTED docs
    /// (drafts return Complete — they are not nagged). Per-row EXISTS lookups; the list
    /// path batches the same checks. NON-BLOCKING. Tenant filter via the ITenantOwned
    /// global filters on Vendor/VendorInvoice/WhtCertificate/Attachment.
    /// </summary>
    private async Task<CompletenessView> ComputeCompletenessAsync(
        Domain.Entities.Purchase.PaymentVoucher p, CancellationToken ct)
    {
        if (p.Status != DocumentStatus.Posted) return CompletenessView.Complete;

        var missing = new List<string>();

        // MISSING_VI — VAT-registered vendor must have a linked POSTED VI.
        var vatRegistered = await _db.Vendors.AsNoTracking()
            .Where(v => v.VendorId == p.VendorId)
            .Select(v => (bool?)v.VatRegistered).FirstOrDefaultAsync(ct) ?? false;
        if (vatRegistered)
        {
            var hasPostedVi = p.VendorInvoiceId is { } vid
                && await _db.VendorInvoices.AsNoTracking()
                    .AnyAsync(v => v.VendorInvoiceId == vid
                                && v.Status == DocumentStatus.Posted, ct);
            if (!hasPostedVi) missing.Add("MISSING_VI");
        }

        // MISSING_WHT_CERT — cheap invariant guard (cert auto-issues at post).
        if (p.WhtAmount > 0m)
        {
            var hasCert = await _db.WhtCertificates.AsNoTracking()
                .AnyAsync(w => w.PaymentVoucherId == p.PaymentVoucherId
                            && w.Direction == "P", ct);
            if (!hasCert) missing.Add("MISSING_WHT_CERT");
        }

        // MISSING_RECEIPT_FILE — soft; no non-deleted Receipt attachment on the PV.
        var hasReceipt = await _db.Attachments.AsNoTracking()
            .AnyAsync(a => a.ParentType == AttachmentParentType.PaymentVoucher
                        && a.Category == AttachmentCategory.Receipt
                        && a.ParentId == p.PaymentVoucherId
                        && a.DeletedAt == null, ct);
        if (!hasReceipt) missing.Add("MISSING_RECEIPT_FILE");

        return CompletenessView.From(missing);
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        var d = await GetDetailAsync(id, ct)
            ?? throw new DomainException("pv.not_found", $"Payment Voucher {id} not found.");

        // Sprint 13j-PURCH Phase C — render via the shared PaperDocument mirror.
        // Seller = the issuing company (the payer); Customer = the payee (vendor).
        // Three-box sign (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน). The foot carries the
        // WHT-deducted net via PaperSummary.Wht → grand total reads "จ่ายสุทธิ".
        // Payment method / cheque detail goes into Notes (no §86/4 schema applies).
        var seller = await Pdf.PaperSellerSource.FromCompanyProfileAsync(_db, _tenant.CompanyId, ct);

        var notes = $"วิธีชำระ / Method: {d.PaymentMethod}"
            + (string.IsNullOrEmpty(d.ChequeNo) ? "" : $"  เช็คเลขที่ {d.ChequeNo}");
        if (!string.IsNullOrWhiteSpace(d.Description)) notes = $"{d.Description}\n{notes}";
        if (!string.IsNullOrWhiteSpace(d.Notes)) notes = $"{notes}\n{d.Notes}";

        var model = new Pdf.PaperDocModel(
            DocType: "ใบสำคัญจ่าย",
            DocTypeEn: "Payment Voucher",
            DocNo: d.DocNo ?? "(ร่าง)",
            IssueDate: d.DocDate,
            Seller: seller,
            Customer: new Pdf.PaperCustomer(
                d.VendorName, Pdf.PaperFormat.TaxId(d.VendorTaxId), d.VendorBranchCode, d.VendorAddress),
            Items: d.Lines.Select(l => new Pdf.PaperLine(
                l.Description, null, null, null, null, null, l.Amount)).ToList(),
            Summary: new Pdf.PaperSummary(
                Subtotal: d.SubtotalAmount, Discount: null, BeforeVat: d.SubtotalAmount,
                Vat: d.VatAmount, Total: d.TotalPaid, VatRate: null, ShowVat: true,
                Wht: d.WhtAmount > 0m ? d.WhtAmount : null),
            SignRoles: new Pdf.PaperSignRoles("ผู้จัดทำ", "ผู้รับเงิน", Middle: "ผู้อนุมัติ"),
            Notes: notes,
            Watermark: new Pdf.PaperWatermark(
                copy ? "สำเนา" : "ต้นฉบับ",
                copy ? Pdf.PaperWatermarkVariant.Warning : Pdf.PaperWatermarkVariant.Success));
        return Pdf.PaperDocumentPdf.Render(model);
    }
}
