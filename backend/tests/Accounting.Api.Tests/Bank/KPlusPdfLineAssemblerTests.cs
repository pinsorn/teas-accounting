using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Bank.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md T3) — KPlusPdfLineAssembler tests over
/// SYNTHETIC positional word arrays (hand-built <see cref="PositionedWord"/> lists — no
/// encrypted PDF needed). Covers: per-page header skip, ยอดยกมา carry-forward re-anchor across
/// pages, MoneyIn/MoneyOut via balance delta, multi-line channel-cell join, an interest line
/// with NO WHT, and the D10 balance-integrity check holding end to end. Positions are clean/
/// well-separated by design (unlike the real export's tight margins in places) — that's the
/// documented contract this pure assembler is tested against; the real PDF was independently
/// read for structure verification (see the spec's attempt log), never embedded here.
/// </summary>
public sealed class KPlusPdfLineAssemblerTests
{
    private static PositionedWord W(int page, string text, double l, double r, double t, double b = 0) =>
        new(page, text, l, r, t, b == 0 ? t - 8 : b);

    /// <summary>2 pages: page 1 = opening + withdrawal + deposit (multi-line channel wrap) +
    /// interest with no WHT; page 2 = its own header repeat + ยอดยกมา re-anchor + a withdrawal.
    /// Balance chain: 1000 -500 +1500 +3 -200 = 1803, matching the ยอดยกไป metadata (1,803.00)
    /// and รวมถอนเงิน/รวมฝากเงิน totals (700.00/2 lines, 1,503.00/2 lines).</summary>
    private static List<PositionedWord> BuildFixture()
    {
        var words = new List<PositionedWord>();

        void AddHeader(int page)
        {
            words.Add(W(page, "วันที่", 70, 85, 670));
            words.Add(W(page, "เวลา/", 95, 110, 670));
            words.Add(W(page, "วันที่มีผล", 95, 120, 660));
            words.Add(W(page, "รายการ", 150, 170, 670));
            words.Add(W(page, "ถอนเงิน", 210, 230, 670));
            words.Add(W(page, "ฝากเงิน", 240, 260, 670));
            words.Add(W(page, "ยอดคงเหลือ", 280, 315, 670));
            words.Add(W(page, "(บาท)", 290, 310, 660));
            words.Add(W(page, "ช่องทาง", 350, 375, 670));
            words.Add(W(page, "รายละเอียด", 450, 480, 670));
        }

        // ── Page 1 metadata (label-matched; well within the 6pt search tolerance) ──
        words.Add(W(1, "เลขที่บัญชีเงินฝาก", 345, 390, 758));
        words.Add(W(1, "999-9-99999-9", 393, 440, 755));
        words.Add(W(1, "รอบระหว่างวันที่", 345, 388, 745));
        words.Add(W(1, "01/02/2026", 393, 422, 742));
        words.Add(W(1, "-", 424, 427, 740));
        words.Add(W(1, "08/02/2026", 430, 459, 742));
        words.Add(W(1, "ยอดยกไป", 345, 369, 718));
        words.Add(W(1, "1,803.00", 505, 533, 715));
        words.Add(W(1, "รวมถอนเงิน", 345, 375, 704));
        words.Add(W(1, "2", 377, 387, 702));
        words.Add(W(1, "รายการ", 390, 408, 702));
        words.Add(W(1, "700.00", 504, 533, 702));
        words.Add(W(1, "รวมฝากเงิน", 345, 374, 691));
        words.Add(W(1, "2", 376, 383, 689));
        words.Add(W(1, "รายการ", 386, 404, 689));
        words.Add(W(1, "1,503.00", 504, 533, 689));

        AddHeader(1);

        // ยอดยกมา (page 1 opening anchor, balance 1,000.00)
        words.Add(W(1, "01-02-26", 68, 90, 645));
        words.Add(W(1, "ยอดยกมา", 148, 172, 645));
        words.Add(W(1, "1,000.00", 285, 310, 645));

        // Withdrawal: 1000 -> 500 = MoneyOut 500.00
        words.Add(W(1, "01-02-26", 68, 90, 630));
        words.Add(W(1, "13:25", 100, 115, 630));
        words.Add(W(1, "ถอนเงินสด", 148, 172, 630));
        words.Add(W(1, "500.00", 225, 245, 630));
        words.Add(W(1, "500.00", 285, 310, 630));
        words.Add(W(1, "ATM", 352, 372, 630));
        words.Add(W(1, "รหัสอ้างอิง", 452, 478, 630));
        words.Add(W(1, "ATM00001", 480, 512, 630));

        // Deposit: 500 -> 2000 = MoneyIn 1500.00, channel wraps "K PLUS" + "MOBILE" (own band)
        words.Add(W(1, "02-02-26", 68, 90, 610));
        words.Add(W(1, "09:00", 100, 115, 610));
        words.Add(W(1, "รับโอนเงิน", 148, 178, 610));
        words.Add(W(1, "1,500.00", 222, 248, 610));
        words.Add(W(1, "2,000.00", 282, 312, 610));
        words.Add(W(1, "K", 352, 356, 610));
        words.Add(W(1, "PLUS", 358, 375, 610));
        words.Add(W(1, "รหัสอ้างอิง", 452, 478, 610));
        words.Add(W(1, "DEP00002", 480, 512, 610));
        words.Add(W(1, "MOBILE", 352, 380, 598));   // wrapped continuation — no date, own band

        // Interest with NO paired WHT: 2000 -> 2003 = MoneyIn 3.00
        words.Add(W(1, "03-02-26", 68, 90, 580));
        words.Add(W(1, "23:59", 100, 115, 580));
        words.Add(W(1, "รับดอกเบี้ยเงินฝาก", 148, 195, 580));
        words.Add(W(1, "3.00", 228, 248, 580));
        words.Add(W(1, "2,003.00", 280, 315, 580));
        words.Add(W(1, "โอนอัตโนมัติ", 350, 390, 580));
        words.Add(W(1, "รหัสอ้างอิง", 452, 478, 580));
        words.Add(W(1, "PCB00099", 480, 512, 580));

        // ── Page 2: repeated header (must be skipped, not double-counted) ──
        AddHeader(2);

        // ยอดยกมา re-anchor (page 2), same balance the page-1 chain ended on (2,003.00)
        words.Add(W(2, "04-02-26", 68, 90, 645));
        words.Add(W(2, "ยอดยกมา", 148, 172, 645));
        words.Add(W(2, "2,003.00", 280, 315, 645));

        // Withdrawal: 2003 -> 1803 = MoneyOut 200.00
        words.Add(W(2, "04-02-26", 68, 90, 630));
        words.Add(W(2, "10:00", 100, 115, 630));
        words.Add(W(2, "ถอนเงินสด", 148, 172, 630));
        words.Add(W(2, "200.00", 225, 245, 630));
        words.Add(W(2, "1,803.00", 280, 315, 630));
        words.Add(W(2, "ATM", 352, 372, 630));
        words.Add(W(2, "รหัสอ้างอิง", 452, 478, 630));
        words.Add(W(2, "ATM00003", 480, 512, 630));

        return words;
    }

