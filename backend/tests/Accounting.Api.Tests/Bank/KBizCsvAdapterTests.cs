using System.Text;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Bank.Adapters;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md T1/T2) — KBizCsvAdapter parser tests.
/// SYNTHETIC fixture only (redacted account no./name/address), modeled on the real KBiz export
/// structure (§B2: ~11 metadata rows, multi-line quoted address, leading empty column, DD-MM-YY
/// CE dates, quoted thousand-comma amounts, ยอดยกมา opening row, UTF-8 BOM) — never embeds real
/// bank data. Pure unit tests, no DB.
/// </summary>
public sealed class KBizCsvAdapterTests
{
    /// <summary>Shared fixture (also reused by <c>StatementImportServiceTests</c>). Balances:
    /// opening 1,000.00; deposit 20,000.00 + interest 5.00 = 20,005.00; withdrawal 15,000.00 +
    /// WHT 0.75 = 15,000.75; closing = 1,000.00 + 20,005.00 - 15,000.75 = 6,004.25.</summary>
    internal const string GoodCsv =
        "รายการเดินบัญชีเงินฝากออมทรัพย์ (มีรายละเอียด),,,,,,,,,,,,\r\n" +
        ",ชื่อบัญชี,\"บจก. ทดสอบ จำกัด\n99 ถ.ทดสอบ ต.ทดสอบ อ.ทดสอบ จ.ทดสอบ 10000\",,,,,เลขที่อ้างอิง,,,,TEST0000000000000,\r\n" +
        ",,,,,,,เลขที่บัญชีเงินฝาก,,,,999-9-99999-9,\r\n" +
        ",,,,,,,รอบระหว่างวันที่,,,,01/02/2026 - 07/07/2026,\r\n" +
        ",,,,,,,ยอดยกไป,,,,,\"6,004.25\"\r\n" +
        ",,,,,,,รวมถอนเงิน,,2,,รายการ,\"15,000.75\"\r\n" +
        ",,,,,,,รวมฝากเงิน,,2,,รายการ,\"20,005.00\"\r\n" +
        ",วันที่,เวลา/ วันที่มีผล,รายการ,ถอนเงิน,,ฝากเงิน,,ยอดคงเหลือ,,ช่องทาง,,รายละเอียด\r\n" +
        ",01-02-26,,ยอดยกมา,,,,,\"1,000.00\",,,,\r\n" +
        ",01-02-26,09:00,เปิดบัญชี,,,,,\"1,000.00\",,สาขาทดสอบ,,รหัสอ้างอิง OPEN0001\r\n" +
        ",25-05-26,18:39,รับโอนเงิน,,,\"20,000.00\",,\"21,000.00\",,MAKE by KBank,,จาก X0000 นาย ทดสอบ ระบบ\r\n" +
        ",26-05-26,12:22,โอนเงิน,\"15,000.00\",,,,\"6,000.00\",,สาขาทดสอบ,,โอนไป TEST BANK X0000 TEST CO\r\n" +
        ",19-06-26,23:59,รับดอกเบี้ยเงินฝาก,,,5.00,,\"6,005.00\",,โอนเข้า/หักบัญชีอัตโนมัติ,,รหัสอ้างอิง PCB00001\r\n" +
        ",19-06-26,23:59,ภาษีหัก ณ ที่จ่าย,0.75,,,,\"6,004.25\",,โอนเข้า/หักบัญชีอัตโนมัติ,,รหัสอ้างอิง PCB00001\r\n";

    internal static MemoryStream Utf8BomStream(string text) =>
        new(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray());

    [Fact]
    public void CanHandle_matches_csv_extension_only()
    {
        var a = new KBizCsvAdapter();
        a.CanHandle("statement.csv", "text/csv").Should().BeTrue();
        a.CanHandle("statement.CSV", "text/csv").Should().BeTrue();
        a.CanHandle("statement.pdf", "application/pdf").Should().BeFalse();
    }

