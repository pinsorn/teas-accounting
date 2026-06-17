namespace Accounting.Application.Purchase;

/// <summary>
/// cont.76 — advisory document-completeness result, computed on READ for POSTED docs only
/// (drafts are never nagged). NON-BLOCKING: never gates post, never edits a posted doc.
/// <c>Missing</c> carries machine codes (e.g. MISSING_VI) the FE maps to Thai reason chips.
/// </summary>
public sealed record CompletenessView(bool IsComplete, IReadOnlyList<string> Missing)
{
    /// <summary>The "nothing to flag" result — used for drafts and for fully-complete posted docs.</summary>
    public static readonly CompletenessView Complete = new(true, System.Array.Empty<string>());

    public static CompletenessView From(IReadOnlyList<string> missing) =>
        missing.Count == 0 ? Complete : new CompletenessView(false, missing);
}

/// <summary>
/// cont.76 — สินค้า/บริการ line snapshot codes (UPPER_SNAKE, mirrors the sales
/// <c>ProductDtos.AllowedProductTypes</c> precedent + the line entity comments).
/// </summary>
public static class ProductTypeCodes
{
    public const string Default = "GOOD";

    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(System.StringComparer.Ordinal)
        { "GOOD", "SERVICE", "EXEMPT_GOOD", "EXEMPT_SERVICE" };

    /// <summary>
    /// Service-path normalisation: null/empty → "GOOD"; a valid code passes through;
    /// an explicitly-invalid non-null value is rejected (so existing call-sites that omit
    /// it keep working, while a bad explicit value is caught). Throws via <paramref name="onInvalid"/>.
    /// </summary>
    public static string Normalize(string? raw, System.Action<string> onInvalid)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Default;
        var code = raw.Trim();
        if (!Allowed.Contains(code)) onInvalid(code);
        return code;
    }

    /// <summary>
    /// Enum → UPPER_SNAKE code (mirror of the Product entity value-converter in
    /// ProductConfiguration). Used to surface a line's product taxonomy on read DTOs.
    /// </summary>
    public static string ToCode(Domain.Enums.ProductType t) => t switch
    {
        Domain.Enums.ProductType.Service       => "SERVICE",
        Domain.Enums.ProductType.ExemptGood    => "EXEMPT_GOOD",
        Domain.Enums.ProductType.ExemptService => "EXEMPT_SERVICE",
        _                                      => "GOOD",
    };

    /// <summary>True for the ม.81 VAT-exempt product taxonomies.</summary>
    public static bool IsExempt(string code) =>
        code is "EXEMPT_GOOD" or "EXEMPT_SERVICE";
}
