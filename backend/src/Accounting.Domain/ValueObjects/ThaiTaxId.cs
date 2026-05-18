namespace Accounting.Domain.ValueObjects;

/// <summary>
/// Thai 13-digit Tax ID / Citizen ID with checksum validation.
/// Validates the last digit as the modulo-11 checksum of the first 12 digits.
/// </summary>
public readonly record struct ThaiTaxId
{
    public string Value { get; }

    private ThaiTaxId(string value) => Value = value;

    public static ThaiTaxId Parse(string raw)
    {
        if (!TryParse(raw, out var id))
            throw new ArgumentException($"Invalid Thai Tax ID: {raw}", nameof(raw));
        return id;
    }

    public static bool TryParse(string? raw, out ThaiTaxId result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Strip non-digits (the source may include hyphens)
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length != 13) return false;

        if (!IsValidChecksum(digits)) return false;

        result = new ThaiTaxId(digits);
        return true;
    }

    /// <summary>
    /// Thai Tax ID / Citizen ID checksum algorithm:
    ///   sum = Σ (digit[i] × (13 - i))  for i in 0..11
    ///   expected_check = (11 - sum mod 11) mod 10
    ///   valid iff digit[12] == expected_check
    /// </summary>
    private static bool IsValidChecksum(string thirteenDigits)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (thirteenDigits[i] - '0') * (13 - i);

        var check = (11 - (sum % 11)) % 10;
        return (thirteenDigits[12] - '0') == check;
    }

    public override string ToString() => Value;

    /// <summary>Formats as `X-XXXX-XXXXX-XX-X` for display.</summary>
    public string ToDisplay() =>
        $"{Value[0]}-{Value[1..5]}-{Value[5..10]}-{Value[10..12]}-{Value[12]}";

    public static implicit operator string(ThaiTaxId id) => id.Value;
}
