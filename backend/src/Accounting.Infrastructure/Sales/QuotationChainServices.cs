using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Sales;

// Sprint 10 Part B — Q → SO → DO chain. Numbering on the POST-equivalent
// (Quotation = Send, SO = Post, DO = Post) via INumberSequenceService with the
// BU code as sub-prefix. BU cascades Q→SO→DO→TI.

internal static class ChainMath
{
    public static (decimal net, decimal vat, decimal total) Line(
        decimal qty, decimal price, decimal discPct, decimal rate)
    {
        var gross = Math.Round(qty * price, 4, MidpointRounding.AwayFromZero);
        var net = discPct > 0
            ? Math.Round(gross * (1m - discPct / 100m), 4, MidpointRounding.AwayFromZero)
            : gross;
        var vat = Math.Round(net * rate, 2, MidpointRounding.AwayFromZero);
        return (net, vat, net + vat);
    }
}

public sealed class QuotationService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers) : IQuotationService
{
    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public async Task<long> CreateDraftAsync(CreateQuotationRequest req, CancellationToken ct)
    {
        Auth();

        // Sprint 14 P7 — per-key BU lock (SO/DO inherit this locked Q BU; v1
        // exposes no direct SO/DO create, so the lock at Q entry is sufficient).
        var (effBu, buErr) = ApiKeyBuBinding.Resolve(
            req.BusinessUnitId, tenant.ApiKeyDefaultBusinessUnitId);
        if (buErr is not null)
            throw new DomainException(buErr,
                $"This API key is bound to Business Unit {tenant.ApiKeyDefaultBusinessUnitId}; " +
                $"request specified {req.BusinessUnitId}.");
        req = req with { BusinessUnitId = effBu };

        var cust = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CustomerId == req.CustomerId, ct)
            ?? throw new DomainException("customer.not_found", "Customer not found.");