    [Fact]
    public void Assemble_reads_metadata_and_totals_correctly()
    {
        var stmt = KPlusPdfLineAssembler.Assemble(BuildFixture());

        stmt.AdapterCode.Should().Be("KPLUS_PDF");
        stmt.AccountNoRaw.Should().Be("999-9-99999-9");
        stmt.PeriodStart.Should().Be(new DateOnly(2026, 2, 1));
        stmt.PeriodEnd.Should().Be(new DateOnly(2026, 2, 8));
        stmt.OpeningBalance.Should().Be(1000.00m);
        stmt.ClosingBalance.Should().Be(1803.00m);
        stmt.WithdrawalTotal.Should().Be(700.00m);
        stmt.WithdrawalCount.Should().Be(2);
        stmt.DepositTotal.Should().Be(1503.00m);
        stmt.DepositCount.Should().Be(2);
    }

    [Fact]
    public void Assemble_skips_repeated_headers_and_yodyokma_anchors_across_pages()
    {
        var stmt = KPlusPdfLineAssembler.Assemble(BuildFixture());

        // 4 real cash-movement lines across both pages; ยอดยกมา (x2) and both header repeats
        // never become lines.
        stmt.Lines.Should().HaveCount(4);
        stmt.Lines.Select(l => l.TxnType).Should().Equal(
            "ถอนเงินสด", "รับโอนเงิน", "รับดอกเบี้ยเงินฝาก", "ถอนเงินสด");
    }

