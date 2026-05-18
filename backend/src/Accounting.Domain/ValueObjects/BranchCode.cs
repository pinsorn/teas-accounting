namespace Accounting.Domain.ValueObjects;

/// <summary>
/// Thai 5-digit branch code as required by the Revenue Department.
/// "00000" = สำนักงานใหญ่ (HQ); "00001"+ = branches.
/// </summary>
public readonly record struct BranchCode
{
    public const string HeadOffice = "00000";

    public string Value { get; }

    private BranchCode(string value) => Value = value;

    public static BranchCode Parse(string raw)
    {
        if (!TryParse(raw, out var code))
            throw new ArgumentException($"Invalid branch code: {raw}", nameof(raw));
        return code;
    }

    public static bool TryParse(string? raw, out BranchCode result)
    {
        result = default;
        if (raw is null) return false;
        if (raw.Length != 5) return false;
        for (var i = 0; i < 5; i++)
            if (raw[i] < '0' || raw[i] > '9') return false;

        result = new BranchCode(raw);
        return true;
    }

    public bool IsHeadOffice => Value == HeadOffice;

    /// <summary>Display label per ม.86/4 #2: "สำนักงานใหญ่" or "สาขาที่ XXXXX".</summary>
    public string ToThaiLabel() => IsHeadOffice ? "สำนักงานใหญ่" : $"สาขาที่ {Value}";

    public override string ToString() => Value;
    public static implicit operator string(BranchCode c) => c.Value;
}
