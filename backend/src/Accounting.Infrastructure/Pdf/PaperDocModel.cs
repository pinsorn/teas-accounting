namespace Accounting.Infrastructure.Pdf;

// Sprint 13j-PDF — C# mirror of the LOCKED PaperDocumentProps (§C4,
// frontend/components/paper/types.ts). Field names match 1:1 so the QuestPDF
// output equals the on-screen PaperDocument preview. Pure data — no entity refs;
// per-doctype mappers build this from the posted snapshot / company profile.

public sealed record PaperSeller(
    string Name,
    string TaxId,
    string BranchCode,
    string Address,
    byte[]? Logo = null,
    string? Phone = null,
    string? Email = null);

public sealed record PaperCustomer(
    string Name,
    string? TaxId = null,
    string? BranchCode = null,
    string? Address = null,
    string? Contact = null,
    string? Phone = null);

public sealed record PaperLine(
    string Description,
    string? DescriptionSub,
    decimal? Quantity,
    string? Unit,
    decimal? UnitPrice,
    decimal? DiscountPercent,
    decimal Amount);

public sealed record PaperSummary(
    decimal Subtotal,
    decimal? Discount,
    decimal? BeforeVat,
    decimal Vat,
    decimal Total,
    decimal? VatRate, // percent, e.g. 7
    // Non-VAT mode (ม.86 — บริษัทไม่จด VAT): when false the foot prints a single
    // "ยอดรวม / Total" row only (no Subtotal/Before-VAT/VAT). Sourced from
    // ICompanyTaxConfigService.VatMode by the per-doctype mapper. Defaults true so positional
    // callers + the VAT-registered path are unaffected.
    bool ShowVat = true,
    // Sprint 13j-PURCH Phase C — Payment Voucher only: when set, the foot prints a
    // "หัก ณ ที่จ่าย · WHT" row above the grand total, and Total carries the
    // WHT-deducted net ("จ่ายสุทธิ"). null for every other doctype (additive, last
    // positional → existing callers unaffected).
    decimal? Wht = null);

public enum PaperWatermarkVariant { Success, Danger, Warning, Info }

public sealed record PaperWatermark(string Text, PaperWatermarkVariant Variant);

// Left/Right = the standard two-box signature strip. Middle is optional and only
// set by the Payment Voucher (Phase C) for a three-box strip
// (ผู้จัดทำ / ผู้อนุมัติ / ผู้รับเงิน); null → the renderer keeps the two-box layout.
public sealed record PaperSignRoles(string Left, string Right, string? Middle = null);

public sealed record PaperDocModel(
    string DocType,        // "ใบกำกับภาษี"
    string DocTypeEn,      // "TAX INVOICE"
    string DocNo,
    DateOnly IssueDate,
    PaperSeller Seller,
    PaperCustomer Customer,
    IReadOnlyList<PaperLine> Items,
    PaperSummary Summary,
    PaperSignRoles SignRoles,
    DateOnly? ValidUntil = null,
    string? ValidUntilLabel = null,
    string? AmountWords = null,
    string? Notes = null,
    PaperWatermark? Watermark = null);
