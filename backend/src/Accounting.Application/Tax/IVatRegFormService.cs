namespace Accounting.Application.Tax;

/// <summary>
/// ภ.พ.01 (VAT registration application) / ภ.พ.09 (change of VAT registration details) —
/// v1 PRINT-AND-SIGN prefill: TEAS fills ONLY the page-1 company-identity header
/// (name, 13-digit tax id, registered address, email/website) from CompanyProfile.
/// Every substantive answer (person type, change items, branch lists, dates, signatures)
/// stays blank for the filer/officer — these are applications, not computed returns, so
/// there is no refusal/attestation machinery: a blank box here just means "not filled yet".
/// Field maps: docs/RD-Forms/pp01/fieldmap/pp01_map.md · docs/RD-Forms/pp09/fieldmap/pp09_map.md.
/// </summary>
public interface IVatRegFormService
{
    /// <summary>ภ.พ.01 PDF with the identity header prefilled.</summary>
    Task<byte[]> BuildPp01Async(CancellationToken ct);

    /// <summary>ภ.พ.09 PDF with the identity header prefilled.</summary>
    Task<byte[]> BuildPp09Async(CancellationToken ct);
}
