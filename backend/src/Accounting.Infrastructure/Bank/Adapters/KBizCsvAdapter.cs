using System.Globalization;
using System.Text.RegularExpressions;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Bank.Csv;

namespace Accounting.Infrastructure.Bank.Adapters;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md §B2, D2, D3) — KBiz (KBank) CSV statement
/// adapter. UTF-8 BOM; RFC4180 (quoted commas/embedded newlines — the account-holder address
/// cell spans lines). Metadata is parsed by LABEL, not fixed column position: the real export's
/// label→value column gap varies row to row (extra spacer cells before a unit-word like
/// "รายการ"), so this takes the LAST non-empty cell after the label as the value and the FIRST
/// non-empty cell as an optional count — robust to that variance rather than brittle fixed
/// offsets. Dates are DD-MM-YY, CE (always 20YY) — the document-ref BE year suffix (…/2569)
/// must NEVER be used to derive the transaction year.
/// </summary>
public sealed class KBizCsvAdapter : IBankStatementAdapter
{
    public string AdapterCode => "KBIZ_CSV";

    public bool CanHandle(string fileName, string mimeType) =>
        fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> MetadataLabels =
    [
        "เลขที่บัญชีเงินฝาก", "รอบระหว่างวันที่", "ยอดยกไป", "รวมถอนเงิน", "รวมฝากเงิน",
    ];

    private static readonly Regex RawRefPattern = new(@"รหัสอ้างอิง\s*(\S+)", RegexOptions.Compiled);

