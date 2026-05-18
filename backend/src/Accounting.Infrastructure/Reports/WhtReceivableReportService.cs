using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Reports;

/// <summary>
/// Basic AR-WHT reports (full versions Sprint 9). Tenant-scoped via the global
/// query filter. No 1180 settlement is modelled this sprint, so every posted
/// WHT receipt is treated as still outstanding for aging.
/// </summary>
public sealed class WhtReceivableReportService(AccountingDbContext db, IClock clock)
    : IWhtReceivableReportService
{
    public async Task<WhtReceivableRegister> GetRegisterAsync(
        DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var rows = await db.Receipts.AsNoTracking()
            .Where(r => r.Status == DocumentStatus.Posted
                        && r.WhtAmount > 0m
                        && r.DocDate >= fromDate && r.DocDate <= toDate)
            .OrderBy(r => r.DocDate).ThenBy(r => r.ReceiptId)
            .Select(r => new WhtReceivableRegisterRow(
                r.DocNo!, r.DocDate, r.CustomerName, r.CustomerTaxId,
                r.WhtAmount, r.CustomerWhtCertNo))
            .ToListAsync(ct);
        return new WhtReceivableRegister(fromDate, toDate, rows, rows.Sum(x => x.WhtAmount));
    }

    public async Task<WhtReceivableAging> GetAgingAsync(CancellationToken ct)
    {
        var today = clock.UtcNow.UtcDateTime.Date;
        var posted = await db.Receipts.AsNoTracking()
            .Where(r => r.Status == DocumentStatus.Posted && r.WhtAmount > 0m)
            .OrderBy(r => r.CustomerName).ThenBy(r => r.DocDate)
            .Select(r => new { r.CustomerName, r.CustomerTaxId, r.DocNo,
                               r.DocDate, r.WhtAmount, r.PostedAt })
            .ToListAsync(ct);

        var docNos = posted.Select(r => r.DocNo).ToList();
        var certs = await db.WhtCertificates.AsNoTracking()
            .Where(w => w.Direction == "R" && w.ReceiptId != null)
            .Select(w => new { w.ReceiptId, w.CertReceivedAt, w.ReconciledAt })
            .ToListAsync(ct);
        var rcIds = await db.Receipts.AsNoTracking()
            .Where(r => docNos.Contains(r.DocNo))
            .Select(r => new { r.ReceiptId, r.DocNo }).ToListAsync(ct);
        var certByDoc = (from c in certs
                         join r in rcIds on c.ReceiptId equals r.ReceiptId
                         select new { r.DocNo, c.CertReceivedAt, c.ReconciledAt })
            .ToDictionary(x => x.DocNo!, x => x);

        var rows = posted.Select(r =>
        {
            var c = certByDoc.GetValueOrDefault(r.DocNo!);
            return new WhtReceivableAgingRow(
                r.CustomerName, r.CustomerTaxId, r.DocNo!, r.DocDate, r.WhtAmount,
                Math.Max(0, (today - (r.PostedAt ?? default).UtcDateTime.Date).Days),
                c?.CertReceivedAt != null, c?.ReconciledAt != null);
        }).ToList();

        var buckets = new WhtReceivableAgingBuckets(
            rows.Where(x => x.AgeDays <= 30).Sum(x => x.WhtAmount),
            rows.Where(x => x.AgeDays is > 30 and <= 60).Sum(x => x.WhtAmount),
            rows.Where(x => x.AgeDays is > 60 and <= 90).Sum(x => x.WhtAmount),
            rows.Where(x => x.AgeDays > 90).Sum(x => x.WhtAmount));
        return new WhtReceivableAging(rows, rows.Sum(x => x.WhtAmount), buckets);
    }
}
