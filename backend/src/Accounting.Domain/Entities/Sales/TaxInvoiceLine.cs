namespace Accounting.Domain.Entities.Sales;

public class TaxInvoiceLine
{
    public long TaxInvoiceId { get; set; }
    public long LineId       { get; set; }
    public int  LineNo       { get; set; }

    public long?  ProductId    { get; set; }
    public string? ProductCode { get; set; }
    public required string DescriptionTh { get; set; }

    public decimal Quantity        { get; set; }
    public int     UomId           { get; set; }
    public required string UomText { get; set; }

    public decimal UnitPrice       { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount  { get; set; }
    public decimal LineAmount      { get; set; }

    public int     TaxCodeId  { get; set; }
    public required string TaxCode { get; set; }
    public decimal TaxRate    { get; set; }
    public decimal TaxAmount  { get; set; }
    public decimal TotalAmount { get; set; }
}
