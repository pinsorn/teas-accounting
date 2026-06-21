using System.Text.RegularExpressions;
using Accounting.Application.Abstractions;
using Accounting.Application.Attachments;
using Accounting.Application.Audit;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.ValueObjects;
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
                p.ContactName, p.BankName, p.BankAccountNo, p.BankAccountName,
                p.SsoEmployerAccountNo))
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
        e.SsoEmployerAccountNo = req.SsoEmployerAccountNo;
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
        // Keep the legacy free-text Line1 in sync — Tax Invoice / e-Tax render from it. Shared
        // composer so a ภ.พ.09 edit formats identically to the founding (company-create) address.
        e.RegisteredAddressLine1 = ThaiRegisteredAddress.ComposeLine1(
            req.HouseNo, req.Building, req.RoomNo, req.Floor,
            req.Village, req.Moo, req.Soi, req.Street);
        e.RegisteredAddressLine2 = null;
        e.UpdatedAt = DateTimeOffset.UtcNow;
        e.UpdatedByUserId = tenant.UserId;

        // §4.8 — a HARD legal-identity change must be audited (admin confirmed the DBD/ภ.พ.09 filing).
        activity.Record("CompanyProfile", e.CompanyId, null, e.CompanyId,
            "RegisteredAddressChanged", note: "admin-confirmed DBD (บอจ.1/บอจ.4) + ภ.พ.09 filing", module: "master");
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateCompanyInfoAsync(UpdateCompanyInfoRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // --- Validate founding identity + tax config (i18n keys resolved on the FE). ---
        var legalName = (req.LegalName ?? "").Trim();
        if (legalName.Length == 0)
            throw new DomainException("company_info.name_required", "Legal name is required.");
        if (!ThaiTaxId.TryParse(req.TaxId, out _))
            throw new DomainException("company_info.tax_id_invalid", "Tax ID must be 13 digits with a valid checksum.");
        var branchCode = (req.BranchCode ?? "").Trim();
        if (!Regex.IsMatch(branchCode, "^[0-9]{5}$"))   // ม.86/4 — 00000 = head office, 00001+ = branch
            throw new DomainException("company_info.branch_invalid", "Branch code must be 5 digits (00000 = head office).");
        if (string.IsNullOrWhiteSpace(req.Province) || string.IsNullOrWhiteSpace(req.PostalCode))
            throw new DomainException("company_info.address_incomplete", "Province and postal code are required.");
        if (req.VatRate < 0m || req.VatRate > 1m)
            throw new DomainException("company_info.vat_rate_invalid", "VAT rate must be between 0 and 1.");
        var mode = (req.Pnd30SubmissionMode ?? "manual").Trim();
        if (mode != "manual" && mode != "auto")
            throw new DomainException("company_info.pnd30_invalid", "ภ.พ.30 submission mode must be 'manual' or 'auto'.");

        var taxId = req.TaxId.Trim();
        // Dup-TaxId guard (companies.tax_id is indexed) — exclude self.
        if (await db.Companies.IgnoreQueryFilters()
                .AnyAsync(c => c.TaxId == taxId && c.CompanyId != tenant.CompanyId, ct))
            throw new DomainException("company.duplicate", $"Another company already uses Tax ID '{taxId}'.");

        var company = await db.Companies.FirstOrDefaultAsync(c => c.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found for the current tenant.");
        var profile = await db.CompanyProfiles.FirstOrDefaultAsync(p => p.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company_profile.not_found", "Company profile not found.");

        // Capture deltas BEFORE mutating (audit).
        var vatChanged = company.VatRegistered != req.VatRegistered || company.VatRate != req.VatRate
            || company.Pnd30SubmissionMode != mode;
        var vatNote = $"vat_registered {company.VatRegistered}->{req.VatRegistered}; "
            + $"vat_rate {company.VatRate}->{req.VatRate}; pnd30_submission_mode {company.Pnd30SubmissionMode}->{mode}";
        var idNote = $"legal_name '{profile.LegalName}'->'{legalName}'; tax_id {profile.TaxId}->{taxId}; "
            + $"branch_code {profile.BranchCode}->{branchCode}";

        var line1 = ThaiRegisteredAddress.ComposeLine1(
            req.HouseNo, req.Building, req.RoomNo, req.Floor, req.Village, req.Moo, req.Soi, req.Street);

        // --- master.companies — VAT/tax config source of truth (§4.6) + master record. ---
        company.NameTh = legalName; company.NameEn = req.NameEn; company.TaxId = taxId;
        company.LegalEntityType = req.LegalEntityType;
        company.VatRegistered = req.VatRegistered; company.VatRegisterDate = req.VatRegisterDate;
        company.VatRate = req.VatRate; company.Pnd30SubmissionMode = mode;
        company.AddressTh = line1; company.SubDistrict = req.Subdistrict; company.District = req.District;
        company.Province = req.Province; company.PostalCode = req.PostalCode;

        // --- company_profiles — documents render the seller from here (snapshotted at post). ---
        profile.LegalName = legalName; profile.TaxId = taxId;
        profile.RegistrationNumber = string.IsNullOrWhiteSpace(req.RegistrationNumber) ? taxId : req.RegistrationNumber.Trim();
        profile.BranchCode = branchCode; profile.VatRegistrationDate = req.VatRegisterDate;
        profile.RegBuilding = req.Building; profile.RegRoomNo = req.RoomNo; profile.RegFloor = req.Floor;
        profile.RegVillage = req.Village; profile.RegHouseNo = req.HouseNo; profile.RegMoo = req.Moo;
        profile.RegSoi = req.Soi; profile.RegStreet = req.Street;
        profile.RegisteredSubdistrict = req.Subdistrict; profile.RegisteredDistrict = req.District;
        profile.RegisteredProvince = req.Province; profile.RegisteredPostalCode = req.PostalCode;
        profile.RegisteredAddressLine1 = line1; profile.RegisteredAddressLine2 = null;
        profile.UpdatedAt = DateTimeOffset.UtcNow; profile.UpdatedByUserId = tenant.UserId;

        // §4.6 — VAT/tax-config change audited as tax_config_change (mirrors CompanyService.UpdateAsync).
        if (vatChanged)
            activity.Record("company", company.CompanyId, null, company.CompanyId, "tax_config_change",
                note: vatNote, module: "master");
        // §4.8 — founding-identity change audited.
        activity.Record("CompanyProfile", company.CompanyId, null, company.CompanyId, "CompanyInfoChanged",
            note: idNote, module: "master");

        await db.SaveChangesAsync(ct);
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
