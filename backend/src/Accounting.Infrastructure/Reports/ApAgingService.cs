using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Sprint 13j-PURCH Phase B — AP Aging report. Read-only projection over
/// posted, not-fully-settled vendor invoices, bucketed by age as of a date.
///
/// Outstanding source (deviation D2): <c>VendorInvoice.SettledAmount</c> /
/// <c>SettlementStatus</c> ARE maintained on PV post
/// (PaymentVoucherService.PostAsync :293-295), so outstanding is the cheap,
/// stored value <c>TotalAmount − SettledAmount</c> filtered by
/// <c>SettlementStatus != "PAID"</c> — no PaymentVoucherApplication SUM needed.
///
/// Multi-tenant: explicit <c>CompanyId == tenant.CompanyId</c> predicate
/// (CLAUDE.md §4.7) in addition to the global query filter.
/// </summary>
public sealed class ApAgingService(AccountingDbContext db, ITenantContext tenant) : IApAgingService
{
    public async Task<ApAgingReport> GetAsync(DateOnly asOf, long? vendorId, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var q = db.VendorInvoices.AsNoTracking()
            .Where(v => v.CompanyId == tenant.CompanyId        // §4.7 mandatory tenant filter
                     && v.Status == DocumentStatus.Posted
                     && v.SettlementStatus != "PAID");
        if (vendorId is { } vid) q = q.Where(v => v.VendorId == vid);

        var raw = await q.Select(v => new
        {
            v.VendorId,
            v.VendorName,
            v.VendorTaxId,
            v.DocDate,
            Outstanding = v.TotalAmount - v.SettledAmount,
        }).ToListAsync(ct);

        var rows = raw
            .Where(x => x.Outstanding > 0m)
            .GroupBy(x => x.VendorId)
            .Select(g =>
            {
                var first = g.First();
                decimal cur = 0m, b3160 = 0m, b6190 = 0m, over90 = 0m;
                foreach (var x in g)
                {
                    var age = asOf.DayNumber - x.DocDate.DayNumber;
                    if (age <= 30) cur += x.Outstanding;          // includes future-dated (age < 0)
                    else if (age <= 60) b3160 += x.Outstanding;
                    else if (age <= 90) b6190 += x.Outstanding;
                    else over90 += x.Outstanding;
                }
                return new ApAgingRow(
                    (int)first.VendorId, first.VendorName, first.VendorTaxId ?? "",
                    cur, b3160, b6190, over90, cur + b3160 + b6190 + over90);
            })
            .OrderByDescending(r => r.Total)
            .ToList();

        var totals = new ApAgingRow(
            0, "TOTAL", "",
            rows.Sum(r => r.Current),
            rows.Sum(r => r.Bucket31To60),
            rows.Sum(r => r.Bucket61To90),
            rows.Sum(r => r.BucketOver90),
            rows.Sum(r => r.Total));

        return new ApAgingReport(asOf, rows, totals);
    }
}
