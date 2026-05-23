using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

public sealed class SalesOrderService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IActivityRecorder activity) : ISalesOrderService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreateSalesOrderRequest req, CancellationToken ct)
    {
        Auth();
        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        var so = new SalesOrder
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = SalesOrderStatus.Draft, DocDate = req.DocDate,
            ExpectedDeliveryDate = req.ExpectedDeliveryDate,
            CustomerId = cust.CustomerId, CustomerName = cust.NameTh,
            CustomerAddress = cust.BillingAddress, CustomerTaxId = cust.TaxId,
            CustomerType = cust.CustomerType, BusinessUnitId = req.BusinessUnitId,
            QuotationId = req.FromQuotationId, CurrencyCode = req.CurrencyCode,
            ExchangeRate = req.ExchangeRate, Notes = req.Notes,
        };
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRate);
            so.Lines.Add(new SalesOrderLine
            {
                LineNo = n++, ProductId = l.ProductId, ProductType = l.ProductType ?? "GOOD", DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            so.SubtotalAmount += net; so.VatAmount += vat; so.TotalAmount += total;
        }
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync(ct);
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Created", toStatus: "Draft");
        await db.SaveChangesAsync(ct);
        return so.SalesOrderId;
    }

    public async Task PostAsync(long id, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == id, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {id} not found.");
        if (so.Status != SalesOrderStatus.Draft)
            throw new DomainException("so.bad_status", "Only a Draft SO can be posted.");
        so.DocNo = await SubNumAsync("SO", so.BusinessUnitId, so.DocDate, ct);
        so.Status = SalesOrderStatus.Posted;
        so.PostedAt = clock.UtcNow; so.PostedBy = tenant.UserId;
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Posted", "Draft", "Posted");
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> CreateDeliveryOrderAsync(
        long salesOrderId, CreateDeliveryOrderRequest req, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == salesOrderId, ct)
            ?? throw new DomainException("so.not_found", $"Sales Order {salesOrderId} not found.");
        if (so.Status != SalesOrderStatus.Posted)
            throw new DomainException("so.not_posted",
                "Sales Order must be Posted before creating a Delivery Order.");

        var dord = new DeliveryOrder
        {
            CompanyId = so.CompanyId, BranchId = so.BranchId,
            Status = DeliveryOrderStatus.Draft, DocDate = req.DocDate,
            CustomerId = so.CustomerId, CustomerName = so.CustomerName,
            CustomerAddress = so.CustomerAddress, CustomerTaxId = so.CustomerTaxId,
            CustomerType = so.CustomerType,
            BusinessUnitId = so.BusinessUnitId,           // BU cascade SO→DO
            SalesOrderId = so.SalesOrderId,
            IsCombinedWithTi = req.IsCombinedWithTi, Notes = req.Notes,
            CurrencyCode = so.CurrencyCode, ExchangeRate = so.ExchangeRate,
        };
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRate);
            dord.Lines.Add(new DeliveryOrderLine
            {
                LineNo = n++, SalesOrderLineId = l.SalesOrderLineId,
                ProductId = l.ProductId, ProductType = l.ProductType ?? "GOOD", DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            dord.SubtotalAmount += net; dord.VatAmount += vat; dord.TotalAmount += total;

            if (l.SalesOrderLineId is { } solId)
            {
                var sol = so.Lines.FirstOrDefault(x => x.LineId == solId);
                if (sol is not null) sol.DeliveredQuantity += l.Quantity;
            }
        }
        db.DeliveryOrders.Add(dord);

        // Auto-close the SO when every line is fully delivered.
        var soClosed = false;
        if (so.Lines.All(x => x.DeliveredQuantity >= x.Quantity) && so.Lines.Count > 0)
        {
            so.Status = SalesOrderStatus.Closed;
            so.ClosedAt = clock.UtcNow;
            soClosed = true;
        }
        await db.SaveChangesAsync(ct);
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Created",
            toStatus: "Draft", note: $"จากใบสั่งขาย {so.DocNo ?? so.SalesOrderId.ToString()}");
        activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "CreatedDeliveryOrder",
            note: $"→ ใบส่งของ {dord.DeliveryOrderId}");
        if (soClosed)
            activity.Record("SalesOrder", so.SalesOrderId, so.DocNo, so.CompanyId, "Closed", "Posted", "Closed");
        await db.SaveChangesAsync(ct);
        return dord.DeliveryOrderId;
    }

    public async Task<IReadOnlyList<SalesOrderListItem>> ListAsync(string? status, CancellationToken ct)
    {
        Auth();
        var qy = db.SalesOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<SalesOrderStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        return await qy.OrderByDescending(x => x.SalesOrderId)
            .Select(x => new SalesOrderListItem(
                x.SalesOrderId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.CustomerName, x.TotalAmount, x.QuotationId)).ToListAsync(ct);
    }

    public async Task<SalesOrderDetail?> GetAsync(long id, CancellationToken ct)
    {
        Auth();
        var so = await db.SalesOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.SalesOrderId == id, ct);
        return so is null ? null : new SalesOrderDetail(
            so.SalesOrderId, so.DocNo, so.Status.ToString(), so.DocDate,
            so.CustomerId, so.CustomerName, so.BusinessUnitId,
            so.SubtotalAmount, so.VatAmount, so.TotalAmount, so.QuotationId,
            so.Lines.OrderBy(l => l.LineNo).Select(l => new ChainLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount)).ToList());
    }

    private async Task<string> SubNumAsync(
        string prefix, int? buId, DateOnly docDate, CancellationToken ct)
    {
        string? buCode = buId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        return await numbers.NextAsync(tenant.CompanyId, tenant.BranchId, prefix, buCode, docDate, ct);
    }
}

