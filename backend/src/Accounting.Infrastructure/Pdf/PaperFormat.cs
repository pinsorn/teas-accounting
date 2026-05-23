namespace Accounting.Infrastructure.Pdf;

// Sprint 13j-PDF — formatting helpers that MUST match the FE (frontend/lib/utils)
// so the PDF text is identical to the on-screen PaperDocument preview.
public static class PaperFormat
{
    // Mirror formatTaxId: "0-1055-56123-45-0". Non-13-digit → returned as-is;
    // null/empty → null (the customer block then omits the line, like the FE).
    public static string? TaxId(string? taxId)
    {
        if (string.IsNullOrEmpty(taxId)) return null;
        if (taxId.Length != 13) return taxId;
        return $"{taxId[0]}-{taxId.Substring(1, 4)}-{taxId.Substring(5, 5)}-{taxId.Substring(10, 2)}-{taxId[12]}";
    }
}
