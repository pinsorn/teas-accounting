using Accounting.Application.Abstractions;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// cont.82.1 P2 — builds the RD WHT batch-upload file (FORMAT กลาง V2.0) from the period's
/// posted 50ทวิ (WhtCertificate, Direction='P'). Read-only (AsNoTracking) — never mutates a
/// posted cert. Same form-filter as <see cref="WhtFilingService"/> so the file matches the
/// preview totals. Tenant isolation via the EF global query filter.
///
/// MVP = ภ.ง.ด.53 (corporate payees). ภ.ง.ด.3 is accepted but the Vendor master only holds a
/// single free-text address, so the RD-mandatory AMPHUR/PROVINCE/POSTAL_CODE come out blank —
/// the user must complete them in RD Prep (see spec §4 G1).
/// </summary>
public sealed class WhtBatchExportService(
    AccountingDbContext db, ITenantContext tenant, IConfiguration config)
    : IWhtBatchExportService
{
    public async Task<WhtBatchFile> BuildAsync(string formType, int period, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var tax = (formType ?? "").Trim().ToUpperInvariant();
        if (tax is not ("PND53" or "PND3"))
            throw new DomainException("wht_batch.unsupported_form",
                $"Batch file supports PND53 (and PND3); '{formType}' is not supported.");

        var (from, to) = TaxFilingPeriod.MonthRange(period);

        var q = db.WhtCertificates.AsNoTracking()
            .Where(w => w.Direction == "P"
                     && w.Status == DocumentStatus.Posted
                     && w.CertDate >= from && w.CertDate <= to
                     && w.FormType != WhtFormType.Pnd54);
        q = tax == "PND53"
            ? q.Where(w => w.PayeeType == CustomerType.Corporate)
            : q.Where(w => w.PayeeType == CustomerType.Individual);

        var certs = await q
            .OrderBy(w => w.PayeeTaxId).ThenBy(w => w.CertDate).ThenBy(w => w.DocNo)
            .Select(w => new
            {
                w.PayeeTaxId, w.PayeeName, w.PayerTaxId, w.PayerBranchCode,
                w.PayerName, w.CertDate, w.WhtRate, w.IncomeAmount, w.WhtAmount,
                w.IncomeTypeCode, w.IncomeDescription, w.BranchId,
            })
            .ToListAsync(ct);

        if (certs.Count == 0)
            throw new DomainException("wht_batch.no_data",
                $"No posted {tax} withholding certificates in period {period}.");

        // M/O guard: NID/PIN is Mandatory. Fail loudly listing offenders rather than emit a
        // blank-id row the RD portal would reject (advisor note).
        var missing = certs
            .Where(c => string.IsNullOrWhiteSpace(c.PayeeTaxId))
            .Select(c => c.PayeeName).Distinct().ToList();
        if (missing.Count > 0)
            throw new DomainException("wht_batch.missing_tax_id",
                $"{missing.Count} payee(s) have no tax id and cannot be filed: "
                + string.Join(", ", missing.Take(10)));

        var first = certs[0];
        var sectionDefaults = (A: true, B: false, C: false);  // ม.3 เตรส = the everyday WHT section
        var header = new WhtBatchFormat.Header(
            TaxType: tax,
            PayerTaxId: first.PayerTaxId,
            PayerBranch: first.PayerBranchCode,
            DeptName: "สำนักงานใหญ่",
            Period: period,
            SectionA: sectionDefaults.A,
            SectionB: sectionDefaults.B,
            SectionC: sectionDefaults.C,
            BranchType: "V",                                  // TEAS companies are VAT-registered
            UserId: config["Tax:Rd:UserId"]);

        var isIndividual = tax == "PND3";
        var payees = certs
            .GroupBy(c => c.PayeeTaxId!)
            .Select(g => new WhtBatchFormat.Payee(
                TaxId: g.Key,
                TitleName: isIndividual ? "-" : "",
                FirstName: g.First().PayeeName,
                LastName: "",
                BranchNo: BranchCode(g.First().PayerBranchCode),
                Incomes: g.Select(c => new WhtBatchFormat.Income(
                    PaidDate: c.CertDate,
                    RatePercent: c.WhtRate * 100m,            // stored as fraction (0.03) → 3.00
                    PaidAmount: c.IncomeAmount,
                    TaxAmount: c.WhtAmount,
                    IncomeType: c.IncomeDescription ?? c.IncomeTypeCode,
                    PayCondition: "1"))                       // หัก ณ ที่จ่าย (default; see spec G2)
                    .ToList()))
            .ToList();

        var bytes = WhtBatchFormat.BuildBytes(header, payees);
        var fileName = WhtBatchFormat.FileName(header);
        var recordCount = payees.Sum(p => (p.Incomes.Count + 2) / 3);  // ceil(incomes/3) = SEQ_NO rows
        return new WhtBatchFile(fileName, bytes, recordCount);
    }

    private static string BranchCode(string? payerBranch) =>
        string.IsNullOrWhiteSpace(payerBranch) ? "000000" : payerBranch;
}
