using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Audit;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Master;

// Sprint 13d P6. Soft fields only are mutable here; hard fields are changed
// out-of-band (ภ.พ.09 + Phase-2 2-person flow) — the /hard endpoint 501s.
// Sprint 13h P10 — UpdateLogoAsync added: stores the logo via the polymorphic
// attachment table (parent_type=COMPANY_PROFILE) and rewrites LogoUrl.
public sealed class CompanyProfileService(
    AccountingDbContext db, ITenantContext tenant, IAttachmentService attachments,
    IActivityRecorder activity)
    : ICompanyProfileService
{
    public async Task<CompanyProfileDto?> GetAsync(CancellationToken ct) =>
        await db.CompanyProfiles.AsNoTracking()
            .Where(p => p.CompanyId == tenant.CompanyId)
            .Select(p => new CompanyProfileDto(
                p.CompanyId,
                p.LegalName, p.TaxId, p.RegistrationNumber,
                p.RegisteredAddressLine1, p.RegisteredAddressLine2,
                p.RegBuilding, p.RegRoomNo, p.RegFloor, p.RegVillage,
                p.RegHouseNo, p.RegMoo, p.RegSoi, p.RegStreet,
                p.RegisteredSubdistrict, p.RegisteredDistrict,
                p.RegisteredProvince, p.RegisteredPostalCode,
                p.VatRegistrationDate, p.BranchCode,
                p.TradeName, p.LogoUrl, p.Phone, p.Email, p.Website,
                p.ContactName, p.BankName, p.BankAccountNo, p.BankAccountName))
            .FirstOrDefaultAsync(ct);

    public async Task UpdateSoftAsync(UpdateCompanyProfileSoftRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var e = await db.CompanyProfiles
            .FirstOrDefaultAsync(p => p.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException(
                "company_profile.not_found",
                "Company profile not found for the current tenant.");

        // SOFT fields only — hard fields are intentionally untouched here.
        e.TradeName = req.TradeName;
        e.LogoUrl = req.LogoUrl;
        e.Phone = req.Phone;
        e.Email = req.Email;
        e.Website = req.Website;
        e.ContactName = req.ContactName;
        e.BankName = req.BankName;
        e.BankAccountNo = req.BankAccountNo;
        e.BankAccountName = req.BankAccountName;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedByUserId = tenant.UserId;

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateRegisteredAddressAsync(UpdateRegisteredAddressRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        if (string.IsNullOrWhiteSpace(req.Province) || string.IsNullOrWhiteSpace(req.PostalCode))
            throw new DomainException("company_profile.address_incomplete", "Province and postal code are required.");

        var e = await db.CompanyProfiles.FirstOrDefaultAsync(p => p.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company_profile.not_found", "Company profile not found.");

        e.RegBuilding = req.Building; e.RegRoomNo = req.RoomNo; e.RegFloor = req.Floor; e.RegVillage = req.Village;
        e.RegHouseNo = req.HouseNo; e.RegMoo = req.Moo; e.RegSoi = req.Soi; e.RegStreet = req.Street;
        e.RegisteredSubdistrict = req.Subdistrict; e.RegisteredDistrict = req.District;
        e.RegisteredProvince = req.Province; e.RegisteredPostalCode = req.PostalCode;
        // Keep the legacy free-text Line1 in sync — Tax Invoice / e-Tax render from it.
        e.RegisteredAddressLine1 = ComposeLine1(req);
        e.RegisteredAddressLine2 = null;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedByUserId = tenant.UserId;

        // §4.8 — a HARD legal-identity change must be audited (admin confirmed the DBD/ภ.พ.09 filing).
        activity.Record("CompanyProfile", e.CompanyId, null, e.CompanyId,
            "RegisteredAddressChanged", note: "admin-confirmed DBD (บอจ.1/บอจ.4) + ภ.พ.09 filing", module: "master");
        await db.SaveChangesAsync(ct);
    }

    private static string ComposeLine1(UpdateRegisteredAddressRequest r)
    {
        var parts = new[]
        {
            r.HouseNo,
            string.IsNullOrWhiteSpace(r.Building) ? null : $"อาคาร{r.Building}",
            string.IsNullOrWhiteSpace(r.RoomNo) ? null : $"ห้อง {r.RoomNo}",
            string.IsNullOrWhiteSpace(r.Floor) ? null : $"ชั้น {r.Floor}",
            string.IsNullOrWhiteSpace(r.Village) ? null : $"หมู่บ้าน{r.Village}",
            string.IsNullOrWhiteSpace(r.Moo) ? null : $"หมู่ {r.Moo}",
            string.IsNullOrWhiteSpace(r.Soi) ? null : $"ซ.{r.Soi}",
            string.IsNullOrWhiteSpace(r.Street) ? null : $"ถ.{r.Street}",
        }.Where(s => !string.IsNullOrWhiteSpace(s));
        var line = string.Join(" ", parts).Trim();
        return line.Length == 0 ? "-" : line;
    }

    public async Task<string> UpdateLogoAsync(
        string fileName, string mimeType, long sizeBytes, Stream content,
        CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // P10 — accept the common image mimetypes only.
        var allowedImage = new[] { "image/png", "image/jpeg", "image/jpg", "image/svg+xml", "image/webp" };
        if (!allowedImage.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            throw new DomainException("company_profile.logo_bad_mime",
                $"Logo MIME type '{mimeType}' is not allowed (png/jpeg/svg/webp only).");
        const long maxLogoBytes = 1L * 1024 * 1024;     // 1 MB
        if (sizeBytes > maxLogoBytes)
            throw new DomainException("company_profile.logo_too_large",
                "Logo file exceeds the 1 MB limit.");

        var profile = await db.CompanyProfiles
            .FirstOrDefaultAsync(p => p.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company_profile.not_found",
                "Company profile not found for the current tenant.");

        // Reuse the polymorphic attachment table — same lifecycle + audit shape.
        var uploaded = await attachments.UploadAsync(
            parentType: "COMPANY_PROFILE",
            parentId: profile.CompanyId,
            category: "OTHER",
            description: "Company logo",
            fileName: fileName,
            mimeType: mimeType,
            sizeBytes: sizeBytes,
            content: content,
            ct: ct);

        var url = $"/attachments/{uploaded.AttachmentId}/download";
        profile.LogoUrl = url;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        profile.UpdatedByUserId = tenant.UserId;
        await db.SaveChangesAsync(ct);
        return url;
    }
}
