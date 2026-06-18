using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Sales;   // ChainMath
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Purchase;

/// <summary>
/// Sprint 12 — internal PO. SoD approval mirrors PV B2 (entity guard +
/// ck_po_sod DB CHECK). doc_no PO-NNNN allocated on Approve (+BU sub-prefix).
/// Tenant-scoped via the global query filter.
/// </summary>
public sealed class PurchaseOrderService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IActivityRecorder activity,
    IFileStorageService storage) : IPurchaseOrderService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreatePurchaseOrderRequest req, CancellationToken ct)
    {
        Auth();
        var v = await db.Vendors.AsNoTracking()
            .FirstOrDefaultAsync(x => x.VendorId == req.VendorId, ct)
            ?? throw new DomainException("vendor.not_found", "Vendor not found.");

        // cont.79 — BU (GL dimension). Required when the company opted in; if supplied
        // it must be an active BU of this tenant (mirror TaxInvoiceService).
        var requiresBu = await db.Companies
            .Where(c => c.CompanyId == tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstOrDefaultAsync(ct);
        if (requiresBu && req.BusinessUnitId is null)
            throw new DomainException("bu.required", "Business Unit is required for this company.");
        if (req.BusinessUnitId is { } buId &&
            !await db.BusinessUnits.AnyAsync(x => x.BusinessUnitId == buId
                && x.CompanyId == tenant.CompanyId && x.IsActive, ct))
            throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

        var po = new PurchaseOrder
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = PurchaseOrderStatus.Draft, DocDate = req.DocDate,
            ExpectedDeliveryDate = req.ExpectedDeliveryDate,
            VendorId = v.VendorId, VendorName = v.NameTh, VendorTaxId = v.TaxId,
            VendorType = v.VendorType, BusinessUnitId = req.BusinessUnitId,
            CurrencyCode = req.CurrencyCode, ExchangeRate = req.ExchangeRate,
            Notes = req.Notes, InternalNotes = req.InternalNotes,
            // M4 (MCP) — stamp the key name when created by an API-key principal (agent). Null for JWT.
            CreatedViaApiKeyName = tenant.ApiKeyName,
        };
        Fill(po, req);
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(ct);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Created", toStatus: "Draft", module: "purchase");
        await db.SaveChangesAsync(ct);
        return po.PurchaseOrderId;
    }

    private static void Fill(PurchaseOrder po, CreatePurchaseOrderRequest req)
    {
        po.Lines.Clear();
        po.SubtotalAmount = po.VatAmount = po.TotalAmount = 0m;
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRate);
            po.Lines.Add(new PurchaseOrderLine
            {
                LineNo = n++, ProductId = l.ProductId, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                LineAmount = net, TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode,
                TaxRate = l.TaxRate, TaxAmount = vat, TotalAmount = total, Notes = l.Notes,
            });
            po.SubtotalAmount += net; po.VatAmount += vat; po.TotalAmount += total;
        }
        po.TotalAmountThb = Math.Round(po.TotalAmount * po.ExchangeRate, 4, MidpointRounding.AwayFromZero);
    }

    private async Task<PurchaseOrder> LoadAsync(long id, CancellationToken ct) =>
        await db.PurchaseOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PurchaseOrderId == id, ct)
            ?? throw new DomainException("po.not_found", $"Purchase Order {id} not found.");

    public async Task UpdateDraftAsync(long id, CreatePurchaseOrderRequest req, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        if (po.Status != PurchaseOrderStatus.Draft)
            throw new DomainException("po.not_draft", "Only a Draft PO can be edited.");
        po.DocDate = req.DocDate; po.ExpectedDeliveryDate = req.ExpectedDeliveryDate;
        po.BusinessUnitId = req.BusinessUnitId; po.CurrencyCode = req.CurrencyCode;
        po.ExchangeRate = req.ExchangeRate; po.Notes = req.Notes;
        po.InternalNotes = req.InternalNotes;
        Fill(po, req);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PurchaseOrderApprovedResult> ApproveAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        string? buCode = po.BusinessUnitId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        var docNo = await numbers.NextAsync(
            po.CompanyId, po.BranchId, "PO", buCode, po.DocDate, ct);
        po.MarkApproved(tenant.UserId ?? 0, docNo, clock.UtcNow);   // SoD in entity + ck_po_sod
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Approved", fromStatus: "Draft", toStatus: "Approved", module: "purchase");
        await db.SaveChangesAsync(ct);
        return new PurchaseOrderApprovedResult(
            po.PurchaseOrderId, po.DocNo!, po.ApprovedBy!.Value, po.ApprovedAt!.Value);
    }

    public async Task MarkSentAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        if (po.Status != PurchaseOrderStatus.Approved)
            throw new DomainException("po.not_approved", "Only an Approved PO can be marked sent.");
        po.SentToVendorAt = clock.UtcNow;
        // NB: there is no "Sent" PurchaseOrderStatus enum member — the status stays
        // Approved and only SentToVendorAt is set. "Sent" here is a semantic audit label.
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "MarkedSent", fromStatus: "Approved", toStatus: "Sent", module: "purchase");
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        var fromClose = po.Status.ToString();
        po.MarkClosed(clock.UtcNow);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Closed", fromStatus: fromClose, toStatus: "Closed", module: "purchase");
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var po = await LoadAsync(id, ct);
        var fromCancel = po.Status.ToString();
        po.MarkCancelled(reason, clock.UtcNow);
        activity.Record("PurchaseOrder", po.PurchaseOrderId, po.DocNo, po.CompanyId,
            "Cancelled", fromStatus: fromCancel, toStatus: "Cancelled", note: reason, module: "purchase");
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PurchaseOrderListItem>> ListAsync(
        string? status, long? vendorId, CancellationToken ct)
    {
        Auth();
        var q = db.PurchaseOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<PurchaseOrderStatus>(status, true, out var st))
            q = q.Where(x => x.Status == st);
        if (vendorId is { } vid) q = q.Where(x => x.VendorId == vid);
        return await q.OrderByDescending(x => x.PurchaseOrderId)
            .Select(x => new PurchaseOrderListItem(
                x.PurchaseOrderId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.ExpectedDeliveryDate, x.VendorName, x.TotalAmount, x.BusinessUnitId))
            .ToListAsync(ct);
    }

    public async Task<PurchaseOrderDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        Auth();
        var po = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PurchaseOrderId == id, ct);
        if (po is null) return null;
        var vis = await db.VendorInvoices.AsNoTracking()
            .Where(v => v.PurchaseOrderId == id)
            .Select(v => new LinkedViDto(v.VendorInvoiceId, v.DocNo, v.TotalAmount))
            .ToListAsync(ct);
        var linked = vis.Sum(v => v.TotalAmount);
        // cont.79 — resolve BU code/name for display (null when no BU on the PO).
        var bu = po.BusinessUnitId is { } buId
            ? await db.BusinessUnits.AsNoTracking()
                .Where(x => x.BusinessUnitId == buId)
                .Select(x => new { x.Code, x.NameTh }).FirstOrDefaultAsync(ct)
            : null;
        // cont.94d — resolve each line's product taxonomy so a PV/derived-VAT prefill
        // knows the right VAT treatment. Authoritative from the linked Product; for an
        // ad-hoc line (no product) infer from the stored TaxRate (0 → treat as exempt so
        // the prefill carries 0% VAT, else GOOD).
        var productIds = po.Lines.Where(l => l.ProductId is not null)
            .Select(l => l.ProductId!.Value).Distinct().ToList();
        var productTypes = productIds.Count == 0
            ? new Dictionary<long, ProductType>()
            : await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId))
                .ToDictionaryAsync(p => p.ProductId, p => p.ProductType, ct);
        string LineProductType(PurchaseOrderLine l) =>
            l.ProductId is { } pid && productTypes.TryGetValue(pid, out var pt)
                ? ProductTypeCodes.ToCode(pt)
                : (l.TaxRate > 0m ? "GOOD" : "EXEMPT_GOOD");
        return new PurchaseOrderDetail(
            po.PurchaseOrderId, po.DocNo, po.Status.ToString(), po.DocDate,
            po.ExpectedDeliveryDate, po.VendorId, po.VendorName, po.BusinessUnitId,
            po.CurrencyCode, po.SubtotalAmount, po.VatAmount, po.TotalAmount,
            po.Notes, po.InternalNotes, po.ApprovedAt, po.ApprovedBy,
            po.SentToVendorAt, po.ClosedAt, po.CancellationReason,
            linked, po.TotalAmount - linked,
            po.Lines.OrderBy(l => l.LineNo).Select(l => new PurchaseOrderLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount,
                LineProductType(l))).ToList(),
            vis, bu?.Code, bu?.NameTh);   // cont.79 — BU display
    }

    public async Task<byte[]> BuildPdfAsync(long id, CancellationToken ct, bool copy = false)
    {
        Auth();
        var po = await db.PurchaseOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.PurchaseOrderId == id, ct)
            ?? throw new DomainException("po.not_found", $"Purchase Order {id} not found.");

        // Sprint 13j-PURCH Phase C — render via the shared PaperDocument mirror
        // (IDENTICAL shape to the Sales TI builder). Seller = the issuing company
        // (we are the buyer issuing the PO); Customer = the vendor. Two-box sign
        // (ผู้ขออนุมัติ / ผู้อนุมัติ). Watermark: explicit copy → "สำเนา", else "ต้นฉบับ".
        var seller = await Pdf.PaperSellerSource.FromCompanyProfileAsync(db, po.CompanyId, ct, storage);
        var model = new Pdf.PaperDocModel(
            DocType: "ใบสั่งซื้อ",
            DocTypeEn: "Purchase Order",
            DocNo: po.DocNo ?? "(ร่าง)",
            IssueDate: po.DocDate,
            Seller: seller,
            Customer: new Pdf.PaperCustomer(po.VendorName, Pdf.PaperFormat.TaxId(po.VendorTaxId)),
            Items: po.Lines.OrderBy(l => l.LineNo).Select(l => new Pdf.PaperLine(
                l.DescriptionTh, null, l.Quantity, l.UomText, l.UnitPrice, null, l.LineAmount)).ToList(),
            Summary: new Pdf.PaperSummary(
                po.SubtotalAmount, null, po.SubtotalAmount, po.VatAmount, po.TotalAmount, null),
            SignRoles: new Pdf.PaperSignRoles("ผู้ขออนุมัติ", "ผู้อนุมัติ"),
            ValidUntil: po.ExpectedDeliveryDate,
            ValidUntilLabel: po.ExpectedDeliveryDate is null ? null : "กำหนดส่ง",
            Notes: po.Notes,
            Watermark: new Pdf.PaperWatermark(
                copy ? "สำเนา" : "ต้นฉบับ",
                copy ? Pdf.PaperWatermarkVariant.Warning : Pdf.PaperWatermarkVariant.Success));
        return Pdf.PaperDocumentPdf.Render(model);
    }

    public async Task<OutstandingPoReport> OutstandingAsync(
        DateOnly asOf, long? vendorId, bool overdueOnly, CancellationToken ct)
    {
        Auth();
        var q = db.PurchaseOrders.AsNoTracking()
            .Where(x => x.Status == PurchaseOrderStatus.Approved);
        if (vendorId is { } vid) q = q.Where(x => x.VendorId == vid);
        var pos = await q.Select(x => new
        {
            x.PurchaseOrderId, x.DocNo, x.VendorName, x.ExpectedDeliveryDate, x.TotalAmount
        }).ToListAsync(ct);

        var ids = pos.Select(p => p.PurchaseOrderId).ToList();
        var viAgg = (await db.VendorInvoices.AsNoTracking()
            .Where(v => v.PurchaseOrderId != null && ids.Contains(v.PurchaseOrderId!.Value))
            .Select(v => new { v.PurchaseOrderId, v.TotalAmount })
            .ToListAsync(ct))
            .GroupBy(v => v.PurchaseOrderId!.Value)
            .ToDictionary(g => g.Key, g => (cnt: g.Count(), sum: g.Sum(x => x.TotalAmount)));

        var rows = new List<OutstandingPoRow>();
        foreach (var p in pos)
        {
            var (cnt, sum) = viAgg.GetValueOrDefault(p.PurchaseOrderId, (0, 0m));
            var overdue = p.ExpectedDeliveryDate is { } ed && ed < asOf
                ? asOf.DayNumber - ed.DayNumber : 0;
            if (overdueOnly && overdue <= 0) continue;
            rows.Add(new OutstandingPoRow(
                p.PurchaseOrderId, p.DocNo, p.VendorName, p.ExpectedDeliveryDate,
                overdue, Bucket(overdue), p.TotalAmount, cnt, sum, p.TotalAmount - sum));
        }
        return new OutstandingPoReport(asOf,
            rows.OrderByDescending(r => r.DaysOverdue).ToList());
    }

    private static string Bucket(int daysOverdue) => daysOverdue switch
    {
        <= 0 => "Current",
        <= 7 => "1-7",
        <= 14 => "8-14",
        <= 30 => "15-30",
        _ => "30+",
    };
}
