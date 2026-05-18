using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using FluentAssertions;

namespace Accounting.Domain.Tests;

/// <summary>
/// Sprint 8.5 — the legally-sensitive label branching. ม.86: only VAT-registered
/// businesses may head a doc "ใบกำกับภาษี"; non-VAT must use a neutral term and
/// cite ม.82/9 on adjustment notes (not ม.86/10 / ม.86/9). This is the
/// authoritative compliance assertion — the PDF builders just call this helper.
/// </summary>
public class DocumentLabelsTests
{
    [Fact]
    public void Vat_mode_uses_the_legal_tax_invoice_header()
    {
        var (th, en) = DocumentLabels.TaxInvoiceHeader(true, "ใบส่งของ", "Delivery Order");
        th.Should().Be("ใบกำกับภาษี");
        en.Should().Be("TAX INVOICE");
    }

    [Fact]
    public void Non_vat_mode_must_not_use_the_tax_invoice_term()
    {
        var (th, en) = DocumentLabels.TaxInvoiceHeader(false, "ใบส่งของ", "Delivery Order");
        th.Should().Be("ใบส่งของ");
        en.Should().Be("Delivery Order");
        th.Should().NotContain("ใบกำกับภาษี", "ม.86 forbids non-VAT companies using that term");
    }

    [Fact]
    public void Non_vat_doc_label_is_configurable()
    {
        var (th, _) = DocumentLabels.TaxInvoiceHeader(false, "ใบแจ้งหนี้", "Invoice");
        th.Should().Be("ใบแจ้งหนี้");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Vat_breakdown_shown_only_in_vat_mode(bool vatMode) =>
        DocumentLabels.ShowVatBreakdown(vatMode).Should().Be(vatMode);

    [Fact]
    public void Credit_note_cites_86_10_in_vat_mode_and_82_9_otherwise()
    {
        var vat = DocumentLabels.AdjustmentNote(TaxAdjustmentNoteType.Credit, true);
        vat.TitleTh.Should().Be("ใบลดหนี้");
        vat.LegalRef.Should().Be("ม.86/10");

        var nonVat = DocumentLabels.AdjustmentNote(TaxAdjustmentNoteType.Credit, false);
        nonVat.LegalRef.Should().Be("ม.82/9");
    }

    [Fact]
    public void Debit_note_cites_86_9_in_vat_mode_and_82_9_otherwise()
    {
        var vat = DocumentLabels.AdjustmentNote(TaxAdjustmentNoteType.Debit, true);
        vat.TitleTh.Should().Be("ใบเพิ่มหนี้");
        vat.LegalRef.Should().Be("ม.86/9");

        var nonVat = DocumentLabels.AdjustmentNote(TaxAdjustmentNoteType.Debit, false);
        nonVat.LegalRef.Should().Be("ม.82/9");
    }
}