    [Fact]
    public void Assemble_derives_direction_from_balance_delta_sign()
    {
        var lines = KPlusPdfLineAssembler.Assemble(BuildFixture()).Lines;

        lines[0].Direction.Should().Be(StatementDirection.MoneyOut);   // 1000 -> 500
        lines[0].Amount.Should().Be(500.00m);
        lines[0].RunningBalance.Should().Be(500.00m);

        lines[1].Direction.Should().Be(StatementDirection.MoneyIn);    // 500 -> 2000
        lines[1].Amount.Should().Be(1500.00m);
        lines[1].RunningBalance.Should().Be(2000.00m);

        lines[3].Direction.Should().Be(StatementDirection.MoneyOut);   // page 2: 2003 -> 1803
        lines[3].Amount.Should().Be(200.00m);
        lines[3].RunningBalance.Should().Be(1803.00m);
    }

    [Fact]
    public void Assemble_joins_the_wrapped_multiline_channel_cell()
    {
        var deposit = KPlusPdfLineAssembler.Assemble(BuildFixture()).Lines[1];
        deposit.Channel.Should().Be("K PLUS MOBILE");
        deposit.RawRef.Should().Be("DEP00002");
    }

    [Fact]
    public void Assemble_handles_interest_line_with_no_paired_wht()
    {
        var lines = KPlusPdfLineAssembler.Assemble(BuildFixture()).Lines;
        var interest = lines.Single(l => l.TxnType == "รับดอกเบี้ยเงินฝาก");

        interest.Direction.Should().Be(StatementDirection.MoneyIn);
        interest.Amount.Should().Be(3.00m);
        interest.RawRef.Should().Be("PCB00099");
        // No WHT line follows it — total line count (4, asserted elsewhere) already proves this.
    }

    [Fact]
    public void Assemble_output_passes_the_shared_D10_integrity_check()
    {
        var stmt = KPlusPdfLineAssembler.Assemble(BuildFixture());
        var act = () => BankStatementIntegrity.Validate(stmt);
        act.Should().NotThrow();
    }

    /// <summary>WP-C (B-br F1) regression — SANITIZED synthetic fixture reproducing the two
    /// real K-Plus PDF page artifacts (no real account numbers/names/amounts — placeholder
    /// values only) that crashed the assembler against the ACTUAL statement:
    /// (1) a vertical stamp/watermark of single-CHARACTER words printed in the page's FAR-LEFT
    ///     MARGIN (well left of the Date column's own left edge) whose tight Y-spacing (&lt;=3.5pt
    ///     gaps — the same tolerance legitimate wrapped-continuation lines rely on) bridged the
    ///     Y-gap between two otherwise well-separated (30pt apart) transaction rows, fusing them
    ///     into ONE row-band; FinalizeRow then tried <c>decimal.Parse</c> on a two-token balance
    ///     cell ("700.00 550.00") and threw an unhandled <see cref="FormatException"/> (a raw 500,
    ///     not the intended 422);
    /// (2) a per-page footer note whose leading words happen to fall inside the Date column's own
    ///     X-range but are not date-SHAPED text — pre-fix, ANY word landing in the Date bucket was
    ///     treated as a new transaction row, producing a row with no Amount/Balance that crashed
    ///     FinalizeRow with "Row dated &lt;footer text&gt; has no balance value."</summary>
    private static List<PositionedWord> BuildMarginNoiseFixture()
    {
        var words = new List<PositionedWord>();

        void AddHeader()
        {
            words.Add(W(1, "วันที่", 70, 85, 670));
            words.Add(W(1, "เวลา/", 95, 110, 670));
            words.Add(W(1, "วันที่มีผล", 95, 120, 660));
            words.Add(W(1, "รายการ", 150, 170, 670));
            words.Add(W(1, "ถอนเงิน", 210, 230, 670));
            words.Add(W(1, "ฝากเงิน", 240, 260, 670));
            words.Add(W(1, "ยอดคงเหลือ", 280, 315, 670));
            words.Add(W(1, "(บาท)", 290, 310, 660));
            words.Add(W(1, "ช่องทาง", 350, 375, 670));
            words.Add(W(1, "รายละเอียด", 450, 480, 670));
        }

        words.Add(W(1, "เลขที่บัญชีเงินฝาก", 345, 390, 758));
        words.Add(W(1, "111-1-11111-1", 393, 440, 755));
        words.Add(W(1, "รอบระหว่างวันที่", 345, 388, 745));
        words.Add(W(1, "01/02/2026", 393, 422, 742));
        words.Add(W(1, "-", 424, 427, 740));
        words.Add(W(1, "08/02/2026", 430, 459, 742));
        words.Add(W(1, "ยอดยกไป", 345, 369, 718));
        words.Add(W(1, "550.00", 505, 533, 715));

        AddHeader();

        // ยอดยกมา anchor: opening balance 1,000.00
        words.Add(W(1, "01-02-26", 68, 90, 645));
        words.Add(W(1, "ยอดยกมา", 148, 172, 645));
        words.Add(W(1, "1,000.00", 285, 310, 645));

        // Row A: withdrawal 300.00, 1000 -> 700, Top=630
        words.Add(W(1, "01-02-26", 68, 90, 630));
        words.Add(W(1, "13:25", 100, 115, 630));
        words.Add(W(1, "ถอนเงินสด", 148, 172, 630));
        words.Add(W(1, "300.00", 225, 245, 630));
        words.Add(W(1, "700.00", 285, 310, 630));
        words.Add(W(1, "ATM", 352, 372, 630));

        // Vertical watermark: 9 single-character words at Left=30 (centerX=32 — well left of the
        // Date column's derived table-left bound, ~62.5 for this header layout), each 3.0pt below
        // the last (Row A Top=630 -> 627 -> 624 -> ... -> 603 -> Row B Top=600): every consecutive
        // gap is <=3.5, so pre-fix this chain alone bridges Row A and Row B into one row-band.
        var stampTop = 627.0;
        for (var i = 0; i < 9; i++)
        {
            words.Add(W(1, "#", 30, 34, stampTop));
            stampTop -= 3.0;
        }

        // Row B: withdrawal 150.00, 700 -> 550, Top=600 (30pt below Row A — a normal real-row gap)
        words.Add(W(1, "02-02-26", 68, 90, 600));
        words.Add(W(1, "09:00", 100, 115, 600));
        words.Add(W(1, "ถอนเงินสด", 148, 172, 600));
        words.Add(W(1, "150.00", 225, 245, 600));
        words.Add(W(1, "550.00", 285, 310, 600));
        words.Add(W(1, "ATM", 352, 372, 600));

        // Per-page footer note, well below the last real row (15pt gap — its own band): sanitized
        // placeholder text (not the real statement's wording) whose words fall inside the Date/
        // Time columns' X-range but aren't date-shaped.
        words.Add(W(1, "NOTE", 68, 95, 585));
        words.Add(W(1, "SYSTEM", 100, 130, 585));

        return words;
    }

