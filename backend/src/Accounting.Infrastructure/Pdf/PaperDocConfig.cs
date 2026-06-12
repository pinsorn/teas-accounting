namespace Accounting.Infrastructure.Pdf;

// Sprint 13j-PDF — C# mirror of frontend/lib/paper-doc-config.ts (PAPER_DOC titles
// + signRoles + validUntilLabel) and the §C7 watermark matrix. Plus the exact
// design-token hex from frontend/lib/design-tokens.css used by the renderer.

public enum PaperDocKind
{
    Quotation,
    SalesOrder,
    DeliveryOrder,
    TaxInvoice,
    Receipt,
    CreditNote,
    DebitNote,
    BillingNote,
}

public sealed record PaperDocConfig(
    string DocType, string DocTypeEn, string SignLeft, string SignRight, string? ValidUntilLabel = null);

public static class PaperDoc
{
    // Mirror of PAPER_DOC.
    public static readonly IReadOnlyDictionary<PaperDocKind, PaperDocConfig> Config =
        new Dictionary<PaperDocKind, PaperDocConfig>
        {
            [PaperDocKind.Quotation]    = new("ใบเสนอราคา", "QUOTATION", "ผู้เสนอราคา", "ผู้รับใบเสนอราคา", "ยืนราคาถึง"),
            [PaperDocKind.SalesOrder]   = new("ใบสั่งขาย", "SALES ORDER", "ผู้ขาย", "ผู้สั่งซื้อ"),
            [PaperDocKind.DeliveryOrder] = new("ใบส่งของ", "DELIVERY ORDER", "ผู้ส่งของ", "ผู้รับของ"),
            [PaperDocKind.TaxInvoice]   = new("ใบกำกับภาษี", "TAX INVOICE", "ผู้ออกใบกำกับ", "ผู้ซื้อ", "ครบกำหนดชำระ"),
            [PaperDocKind.Receipt]      = new("ใบเสร็จรับเงิน", "RECEIPT", "ผู้รับเงิน", "ผู้จ่ายเงิน"),
            [PaperDocKind.CreditNote]   = new("ใบลดหนี้", "CREDIT NOTE", "ผู้ออกใบลดหนี้", "ผู้ซื้อ"),
            [PaperDocKind.DebitNote]    = new("ใบเพิ่มหนี้", "DEBIT NOTE", "ผู้ออกใบเพิ่มหนี้", "ผู้ซื้อ"),
            // D4 rename (Ham 2026-06-12): EN label = INVOICE, matching the FE paper-doc-config mirror.
            [PaperDocKind.BillingNote]  = new("ใบแจ้งหนี้", "INVOICE", "ผู้ออกใบแจ้งหนี้", "ผู้รับใบแจ้งหนี้", "ครบกำหนดชำระ"),
        };

    // VAT rate normalizer → percent for display. Sales-chain docs (Q/SO/DO/BN)
    // store the rate as a percent (7); fiscal docs (TI/CN/DN) store a fraction
    // (0.07). Both must print "7%" — never 700%. (Sprint 13j-PDF bug fix.)
    public static decimal VatPercent(decimal rate) => rate <= 1m ? rate * 100m : rate;

    private static readonly HashSet<string> Cancelled = new(StringComparer.Ordinal)
        { "Cancelled", "Voided", "Rejected" };

    // Mirror of paperWatermark(kind, status).
    public static PaperWatermark? Watermark(PaperDocKind kind, string status)
    {
        if (Cancelled.Contains(status)) return new("ยกเลิก", PaperWatermarkVariant.Danger);
        return kind switch
        {
            PaperDocKind.Quotation => null,
            PaperDocKind.SalesOrder => status is "Confirmed" or "Posted" or "Closed"
                ? new("ยืนยันแล้ว", PaperWatermarkVariant.Success) : null,
            PaperDocKind.DeliveryOrder => status == "Delivered"
                ? new("ส่งของแล้ว", PaperWatermarkVariant.Success) : null,
            PaperDocKind.TaxInvoice or PaperDocKind.Receipt
                or PaperDocKind.CreditNote or PaperDocKind.DebitNote => status == "Posted"
                ? new("ต้นฉบับ", PaperWatermarkVariant.Success) : null,
            PaperDocKind.BillingNote => status is "Posted" or "Issued" or "Settled"
                ? new("ออกแล้ว", PaperWatermarkVariant.Info) : null,
            _ => null,
        };
    }
}

// design-tokens.css hex (exact).
public static class PaperColors
{
    public const string Ink900 = "#1A1816";
    public const string Ink700 = "#34312D";
    public const string Ink600 = "#4D4943";
    public const string Ink500 = "#6B6660";
    public const string Ink400 = "#8A847A";
    public const string Ink200 = "#D7D1C7";
    public const string Ink100 = "#ECE7DF";
    public const string Ink50  = "#FAF8F5";
    public const string Peach700 = "#9E5C34";
    public const string Peach600 = "#C57543";
    public const string Peach400 = "#E8A87C";
    public const string Peach300 = "#ECB68F";
    public const string Peach200 = "#F2CDB0";
    public const string Peach100 = "#F8E3D0";
    public const string Peach50  = "#FBF1E8";
    public const string White = "#FFFFFF";

    // Watermark tints (rgba in paper.css → solid hex blend on white is unstable,
    // so use the same low-alpha colors via QuestPDF FontColor + opacity layer).
    public static string WatermarkHex(PaperWatermarkVariant v) => v switch
    {
        PaperWatermarkVariant.Success => "#4A7C59",
        PaperWatermarkVariant.Danger  => "#B5524A",
        PaperWatermarkVariant.Warning => "#C68A2E",
        PaperWatermarkVariant.Info    => "#5B7B9A",
        _ => "#4A7C59",
    };
}
