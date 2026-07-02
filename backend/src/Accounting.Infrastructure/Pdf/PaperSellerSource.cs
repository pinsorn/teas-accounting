using Accounting.Application.Abstractions;
using Accounting.Application.Pdf;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
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
        AccountingDbContext db, int companyId, CancellationToken ct,
        IFileStorageService? storage = null)
    {
        var p = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId, ct);
        if (p is null) return await FromCompanyAsync(db, companyId, ct);

        var address = ComposeRegisteredAddress(p);

        // Sprint 13k — the company's uploaded logo is embedded in the document header;
        // PaperDocumentPdf falls back to the bundled TEAS mark when both are null. The
        // logo is decorative: a read (or SVG-decode) failure never blocks a legal
        // document. A raster logo → Logo bytes; an SVG logo → LogoSvg markup, rendered
        // natively as vector. Prefer whichever format was actually uploaded.
        byte[]? logo = null;
        string? logoSvg = null;
        if (storage is not null)
            (logo, logoSvg) = await ResolveLogoAsync(db, storage, companyId, ct);

        return new PaperSeller(
            string.IsNullOrEmpty(p.TradeName) ? p.LegalName : p.TradeName!,
            p.TaxId,
            string.IsNullOrEmpty(p.BranchCode) ? "00000" : p.BranchCode,
            address,
            Logo: logo,
            LogoSvg: logoSvg,
            Phone: p.Phone,
            Email: p.Email);
    }

    private static readonly string[] RasterLogoMimes =
        { "image/png", "image/jpeg", "image/jpg", "image/webp" };
    private const string SvgMime = "image/svg+xml";

    /// <summary>Latest non-deleted COMPANY_PROFILE logo attachment for the tenant.
    /// A raster logo (PNG/JPEG/WebP) is returned as bytes in <c>Logo</c>; an SVG logo is
    /// returned as validated UTF-8 markup in <c>LogoSvg</c> (QuestPDF renders it natively
    /// as vector). Both null when there is none, when the MIME is neither, or on any
    /// read/decode error — the caller then falls back to the bundled mark. The SVG is
    /// validated HERE (SvgImage.FromText) because QuestPDF's .Svg() defers decoding to
    /// GeneratePdf(); a malformed file would otherwise throw there — outside this catch —
    /// and blow up a whole legal document. The logo is decorative and must never do that.</summary>
    private static async Task<(byte[]? Logo, string? LogoSvg)> ResolveLogoAsync(
        AccountingDbContext db, IFileStorageService storage, int companyId, CancellationToken ct)
    {
        var att = await db.Set<Attachment>().AsNoTracking()
            .Where(a => a.ParentType == AttachmentParentType.CompanyProfile
                        && a.ParentId == companyId
                        && a.DeletedAt == null)
            .OrderByDescending(a => a.UploadedAt)
            .Select(a => new { a.StoragePath, a.MimeType })
            .FirstOrDefaultAsync(ct);
        if (att is null) return (null, null);
        var mime = att.MimeType.ToLowerInvariant();
        var isSvg = mime == SvgMime;
        if (!isSvg && !RasterLogoMimes.Contains(mime)) return (null, null);
        try
        {
            await using var s = await storage.OpenReadAsync(att.StoragePath, ct);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms, ct);
            if (ms.Length == 0) return (null, null);
            if (!isSvg) return (ms.ToArray(), null);

            // Strip a leading UTF-8 BOM (common from exporters) and prove the markup is
            // decodable now; FromText throws on bad input, so a broken SVG is caught here
            // and falls back to the mascot instead of throwing later inside GeneratePdf().
            var svg = System.Text.Encoding.UTF8.GetString(ms.ToArray()).TrimStart((char)0xFEFF);
            _ = QuestPDF.Infrastructure.SvgImage.FromText(svg);
            return (null, svg);
        }
        catch
        {
            return (null, null); // logo is decorative — never fail a legal document on it
        }
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
