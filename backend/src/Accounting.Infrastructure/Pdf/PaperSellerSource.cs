using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Pdf;

// Sprint 13j-PDF — builds the PaperDocument seller block from the tenant company
// (HQ branch). Used by every doctype whose seller is the company itself
// (Q/SO/DO/RC/CN/DN/BN); TaxInvoice instead uses its own posted snapshot.
public static class PaperSellerSource
{
    // Mirror frontend/lib/paper-doc-config.ts companyToSeller EXACTLY (the FE
    // detail pages build the seller from CompanyProfile, so the PDF must too):
    // name = tradeName || legalName; taxId RAW (NOT formatted, unlike the customer
    // block); branchCode || "00000"; address = registered lines joined by space;
    // logoUrl/phone/email passed through. Falls back to the Company row if the
    // tenant has no profile yet.
    public static async Task<PaperSeller> FromCompanyProfileAsync(
        AccountingDbContext db, int companyId, CancellationToken ct)
    {
        var p = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId, ct);
        if (p is null) return await FromCompanyAsync(db, companyId, ct);

        var address = ComposeRegisteredAddress(p);

        return new PaperSeller(
            string.IsNullOrEmpty(p.TradeName) ? p.LegalName : p.TradeName!,
            p.TaxId,
            string.IsNullOrEmpty(p.BranchCode) ? "00000" : p.BranchCode,
            address,
            Logo: null,           // TODO: resolve p.LogoUrl attachment → bytes; fallback mascot for now
            Phone: p.Phone,
            Email: p.Email);
    }

    /// <summary>Registered (DBD) address joined to one line; "" when the profile is null
    /// or has no registered-address fields. Also used by the TI post-snapshot (ม.86/4 #2).</summary>
    public static string ComposeRegisteredAddress(Domain.Entities.Master.CompanyProfile? p) =>
        p is null ? string.Empty : string.Join(" ", new[]
        {
            p.RegisteredAddressLine1, p.RegisteredAddressLine2, p.RegisteredSubdistrict,
            p.RegisteredDistrict, p.RegisteredProvince, p.RegisteredPostalCode,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public static async Task<PaperSeller> FromCompanyAsync(
        AccountingDbContext db, int companyId, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking().Include(x => x.Branches)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId, ct);
        if (c is null) return new PaperSeller("—", string.Empty, "00000", string.Empty);
        var b = c.Branches.FirstOrDefault(x => x.IsHeadOffice) ?? c.Branches.FirstOrDefault();
        return new PaperSeller(c.NameTh, c.TaxId, b?.BranchCode ?? "00000", c.AddressTh ?? string.Empty);
    }
}
