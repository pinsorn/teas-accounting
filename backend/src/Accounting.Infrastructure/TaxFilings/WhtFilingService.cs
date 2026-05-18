using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// Sprint 9 Part C — ภ.ง.ด.3 / ภ.ง.ด.53 / ภ.ง.ด.54 (AP-side WHT certificates,
/// Direction='P') + ภ.พ.36 reverse-charge (consumes Sprint 8.7
/// requires_pnd36_reverse_charge; on finalize posts the Dr 1170 / Cr 2151 JV,
/// net 0). Finalize persists to the immutable tax.tax_filings history via the
/// shared <see cref="TaxFilingStore"/>. Tenant-scoped via the query filter.
/// WHT period = CertDate month; ภ.พ.36 period = doc DocDate month. All ภ.ง.ด.
/// / ภ.พ.36 are due the 7th of the following month.
/// </summary>
public sealed class WhtFilingService(
    AccountingDbContext db,
    IJournalService journals,
    ITenantContext tenant,
    IClock clock,
    IOptions<VatModeOptions> opts,
    IRdEfilingClient rd) : IWhtFilingService
{
    private string SubmissionMode() =>
        opts.Value.Pnd30SubmissionMode?.ToLowerInvariant() == "auto" ? "auto" : "manual";

    private void EnsureAuth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    public Task<WhtFiling> GeneratePnd3Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND3", period, mode,
            q => q.Where(w => w.PayeeType == CustomerType.Individual
                           && w.FormType != WhtFormType.Pnd54), ct);

    public Task<WhtFiling> GeneratePnd53Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND53", period, mode,
            q => q.Where(w => w.PayeeType == CustomerType.Corporate
                           && w.FormType != WhtFormType.Pnd54), ct);

    public Task<WhtFiling> GeneratePnd54Async(int period, TaxFilingMode mode, CancellationToken ct)
        => WhtAsync("PND54", period, mode,
            q => q.Where(w => w.FormType == WhtFormType.Pnd54), ct);

    private async Task<WhtFiling> WhtAsync(
        string formType, int period, TaxFilingMode mode,
        Func<IQueryable<Domain.Entities.Tax.WhtCertificate>,
             IQueryable<Domain.Entities.Tax.WhtCertificate>> filter,
        CancellationToken ct)
    {
        EnsureAuth();
        var (from, to) = TaxFilingPeriod.MonthRange(period);

        var q = db.WhtCertificates.AsNoTracking()
            .Where(w => w.Direction == "P"
                     && w.Status == DocumentStatus.Posted
                     && w.CertDate >= from && w.CertDate <= to);
        q = filter(q);

        var rows = await q
            .OrderBy(w => w.CertDate).ThenBy(w => w.DocNo)
            .Select(w => new WhtFilingRow(
                w.DocNo, w.PayeeName, w.PayeeTaxId, w.IncomeTypeCode,
                w.IncomeAmount, w.WhtRate, w.WhtAmount))
            .ToListAsync(ct);

        var totals = new WhtFilingTotals(
            rows.Sum(r => r.IncomeAmount), rows.Sum(r => r.WhtAmount));
        var sub = SubmissionMode();
        var due = TaxFilingPeriod.DueDate(period, 7);
        var status = mode == TaxFilingMode.Finalize
            ? TaxFilingStore.FinalStatus(sub) : "Preview";

        var filing = new WhtFiling(period, formType, due, sub, rows, totals, status);
        if (mode == TaxFilingMode.Finalize)
            await TaxFilingStore.FinalizeAsync(
                db, tenant, clock, formType, period, sub, filing, ct, rd);
        return filing;
    }

    public async Task<Pnd36Filing> GeneratePnd36Async(
        int period, TaxFilingMode mode, CancellationToken ct)
    {
        EnsureAuth();
        var (from, to) = TaxFilingPeriod.MonthRange(period);
        const decimal vatRate = 0.07m;

        // Foreign-service reverse-charge docs: VI + PV flagged in Sprint 8.7,
        // posted in the period. vat = 7% of the foreign-service (subtotal).
        var viRows = await db.VendorInvoices.AsNoTracking()
            .Where(v => v.RequiresPnd36ReverseCharge
                     && v.Status == DocumentStatus.Posted
                     && v.DocDate >= from && v.DocDate <= to)
            .Join(db.Vendors.AsNoTracking(), v => v.VendorId, ven => ven.VendorId,
                  (v, ven) => new { v.VendorName, ven.CountryCode, v.DocNo, v.SubtotalAmount })
            .ToListAsync(ct);
        var pvRows = await db.PaymentVouchers.AsNoTracking()
            .Where(p => p.RequiresPnd36ReverseCharge
                     && p.Status == DocumentStatus.Posted
                     && p.DocDate >= from && p.DocDate <= to)
            .Join(db.Vendors.AsNoTracking(), p => p.VendorId, ven => ven.VendorId,
                  (p, ven) => new { p.VendorName, ven.CountryCode, p.DocNo, p.SubtotalAmount })
            .ToListAsync(ct);

        var rows = viRows.Select(x => new Pnd36Row(
                x.VendorName, x.CountryCode, x.DocNo ?? "",
                x.SubtotalAmount, vatRate,
                decimal.Round(x.SubtotalAmount * vatRate, 2)))
            .Concat(pvRows.Select(x => new Pnd36Row(
                x.VendorName, x.CountryCode, x.DocNo ?? "",
                x.SubtotalAmount, vatRate,
                decimal.Round(x.SubtotalAmount * vatRate, 2))))
            .OrderBy(r => r.RefDoc)
            .ToList();

        var totalService = rows.Sum(r => r.ServiceAmountThb);
        var totalVat = rows.Sum(r => r.VatAmount);
        var sub = SubmissionMode();
        var due = TaxFilingPeriod.DueDate(period, 7);
        long? jvId = null;

        if (mode == TaxFilingMode.Finalize)
        {
            // Guard BEFORE posting the JV so a re-finalize can't orphan a JV.
            if (await db.TaxFilings.AnyAsync(
                    f => f.FormType == "PND36" && f.Period == period, ct))
                throw new DomainException("tax_filing.already_finalized",
                    $"PND36 for period {period} is already finalized (immutable).");

            if (totalVat > 0m)
                jvId = await PostReverseChargeJvAsync(period, to, totalVat, ct);

            var status = TaxFilingStore.FinalStatus(sub);
            var filing0 = new Pnd36Filing(
                period, due, sub, rows, totalService, totalVat, jvId, status);
            await TaxFilingStore.FinalizeAsync(
                db, tenant, clock, "PND36", period, sub, filing0, ct, rd);
            return filing0;
        }

        return new Pnd36Filing(
            period, due, sub, rows, totalService, totalVat, null, "Preview");
    }

    /// <summary>
    /// C5 auto-JV: Dr 1170 Input VAT / Cr 2151 Output VAT (net 0). Output side
    /// remits in next month's ภ.พ.30; input side claims back — effective net 0.
    /// </summary>
    private async Task<long> PostReverseChargeJvAsync(
        int period, DateOnly docDate, decimal vat, CancellationToken ct)
    {
        var accts = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountCode == "1170" || a.AccountCode == "2151")
            .Select(a => new { a.AccountId, a.AccountCode })
            .ToListAsync(ct);
        var input  = accts.FirstOrDefault(a => a.AccountCode == "1170")?.AccountId
            ?? throw new DomainException("gl.account_missing", "Account 1170 (Input VAT) not found.");
        var output = accts.FirstOrDefault(a => a.AccountCode == "2151")?.AccountId
            ?? throw new DomainException("gl.account_missing", "Account 2151 (Output VAT) not found.");

        var req = new CreateJournalRequest(
            docDate, docDate,
            $"ภ.พ.36 reverse charge {period}", $"PND36-{period}", "THB", 1m,
            [
                new JournalLineInput(input,  vat, 0m, "Input VAT (claim back)", $"PND36-{period}", null),
                new JournalLineInput(output, 0m, vat, "Output VAT (reverse charge)", $"PND36-{period}", null),
            ]);
        var jid = await journals.CreateDraftAsync(req, ct);
        var posted = await journals.PostAsync(jid, ct);
        return posted.JournalId;
    }
}
