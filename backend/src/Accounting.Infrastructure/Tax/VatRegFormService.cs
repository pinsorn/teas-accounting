using Accounting.Application.Abstractions;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Tax;

/// <summary>
/// ภ.พ.01/ภ.พ.09 v1 prefill — identity header from CompanyProfile (same source/fallbacks as the
/// ภ.ง.ด.50 header: profile values first, Companies row for TaxId/name when no profile exists).
/// Print-and-sign: no attestation/refusal machinery (these are applications, not computed returns).
/// </summary>
public sealed class VatRegFormService(
    AccountingDbContext db,
    ITenantContext tenant) : IVatRegFormService
{
    public async Task<byte[]> BuildPp01Async(CancellationToken ct) =>
        Pp01FormFiller.Fill(await IdentityAsync(ct));

    public async Task<byte[]> BuildPp09Async(CancellationToken ct) =>
        Pp09FormFiller.Fill(await IdentityAsync(ct));

    private async Task<VatRegIdentity> IdentityAsync(CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");
        var prof = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);

        return new VatRegIdentity(
            TaxId: prof?.TaxId ?? c.TaxId, LegalName: prof?.LegalName ?? c.NameTh,
            Building: prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor,
            Village: prof?.RegVillage, HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo,
            Soi: prof?.RegSoi, Road: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province: prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            Email: prof?.Email, Website: prof?.Website);
    }
}
