using Accounting.Api.Endpoints;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// Codex review finding #7 (2026-07-10) — CSV formula-injection hardening. One shared helper
/// (<see cref="ReportEndpoints.CsvCell"/>) used by every backend CSV export in
/// ReportEndpoints.cs (ar-aging/export, general-ledger/export csv branch).
/// </summary>
public sealed class ReportEndpointsCsvCellTests
{
    [Theory]
    [InlineData("=cmd|'/c calc'!A1", "\"'=cmd|'/c calc'!A1\"")]
    [InlineData("+1+1", "\"'+1+1\"")]
    [InlineData("-1+1", "\"'-1+1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    public void CsvCell_prefixes_formula_injection_trigger_chars_before_quoting(string input, string expected)
    {
        ReportEndpoints.CsvCell(input).Should().Be(expected);
    }

    [Fact]
    public void CsvCell_prefixes_leading_tab_and_cr()
    {
        ReportEndpoints.CsvCell("\tfoo").Should().Be("\"'\tfoo\"");
        ReportEndpoints.CsvCell("\rfoo").Should().Be("\"'\rfoo\"");
    }

    [Fact]
    public void CsvCell_leaves_normal_text_unprefixed()
    {
        ReportEndpoints.CsvCell("บริษัท ทดสอบ จำกัด").Should().Be("\"บริษัท ทดสอบ จำกัด\"");
    }

    [Fact]
    public void CsvCell_null_returns_empty_string_not_quoted_null()
    {
        ReportEndpoints.CsvCell(null).Should().Be("");
    }

    [Fact]
    public void CsvCell_still_doubles_embedded_quotes_after_prefixing()
    {
        ReportEndpoints.CsvCell("=say \"hi\"").Should().Be("\"'=say \"\"hi\"\"\"");
    }
}