    public ParsedStatement Parse(Stream content, string? password)
    {
        // CSV is plaintext — password (reserved for the K-Plus PDF adapter, B3) is unused here.
        var rows = Rfc4180Reader.ReadAll(content);

        var headerIdx = rows.FindIndex(r => r.Contains("วันที่") && r.Contains("ยอดคงเหลือ"));
        if (headerIdx < 0)
            throw new DomainException("bank.csv_header_not_found",
                "Could not locate the statement header row (expected วันที่ / ยอดคงเหลือ columns).");
        var header = rows[headerIdx];

        int ColOf(string label)
        {
            var idx = Array.IndexOf(header, label);
            if (idx < 0)
                throw new DomainException("bank.csv_header_not_found", $"Header column '{label}' not found.");
            return idx;
        }
        var cDate = ColOf("วันที่");
        var cTime = ColOf("เวลา/ วันที่มีผล");
        var cType = ColOf("รายการ");
        var cWithdraw = ColOf("ถอนเงิน");
        var cDeposit = ColOf("ฝากเงิน");
        var cBalance = ColOf("ยอดคงเหลือ");
        var cChannel = ColOf("ช่องทาง");
        var cDetail = ColOf("รายละเอียด");
        var maxIdx = new[] { cDate, cTime, cType, cWithdraw, cDeposit, cBalance, cChannel, cDetail }.Max();

        // ── Metadata (label-matched, not position-matched — rows[0..headerIdx)) ──
        string? accountNo = null;
        DateOnly? periodStart = null, periodEnd = null;
        decimal? closingBalanceMeta = null, withdrawalTotal = null, depositTotal = null;
        int? withdrawalCount = null, depositCount = null;

        foreach (var r in rows.Take(headerIdx))
        {
            var labelIdx = Array.FindIndex(r, c => MetadataLabels.Contains(c.Trim()));
            if (labelIdx < 0) continue;
            var after = r.Skip(labelIdx + 1).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (after.Length == 0) continue;
            var last = after[^1];

            switch (r[labelIdx].Trim())
            {
                case "เลขที่บัญชีเงินฝาก":
                    accountNo = last;
                    break;
                case "รอบระหว่างวันที่":
                    var span = last.Split('-', 2);
                    if (span.Length == 2)
                    {
                        periodStart = ParseDdMmYyyyCe(span[0].Trim());
                        periodEnd = ParseDdMmYyyyCe(span[1].Trim());
                    }
                    break;
                case "ยอดยกไป":
                    closingBalanceMeta = ParseAmount(last);
                    break;
                case "รวมถอนเงิน":
                    withdrawalTotal = ParseAmount(last);
                    withdrawalCount = int.TryParse(after[0], out var wc) ? wc : null;
                    break;
                case "รวมฝากเงิน":
                    depositTotal = ParseAmount(last);
                    depositCount = int.TryParse(after[0], out var dc) ? dc : null;
                    break;
            }
        }

        if (accountNo is null || periodStart is null || periodEnd is null || closingBalanceMeta is null)
            throw new DomainException("bank.csv_missing_metadata",
                "Could not locate all required metadata (account no. / period / closing balance).");

        // ── Data rows (rows[headerIdx+1..]) ──
        decimal? openingBalance = null;
        var lines = new List<ParsedStatementLine>();
        var lineNo = 0;

        foreach (var r in rows.Skip(headerIdx + 1))
        {
            if (r.Length <= maxIdx) continue;              // short/blank trailing row
            var dateCell = r[cDate].Trim();
            if (dateCell.Length == 0) continue;             // trailing blank row

            lineNo++;
            var type = r[cType].Trim();
            var withdraw = r[cWithdraw].Trim();
            var deposit = r[cDeposit].Trim();
            var balance = ParseAmount(r[cBalance]);

            if (type == "ยอดยกมา" && withdraw.Length == 0 && deposit.Length == 0)
            {
                openingBalance = balance;   // carry-forward anchor row — NOT a txn line
                continue;
            }
            if (withdraw.Length == 0 && deposit.Length == 0)
                continue;   // no cash movement (e.g. "เปิดบัญชี" account-opening notice) — skip
            if (withdraw.Length > 0 && deposit.Length > 0)
                throw new DomainException("bank.csv_ambiguous_direction",
                    $"Line {lineNo}: both withdrawal and deposit columns are populated.");

            var direction = withdraw.Length > 0 ? StatementDirection.MoneyOut : StatementDirection.MoneyIn;
            var amount = ParseAmount(withdraw.Length > 0 ? withdraw : deposit);
            var description = r[cDetail].Trim();
            var rawRefMatch = RawRefPattern.Match(description);

            lines.Add(new ParsedStatementLine(
                lineNo, ParseDdMmYyCe(dateCell),
                r[cTime].Trim() is { Length: > 0 } t ? TimeOnly.ParseExact(t, "HH:mm", CultureInfo.InvariantCulture) : null,
                null, direction, amount, balance,
                r[cChannel].Trim(), type, description,
                rawRefMatch.Success ? rawRefMatch.Groups[1].Value : null));
        }

        if (openingBalance is null)
            throw new DomainException("bank.csv_missing_metadata",
                "Could not locate the opening balance (ยอดยกมา) row.");

        return new ParsedStatement(
            AdapterCode, accountNo, periodStart.Value, periodEnd.Value,
            openingBalance.Value, closingBalanceMeta.Value,
            withdrawalTotal, depositTotal, withdrawalCount, depositCount, lines);
    }

    private static decimal ParseAmount(string s) =>
        decimal.Parse(s.Trim().Replace(",", ""), CultureInfo.InvariantCulture);

    /// <summary>Metadata period dates — DD/MM/YYYY, CE.</summary>
    private static DateOnly ParseDdMmYyyyCe(string s)
    {
        var p = s.Split('/');
        return new DateOnly(int.Parse(p[2]), int.Parse(p[1]), int.Parse(p[0]));
    }

    /// <summary>Transaction dates — DD-MM-YY, 2-digit CE ("26" -&gt; 2026). Never derive the
    /// year from the document-ref's BE suffix (…/2569).</summary>
    private static DateOnly ParseDdMmYyCe(string s)
    {
        var p = s.Split('-');
        return new DateOnly(2000 + int.Parse(p[2]), int.Parse(p[1]), int.Parse(p[0]));
    }
}
