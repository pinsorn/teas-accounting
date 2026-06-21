using Accounting.Application.Abstractions;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.TaxFilings;

/// <summary>
/// ภ.พ.30 (VAT return) RD-Prep "Format กลาง" batch-file exporter. Reuses
/// <see cref="ITaxFilingService.GeneratePnd30Async"/> for ALL figures (never recomputes VAT) so the
/// file always matches the 07.01 preview / the filled PDF, then maps them to the per-branch DETAIL
/// row(s) of <see cref="Pp30BatchFormat"/>. Read-only — never mutates a posted document. Tenant
/// isolation via GeneratePnd30Async + the EF global query filter (no raw company_id touched here).
///
/// TEAS files company-level (one HQ branch), so this emits exactly ONE detail row, BRANCH_NO from the
/// company profile's registered branch (HQ = 0). The RD-mandatory address (NUMBER = เลขที่,
/// POSTAL_CODE) comes from the same <c>CompanyProfile</c> the PDF filler uses; both are Mandatory on
/// the form, so a blank one fails LOUDLY (<c>pp30_batch.missing_address</c>) rather than shipping a
/// row RD Prep would reject — mirrors WhtBatchExportService's missing_tax_id guard.
/// </summary>
public sealed class Pp30BatchExportService(
    AccountingDbContext db, ITenantContext tenant, ITaxFilingService filing)
    : IPp30BatchExportService
{
    public async Task<WhtBatchFile> BuildAsync(int period, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // Single source of truth for the VAT figures (ม.82/3) — no GL recompute.
        var f = await filing.GeneratePnd30Async(period, TaxFilingMode.Preview, ct);

        // ข้อ1 ยอดขายในเดือนนี้ ต้องมีค่ามากกว่า 0 (validator: a normal filing must have sales).
        if (f.Lines.TotalSales <= 0m)
            throw new DomainException("pp30_batch.no_data",
                $"No sales in period {period}; ภ.พ.30 batch file requires ข้อ1 ยอดขาย > 0.");

        // เลขที่ (NUMBER) + รหัสไปรษณีย์ (POSTAL_CODE) are Mandatory on the ภ.พ.30 form. They live on the
        // company's registered-address profile (same source as BuildPnd30PdfAsync), NOT in the filing.
        var prof = await db.CompanyProfiles.AsNoTracking()
            .Where(p => p.CompanyId == tenant.CompanyId)
            .Select(p => new { p.RegHouseNo, p.RegisteredPostalCode, p.BranchCode })
            .FirstOrDefaultAsync(ct);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(prof?.RegHouseNo)) missing.Add("เลขที่ (registered house no.)");
        if (string.IsNullOrWhiteSpace(prof?.RegisteredPostalCode)) missing.Add("รหัสไปรษณีย์ (postal code)");
        if (missing.Count > 0)
            throw new DomainException("pp30_batch.missing_address",
                "Company registered address is incomplete; ภ.พ.30 requires: "
                + string.Join(", ", missing) + ". Complete the company profile first.");

        var branch = new Pp30BatchFormat.Branch(
            BranchNo:       prof!.BranchCode,            // HQ "00000" → 0 (Branch5 normalises)
            AddressNo:      prof.RegHouseNo!,
            PostalCode:     prof.RegisteredPostalCode!,
            SalesTotal:     f.Lines.TotalSales,                 // ข้อ1
            SalesZeroRated: f.Lines.SalesZeroRated.Amount,      // ข้อ2
            SalesExempt:    f.Lines.SalesExempt.Amount,         // ข้อ3
            OutputVat:      f.Lines.OutputVatTotal,             // ข้อ5
            PurchaseTotal:  f.Lines.PurchaseTaxable.Amount,     // ข้อ6
            InputVat:       f.Lines.InputVatTotal);             // ข้อ7
        // ข้อ4 (taxable) และ ข้อ8/9 (net) ถูก derive ในตัว builder จากค่าที่ปัดแล้ว เพื่อให้ผ่าน identity checks.

        var header = new Pp30BatchFormat.Header(f.Company.TaxId, period);
        var branches = new[] { branch };

        var bytes = Pp30BatchFormat.BuildBytes(header, branches);
        var fileName = Pp30BatchFormat.FileName(header);
        return new WhtBatchFile(fileName, bytes, branches.Length);
    }
}