        var q = new Quotation
        {
            CompanyId = tenant.CompanyId, BranchId = tenant.BranchId,
            Status = QuotationStatus.Draft, DocDate = req.DocDate,
            ValidUntilDate = req.ValidUntilDate, CustomerId = cust.CustomerId,
            CustomerName = cust.NameTh, CustomerAddress = cust.BillingAddress,
            CustomerTaxId = cust.TaxId, CustomerType = cust.CustomerType,
            BusinessUnitId = req.BusinessUnitId, CurrencyCode = req.CurrencyCode,
            ExchangeRate = req.ExchangeRate, Notes = req.Notes,
            InternalNotes = req.InternalNotes,
            ShowWhtNote = cust.CustomerType == CustomerType.Corporate,
        };
        int n = 1;
        foreach (var l in req.Lines)
        {
            var (net, vat, total) = ChainMath.Line(l.Quantity, l.UnitPrice, l.DiscountPercent, l.TaxRate);
            q.Lines.Add(new QuotationLine
            {
                LineNo = n++, ProductId = l.ProductId, DescriptionTh = l.DescriptionTh,
                Quantity = l.Quantity, UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = net,
                TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = vat, TotalAmount = total,
            });
            q.SubtotalAmount += net; q.VatAmount += vat; q.TotalAmount += total;
        }
        db.Quotations.Add(q);
        await db.SaveChangesAsync(ct);
        return q.QuotationId;
    }

    private async Task<Quotation> LoadAsync(long id, CancellationToken ct) =>
        await db.Quotations.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.QuotationId == id, ct)
            ?? throw new DomainException("quotation.not_found", $"Quotation {id} not found.");

    public async Task SendAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Draft)
            throw new DomainException("quotation.bad_status", "Only a Draft quotation can be sent.");
        q.DocNo = await SubPrefixNumberAsync("QT", q.BusinessUnitId, q.DocDate, ct);
        q.Status = QuotationStatus.Sent;
        q.SentAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task AcceptAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Sent)
            throw new DomainException("quotation.bad_status", "Only a Sent quotation can be accepted.");
        q.Status = QuotationStatus.Accepted;
        q.AcceptedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status is not (QuotationStatus.Sent or QuotationStatus.Draft))
            throw new DomainException("quotation.bad_status", "Cannot reject in this status.");
        q.Status = QuotationStatus.Rejected; q.RejectedReason = reason;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(long id, string reason, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status is QuotationStatus.Accepted && q.ConvertedToSoId is not null)
            throw new DomainException("quotation.converted",
                "Cannot cancel — already converted to a Sales Order.");
        q.Status = QuotationStatus.Cancelled; q.CancelledReason = reason;
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> ConvertToSalesOrderAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await LoadAsync(id, ct);
        if (q.Status != QuotationStatus.Accepted)
            throw new DomainException("quotation.not_accepted",
                "Quotation must be Accepted before converting to a Sales Order.");
        if (q.ConvertedToSoId is not null)
            throw new DomainException("quotation.converted",
                "Quotation already converted.");

        var so = new SalesOrder
        {
            CompanyId = q.CompanyId, BranchId = q.BranchId,
            Status = SalesOrderStatus.Draft, DocDate = clock.UtcNow.UtcDateTime.ToDateOnly(),
            CustomerId = q.CustomerId, CustomerName = q.CustomerName,
            CustomerAddress = q.CustomerAddress, CustomerTaxId = q.CustomerTaxId,
            CustomerType = q.CustomerType, BusinessUnitId = q.BusinessUnitId,
            QuotationId = q.QuotationId, CurrencyCode = q.CurrencyCode,
            ExchangeRate = q.ExchangeRate, SubtotalAmount = q.SubtotalAmount,
            VatAmount = q.VatAmount, TotalAmount = q.TotalAmount,
        };
        foreach (var l in q.Lines.OrderBy(x => x.LineNo))
            so.Lines.Add(new SalesOrderLine
            {
                LineNo = l.LineNo, ProductId = l.ProductId, ProductCode = l.ProductCode,
                DescriptionTh = l.DescriptionTh, Quantity = l.Quantity,
                UomText = l.UomText, UnitPrice = l.UnitPrice,
                DiscountPercent = l.DiscountPercent, LineAmount = l.LineAmount,
                TaxCodeId = l.TaxCodeId, TaxCode = l.TaxCode, TaxRate = l.TaxRate,
                TaxAmount = l.TaxAmount, TotalAmount = l.TotalAmount,
            });
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync(ct);

        q.ConvertedToSoId = so.SalesOrderId;
        await db.SaveChangesAsync(ct);
        return so.SalesOrderId;
    }

    public async Task<IReadOnlyList<QuotationListItem>> ListAsync(string? status, CancellationToken ct)
    {
        Auth();
        var qy = db.Quotations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<QuotationStatus>(status, true, out var st))
            qy = qy.Where(x => x.Status == st);
        return await qy.OrderByDescending(x => x.QuotationId)
            .Select(x => new QuotationListItem(
                x.QuotationId, x.DocNo, x.Status.ToString(), x.DocDate,
                x.ValidUntilDate, x.CustomerName, x.TotalAmount, x.ConvertedToSoId))
            .ToListAsync(ct);
    }

    public async Task<QuotationDetail?> GetAsync(long id, CancellationToken ct)
    {
        Auth();
        var q = await db.Quotations.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.QuotationId == id, ct);
        return q is null ? null : new QuotationDetail(
            q.QuotationId, q.DocNo, q.Status.ToString(), q.DocDate, q.ValidUntilDate,
            q.CustomerId, q.CustomerName, q.BusinessUnitId, q.CurrencyCode,
            q.SubtotalAmount, q.VatAmount, q.TotalAmount, q.ShowWhtNote,
            q.ConvertedToSoId, q.Notes,
            q.Lines.OrderBy(l => l.LineNo).Select(l => new ChainLineDto(
                l.LineNo, l.ProductId, l.ProductCode, l.DescriptionTh, l.Quantity,
                l.UomText, l.UnitPrice, l.LineAmount, l.TaxAmount, l.TotalAmount)).ToList());
    }

    private async Task<string> SubPrefixNumberAsync(
        string prefix, int? buId, DateOnly docDate, CancellationToken ct)
    {
        string? buCode = buId is { } b
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == b)
                .Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        return await numbers.NextAsync(tenant.CompanyId, tenant.BranchId, prefix, buCode, docDate, ct);
    }
}

internal static class ClockDateExt
{
    public static DateOnly ToDateOnly(this DateTime dt) => DateOnly.FromDateTime(dt);
}
