namespace Accounting.Domain.Enums;

/// <summary>Direction of a tax line — input (purchase) vs output (sale).</summary>
public enum TaxDirection
{
    Input,
    Output,
}

public enum TaxType
{
    /// <summary>Value Added Tax</summary>
    Vat,
    /// <summary>Withholding Tax (ภาษีหัก ณ ที่จ่าย)</summary>
    Wht,
    /// <summary>Specific Business Tax (ภาษีธุรกิจเฉพาะ)</summary>
    Sbt,
}
