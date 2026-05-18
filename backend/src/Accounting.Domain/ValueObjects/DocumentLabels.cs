using Accounting.Domain.Enums;

namespace Accounting.Domain.ValueObjects;

/// <summary>
/// Sprint 8.5 — pure resolver for the legally-sensitive document header / legal-basis
/// strings that depend on the company's VAT-registration mode.
///
/// ม.86: only a VAT-registered business may issue a document headed "ใบกำกับภาษี".
/// A non-VAT company must use a neutral commercial term (config-driven). CN/DN cite
/// ม.86/10 / ม.86/9 under VAT mode, but ม.82/9 (price adjustment) under non-VAT mode.
///
/// This is a pure function (no I/O) so the compliance branching is unit-tested
/// directly — the PDF builders just call it.
/// </summary>
public static class DocumentLabels
{
    /// <summary>Header for a Tax Invoice. Under non-VAT mode the legal "ใบกำกับภาษี"
    /// term is replaced by the configured neutral label.</summary>
    public static (string Th, string En) TaxInvoiceHeader(
        bool vatMode, string nonVatDocLabelTh, string nonVatDocLabelEn) =>
        vatMode
            ? ("ใบกำกับภาษี", "TAX INVOICE")
            : (nonVatDocLabelTh, nonVatDocLabelEn);

    /// <summary>True when the VAT line/column must be rendered. Non-VAT companies
    /// have no output VAT to show — a single total only.</summary>
    public static bool ShowVatBreakdown(bool vatMode) => vatMode;

    /// <summary>Title + parenthetical legal basis for a Credit / Debit Note.
    /// VAT mode: ม.86/10 (CN) / ม.86/9 (DN). Non-VAT: ม.82/9 (price adjustment).</summary>
    public static (string TitleTh, string TitleEn, string LegalRef) AdjustmentNote(
        TaxAdjustmentNoteType type, bool vatMode)
    {
        var legalRef = vatMode
            ? (type == TaxAdjustmentNoteType.Credit ? "ม.86/10" : "ม.86/9")
            : "ม.82/9";
        return type == TaxAdjustmentNoteType.Credit
            ? ("ใบลดหนี้", "CREDIT NOTE", legalRef)
            : ("ใบเพิ่มหนี้", "DEBIT NOTE", legalRef);
    }
}
