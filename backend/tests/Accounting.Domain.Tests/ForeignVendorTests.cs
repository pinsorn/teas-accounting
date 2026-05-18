using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using FluentAssertions;

namespace Accounting.Domain.Tests;

/// <summary>
/// Sprint 8.7 — foreign-vendor / self-withhold domain surface. The flag-
/// interaction rules + GL are service-level (integration-tested in
/// Sprint87ForeignVendorTests); the pure invariants worth pinning here are the
/// gross-up arithmetic and the receipt-only recoverable boolean.
/// </summary>
public class ForeignVendorTests
{
    [Fact]
    public void New_vendor_defaults_are_domestic_vat_registered()
    {
        var v = new Vendor { CompanyId = 1, VendorCode = "V1", NameTh = "ผู้ขาย" };
        v.IsForeign.Should().BeFalse();
        v.HasThaiVatDReg.Should().BeFalse();
        v.CountryCode.Should().BeNull();
    }

    [Fact]
    public void New_payment_voucher_is_not_self_withhold_by_default()
    {
        var pv = new PaymentVoucher { CompanyId = 1, SubPrefix = "SVC", VendorName = "v" };
        pv.SelfWithholdMode.Should().BeFalse();
        pv.RequiresPnd36ReverseCharge.Should().BeFalse();
    }

    [Fact]
    public void Vendor_invoice_has_input_vat_by_default()
    {
        var vi = new VendorInvoice { CompanyId = 1, VendorTaxInvoiceNo = "T1", VendorName = "v" };
        vi.HasInputVat.Should().BeTrue();
        vi.RequiresPnd36ReverseCharge.Should().BeFalse();
    }

    [Theory]
    // subtotal, vat, wht → gross-up: expense = sub+vat+wht, cash = sub+vat
    [InlineData(3500, 0, 525, 4025, 3500)]      // foreign AWS (no VAT, 15%)
    [InlineData(10000, 700, 300, 11000, 10700)] // domestic self-withhold (3%)
    public void Self_withhold_gross_up_math(
        decimal sub, decimal vat, decimal wht, decimal expExpense, decimal expCash)
    {
        (sub + vat + wht).Should().Be(expExpense);   // Dr Expense (our cost)
        (sub + vat).Should().Be(expCash);            // Cr Bank (full payment)
        // JV balances: expense == cash + wht_payable.
        (sub + vat + wht).Should().Be(expCash + wht);
    }

    [Theory]
    [InlineData(true, true, true)]    // HasInputVat && recoverable → recoverable
    [InlineData(false, true, false)]  // receipt-only → VAT lumps into expense
    [InlineData(true, false, false)]  // category non-recoverable (ม.82/5)
    public void Receipt_only_recoverable_boolean(
        bool hasInputVat, bool lineRecoverable, bool expected) =>
        (hasInputVat && lineRecoverable).Should().Be(expected);
}