public sealed class DeliveryOrderService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, ITaxInvoiceService taxInvoices,
    IActivityRecorder activity)
    : IDeliveryOrderService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreateDeliveryOrderRequest req, CancellationToken ct)
    {
        Auth();
        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");
        var dord = new DeliveryOrder
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = DeliveryOrderStatus.Draft, DocDate = req.DocDate,
            CustomerId = cust.CustomerId, CustomerName = cust.NameTh,
            CustomerAddress = cust.BillingAddress, CustomerTaxId = cust.TaxId,
            CustomerType = cust.CustomerType, BusinessUnitId = req.BusinessUnitId,
            SalesOrderId = req.FromSalesOrderId, IsCombinedWithTi = req.IsCombinedWithTi,
            Notes = req.Notes,
        };
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRate);
            dord.Lines.Add(new DeliveryOrderLine
            {
                LineNo = n++, SalesOrderLineId = l.SalesOrderLineId,
                ProductId = l.ProductId, ProductType = l.ProductType ?? "GOOD", DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            dord.SubtotalAmount += net; dord.VatAmount += vat; dord.TotalAmount += total;
        }
        db.DeliveryOrders.Add(dord);
        await db.SaveChangesAsync(ct);
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Created", toStatus: "Draft");
        await db.SaveChangesAsync(ct);
        return dord.DeliveryOrderId;
    }

    // Sprint 13h P9 — Draft → Issued. Doc number allocated; TI NOT yet created.
    public async Task IssueAsync(long id, CancellationToken ct)
    {
        Auth();
        var dord = await db.DeliveryOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {id} not found.");
        if (dord.Status != DeliveryOrderStatus.Draft)
            throw new DomainException("do.bad_status", "Only a Draft DO can be issued.");

        string? buCode = dord.BusinessUnitId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        dord.DocNo = await numbers.NextAsync(
            tenant.CompanyId, tenant.BranchId, "DO", buCode, dord.DocDate, ct);
        dord.Status = DeliveryOrderStatus.Issued;
        dord.PostedAt = clock.UtcNow; dord.PostedBy = tenant.UserId;
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Issued", "Draft", "Issued");
        await db.SaveChangesAsync(ct);
    }

    // cont.69 Phase 1 — Issued → Delivered is a STATUS CHANGE ONLY. The old
    // IsCombinedWithTi auto-TI path is removed: a non-VAT DO marking delivered no longer
    // funnels through EnsureVatRegistered → 422. The new linear flow issues the
    // Tax Invoice manually from the Invoice (BillingNote) step, never from delivery.
    // Legacy DOs already linked to a TI (dord.TaxInvoiceId set) keep that history.
    public async Task MarkDeliveredAsync(long id, CancellationToken ct)
    {
        Auth();
        var dord = await db.DeliveryOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {id} not found.");
        if (dord.Status != DeliveryOrderStatus.Issued)
            throw new DomainException("do.bad_status", "Only an Issued DO can be marked Delivered.");

        dord.Status = DeliveryOrderStatus.Delivered;
        dord.DeliveredAt = clock.UtcNow;
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId, "Delivered", "Issued", "Delivered");
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> CreateTaxInvoiceAsync(long deliveryOrderId, CancellationToken ct)
    {
        Auth();
        var dord = await db.DeliveryOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == deliveryOrderId, ct)
            ?? throw new DomainException("do.not_found", $"Delivery Order {deliveryOrderId} not found.");
        if (dord.Status != DeliveryOrderStatus.Delivered)
            throw new DomainException("do.not_delivered", "DO must be Delivered to create a Tax Invoice.");
        if (dord.TaxInvoiceId is not null)
            throw new DomainException("do.ti_exists", "This DO already has a linked Tax Invoice.");
        // Pattern Y — separate manual step (non-combined DO, TI issued later).
        dord.TaxInvoiceId = await GenerateTiAsync(dord, ct);
        activity.Record("DeliveryOrder", dord.DeliveryOrderId, dord.DocNo, dord.CompanyId,
            "CreatedTaxInvoice", note: $"→ ใบกำกับภาษี {dord.TaxInvoiceId}");
        await db.SaveChangesAsync(ct);
        return dord.TaxInvoiceId.Value;
    }

    private async Task<long> GenerateTiAsync(DeliveryOrder dord, CancellationToken ct)
    {
        var lines = dord.Lines.OrderBy(l => l.LineNo).Select(l => new TaxInvoiceLineInput(
            l.ProductId, null, l.DescriptionTh, l.Quantity, 1, l.UomText,
            l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate,
            l.ProductType)).ToList();   // Sprint 13h P7 — DO→TI cascade
        var tiId = await taxInvoices.CreateDraftAsync(new CreateTaxInvoiceRequest(
            dord.DocDate, dord.CustomerId, false, dord.CurrencyCode, dord.ExchangeRate,
            dord.Notes, null, null, lines, dord.BusinessUnitId), ct);   // BU cascade DO→TI
        await taxInvoices.PostAsync(tiId, ct);
        return tiId;
    }

    public async Task<IReadOnlyList<DeliveryOrderListItem>> ListAsync(string? status, CancellationToken ct)
    {
        Auth();
        var qy = db.DeliveryOrders.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<DeliveryOrderStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        return await qy.OrderByDescending(x => x.DeliveryOrderId)
            .Select(x => new DeliveryOrderListItem(
                x.DeliveryOrderId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.CustomerName, x.IsCombinedWithTi, x.TaxInvoiceId, x.SalesOrderId,
                x.CustomerId, x.TotalAmount))
            .ToListAsync(ct);
    }

    public async Task<DeliveryOrderDetail?> GetAsync(long id, CancellationToken ct)
    {
        Auth();
        var d = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.DeliveryOrderId == id, ct);
        if (d is null) return null;
        // cont.69 — the Invoice created from this DO (one-per-DO), so the FE can hide
        // the "create Invoice" action once it exists.
        var billingNoteId = await db.BillingNotes.AsNoTracking()
            .Where(b => b.DeliveryOrderId == id)
            .Select(b => (long?)b.BillingNoteId).FirstOrDefaultAsync(ct);
        return new DeliveryOrderDetail(
            d.DeliveryOrderId, d.DocNo, d.Status.ToString(), d.DocDate,
            d.CustomerId, d.CustomerName, d.BusinessUnitId, d.IsCombinedWithTi,
            d.TaxInvoiceId, d.SalesOrderId, d.SubtotalAmount, d.VatAmount, d.TotalAmount,
            d.Lines.OrderBy(l => l.LineNo).Select(l => new ChainLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount)).ToList(),
            billingNoteId);
    }
}
