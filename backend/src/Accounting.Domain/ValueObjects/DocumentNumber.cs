using System.Text.RegularExpressions;

namespace Accounting.Domain.ValueObjects;

/// <summary>
/// Document number in format `MM-YYYY-PREFIX-NNNN` or `MM-YYYY-PREFIX-CATEGORY-NNNN`.
/// Examples: "05-2026-TI-0001", "05-2026-PV-RENT-0001"
/// </summary>
public readonly record struct DocumentNumber
{
    private static readonly Regex Pattern =
        new(@"^(?<month>0[1-9]|1[0-2])-(?<year>\d{4})-(?<prefix>[A-Z]{2,5})(?:-(?<sub>[A-Z]{2,10}))?-(?<seq>\d{4,6})$",
            RegexOptions.Compiled);

    public string Value { get; }
    public int Year { get; }
    public int Month { get; }
    public string Prefix { get; }
    public string? SubPrefix { get; }
    public int Sequence { get; }

    private DocumentNumber(string value, int year, int month, string prefix, string? sub, int seq)
    {
        Value = value;
        Year = year;
        Month = month;
        Prefix = prefix;
        SubPrefix = sub;
        Sequence = seq;
    }

    public static DocumentNumber Parse(string raw)
    {
        if (!TryParse(raw, out var num))
            throw new ArgumentException($"Invalid document number: {raw}", nameof(raw));
        return num;
    }

    public static bool TryParse(string? raw, out DocumentNumber result)
    {
        result = default;
        if (raw is null) return false;
        var m = Pattern.Match(raw);
        if (!m.Success) return false;

        result = new DocumentNumber(
            value: raw,
            year:  int.Parse(m.Groups["year"].Value),
            month: int.Parse(m.Groups["month"].Value),
            prefix: m.Groups["prefix"].Value,
            sub: m.Groups["sub"].Success ? m.Groups["sub"].Value : null,
            seq: int.Parse(m.Groups["seq"].Value));
        return true;
    }

    public static DocumentNumber Build(int year, int month, string prefix, string? subPrefix, int sequence, int padding = 4)
    {
        var seqStr = sequence.ToString().PadLeft(padding, '0');
        var sub = string.IsNullOrEmpty(subPrefix) ? "" : $"-{subPrefix}";
        var value = $"{month:D2}-{year:D4}-{prefix}{sub}-{seqStr}";
        return new DocumentNumber(value, year, month, prefix, subPrefix, sequence);
    }

    public override string ToString() => Value;
    public static implicit operator string(DocumentNumber d) => d.Value;
}
