using Accounting.Application.Reports;
using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

/// <summary>
/// Pure builder test for the financial-statement supporting report PDF (no DB — input is two report
/// DTOs + a header). Asserts STRUCTURAL only: %PDF magic + a comfortable size floor. (QuestPDF
/// FlateDecode-compresses the content stream, so Thai labels never appear literally in the bytes —
/// a text-substring assertion would be wrong; mirrors PayslipPdf/PurchasePdf coverage.) The
/// EnsureFont() in the builder sets the QuestPDF Community license, so no host/Program.cs is needed.
/// </summary>
public sealed class FinancialStatementPdfTests
{
    private static void AssertPdf(byte[] bytes)
    {
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(1024);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    private static BalanceSheetReport SampleBalanceSheet(bool balanced = true)
    {
        var assets = new BalanceSheetSection(
            new[]
            {
                new BalanceSheetRow("1110", "เงินสดและรายการเทียบเท่าเงินสด", 50_000m),
                new BalanceSheetRow("1130", "ลูกหนี้การค้า", 30_000m),
            },
            Total: 80_000m);
        var liabilities = new BalanceSheetSection(
            new[] { new BalanceSheetRow("2110", "เจ้าหนี้การค้า", 20_000m) },
            Total: 20_000m);
        var equity = new BalanceSheetSection(
            new[] { new BalanceSheetRow("3100", "ทุนจดทะเบียนที่ชำระแล้ว", 40_000m) },
            Total: 40_000m);
        // CurrentPeriodEarnings is a DISTINCT field; LiabAndEquity = Liab + Equity + CPE.
        var cpe = 20_000m;
        var liabEquity = liabilities.Total + equity.Total + cpe; // 80,000 → balances Assets
        return new BalanceSheetReport(
            AsOfDate: new DateOnly(2026, 12, 31), CompanyId: 1,
            Assets: assets, Liabilities: liabilities, Equity: equity,
            CurrentPeriodEarnings: cpe,
            LiabilitiesAndEquityTotal: balanced ? liabEquity : liabEquity + 1m,
            Balanced: balanced,
            Note: "test");
    }

    private static ProfitLossReport SampleProfitLoss()
    {
        var totals = new ProfitLossGroup(null, null, "รวม", Revenue: 500_000m, Expense: 480_000m, NetProfit: 20_000m);
        return new ProfitLossReport(
            From: new DateOnly(2026, 1, 1), To: new DateOnly(2026, 12, 31),
            Groups: new[] { totals }, Totals: totals, Note: "test");
    }

    [Fact]
    public void Renders_valid_pdf_when_balanced()
    {
        var bytes = FinancialStatementPdf.Render(
            new FinancialStatementHeader("บริษัท ทดสอบ จำกัด", "0105500000001"),
            SampleBalanceSheet(balanced: true), SampleProfitLoss());
        AssertPdf(bytes);
    }

    [Fact]
    public void Renders_valid_pdf_even_when_unbalanced()
    {
        // The report surfaces the imbalance in text; it must still produce a valid PDF (never throws).
        var bytes = FinancialStatementPdf.Render(
            new FinancialStatementHeader("บริษัท ทดสอบ จำกัด", "0105500000001"),
            SampleBalanceSheet(balanced: false), SampleProfitLoss());
        AssertPdf(bytes);
    }
}