    [Fact]
    public void Parse_reads_metadata_by_label_and_ce_dates_correctly()
    {
        var stmt = new KBizCsvAdapter().Parse(Utf8BomStream(GoodCsv), null);

        stmt.AdapterCode.Should().Be("KBIZ_CSV");
        stmt.AccountNoRaw.Should().Be("999-9-99999-9");
        stmt.PeriodStart.Should().Be(new DateOnly(2026, 2, 1));
        stmt.PeriodEnd.Should().Be(new DateOnly(2026, 7, 7));
        stmt.OpeningBalance.Should().Be(1000.00m);
        stmt.ClosingBalance.Should().Be(6004.25m);
        stmt.WithdrawalTotal.Should().Be(15000.75m);
        stmt.WithdrawalCount.Should().Be(2);
        stmt.DepositTotal.Should().Be(20005.00m);
        stmt.DepositCount.Should().Be(2);

        // The multi-line quoted address (row ABOVE the header) did not corrupt row alignment —
        // proven by the header (found AFTER it) and every field below resolving correctly.
        stmt.Lines.Should().HaveCount(4);
    }

    [Fact]
    public void Parse_skips_yodyokma_and_zero_movement_rows_emits_only_cash_lines()
    {
        var stmt = new KBizCsvAdapter().Parse(Utf8BomStream(GoodCsv), null);

        // 6 data rows in the fixture (ยอดยกมา, เปิดบัญชี, deposit, withdrawal, interest, WHT);
        // only the 4 with real cash movement become lines.
        stmt.Lines.Select(l => l.TxnType).Should().Equal(
            "รับโอนเงิน", "โอนเงิน", "รับดอกเบี้ยเงินฝาก", "ภาษีหัก ณ ที่จ่าย");
    }

    [Fact]
    public void Parse_derives_direction_amount_and_balance_per_line()
    {
        var lines = new KBizCsvAdapter().Parse(Utf8BomStream(GoodCsv), null).Lines;

        lines[0].Direction.Should().Be(StatementDirection.MoneyIn);
        lines[0].Amount.Should().Be(20000.00m);
        lines[0].RunningBalance.Should().Be(21000.00m);
        lines[0].Channel.Should().Be("MAKE by KBank");

        lines[1].Direction.Should().Be(StatementDirection.MoneyOut);
        lines[1].Amount.Should().Be(15000.00m);
        lines[1].RunningBalance.Should().Be(6000.00m);
    }

    [Fact]
    public void Parse_keeps_interest_and_its_wht_as_two_separate_lines_not_netted()
    {
        var lines = new KBizCsvAdapter().Parse(Utf8BomStream(GoodCsv), null).Lines;

        var interest = lines.Single(l => l.TxnType == "รับดอกเบี้ยเงินฝาก");
        var wht = lines.Single(l => l.TxnType == "ภาษีหัก ณ ที่จ่าย");

        interest.Direction.Should().Be(StatementDirection.MoneyIn);
        interest.Amount.Should().Be(5.00m);
        interest.RawRef.Should().Be("PCB00001");

        wht.Direction.Should().Be(StatementDirection.MoneyOut);
        wht.Amount.Should().Be(0.75m);
        wht.RawRef.Should().Be("PCB00001");
    }

    [Fact]
    public void Parse_converts_DD_MM_YY_to_CE_year_never_BE()
    {
        var lines = new KBizCsvAdapter().Parse(Utf8BomStream(GoodCsv), null).Lines;
        lines[0].TxnDate.Should().Be(new DateOnly(2026, 5, 25));   // "25-05-26" -> 2026, never 2569 (BE)
    }

    [Fact]
    public void Validate_throws_with_line_number_and_amount_only_never_description_text()
    {
        // Corrupt the deposit row's balance so its declared amount (20,000.00) no longer matches
        // the balance movement — and embed a "secret" description that must NEVER leak.
        var corrupt = GoodCsv.Replace(
            ",25-05-26,18:39,รับโอนเงิน,,,\"20,000.00\",,\"21,000.00\",,MAKE by KBank,,จาก X0000 นาย ทดสอบ ระบบ",
            ",25-05-26,18:39,รับโอนเงิน,,,\"20,000.00\",,\"16,000.00\",,MAKE by KBank,,SECRET PII SHOULD NOT LEAK");

        var parsed = new KBizCsvAdapter().Parse(Utf8BomStream(corrupt), null);

        var act = () => BankStatementIntegrity.Validate(parsed);

        act.Should().Throw<DomainException>()
            .Which.Message.Should()
            .Contain("line 3")
            .And.NotContain("SECRET PII SHOULD NOT LEAK");
    }
}