    [Fact]
    public void Assemble_ignores_left_margin_watermark_and_non_date_footer_note()
    {
        var stmt = KPlusPdfLineAssembler.Assemble(BuildMarginNoiseFixture());

        stmt.OpeningBalance.Should().Be(1000.00m);
        stmt.ClosingBalance.Should().Be(550.00m);
        stmt.Lines.Should().HaveCount(2);

        stmt.Lines[0].Direction.Should().Be(StatementDirection.MoneyOut);
        stmt.Lines[0].Amount.Should().Be(300.00m);
        stmt.Lines[0].RunningBalance.Should().Be(700.00m);

        stmt.Lines[1].Direction.Should().Be(StatementDirection.MoneyOut);
        stmt.Lines[1].Amount.Should().Be(150.00m);
        stmt.Lines[1].RunningBalance.Should().Be(550.00m);

        var act = () => BankStatementIntegrity.Validate(stmt);
        act.Should().NotThrow();
    }

    [Fact]
    public void Assemble_throws_when_a_balance_delta_contradicts_the_printed_amount()
    {
        // Corrupt the withdrawal's printed balance so it no longer matches its own amount cell —
        // the assembler still parses (amount is read independently of the delta, per D9), but the
        // shared D10 validator must catch the mismatch.
        var words = BuildFixture();
        // Disambiguate by Left, not Text: the amount cell (L=225) and balance cell (L=285) both
        // happen to read "500.00" for this row — target the BALANCE cell specifically.
        var idx = words.FindIndex(w => w.PageNo == 1 && w.Top == 630 && w.Left == 285);
        words[idx] = words[idx] with { Text = "450.00" };   // balance cell; now inconsistent with amount

        var stmt = KPlusPdfLineAssembler.Assemble(words);
        var act = () => BankStatementIntegrity.Validate(stmt);

        // The withdrawal is the first EMITTED line (ยอดยกมา doesn't get a line number) -> line 1.
        act.Should().Throw<DomainException>().Which.Message.Should().Contain("line 1");
    }
}
