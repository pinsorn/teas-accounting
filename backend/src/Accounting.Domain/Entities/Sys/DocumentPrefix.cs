namespace Accounting.Domain.Entities.Sys;

/// <summary>
/// Registry of allowed document prefixes (TI, RC, CN, …). Documents may only be issued
/// against a registered prefix — enforced by FK from sys.number_sequences.
/// </summary>
public class DocumentPrefix
{
    public int PrefixId { get; set; }
    public required string PrefixCode { get; set; }
    public required string DocumentType { get; set; }
    public required string DescriptionTh { get; set; }
    public string? DescriptionEn { get; set; }
    public bool RequiresEtax { get; set; }
    public bool IsFiscalDoc { get; set; }
    public bool IsExpense { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
