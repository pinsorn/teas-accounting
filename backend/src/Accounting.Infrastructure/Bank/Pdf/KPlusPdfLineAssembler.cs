using System.Globalization;
using System.Text.RegularExpressions;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Infrastructure.Bank.Pdf;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B3.3, D9) — PURE row/column reconstruction.
/// No IO: takes already-extracted <see cref="PositionedWord"/>s (from
/// <see cref="KPlusPdfTextExtractor"/>, or a hand-built synthetic fixture in tests) and returns a
/// <see cref="ParsedStatement"/>.
///
/// Column X-ranges are derived from the header row's OWN word positions (never hardcoded), per
/// D9 — validated against the real K-Plus export: column boundaries are the midpoints between
/// adjacent header-label CENTERS, and a word is assigned to whichever column its own center-X
/// falls closest to. The header wraps across 2 physical lines for two labels ("เวลา/" +
/// "วันที่มีผล", "ยอดคงเหลือ" + "(บาท)") — both halves are unioned into one anchor.
///
/// EACH page repeats the full header block and starts with its own "ยอดยกมา" carry-forward row
/// — both are skipped (not txn lines); ยอดยกมา RE-ANCHORS the running balance per page.
///
/// Direction is DERIVED from the balance-delta SIGN (D9); Amount is the PDF's own PRINTED
/// amount-cell value (independently parsed, like the CSV adapter) — NOT the delta itself, so
/// D10's shared <see cref="BankStatementIntegrity"/> check ("parsed amount cell must equal the
/// delta") is a real, non-vacuous cross-check rather than trivially true by construction.
///
/// Multi-line channel/description cells: a row-band with NO date-column word is a WRAPPED
/// CONTINUATION of the previous transaction row (not its own line, not the next one) — its
/// channel/detail words are appended to the current row's accumulator.
/// </summary>
public static class KPlusPdfLineAssembler
{
    private const string AdapterCode = "KPLUS_PDF";
    private const double RowYTolerance = 3.5;
    private const double MetadataYTolerance = 6.0;

    private static readonly (string Key, string[] Labels)[] ColumnDefs =
    [
        ("Date", ["วันที่"]),
        ("Time", ["เวลา/", "วันที่มีผล"]),
        ("Type", ["รายการ"]),
        ("Amount", ["ถอนเงิน", "ฝากเงิน"]),
        ("Balance", ["ยอดคงเหลือ", "(บาท)"]),
        ("Channel", ["ช่องทาง"]),
        ("Detail", ["รายละเอียด"]),
    ];

    private static readonly Regex RawRefPattern = new(@"รหัสอ้างอิง\s*(\S+)", RegexOptions.Compiled);

    // WP-C (B-br F1) — the real export prints a per-page footer note ("ออกโดย K PLUS...", issued-by
    // disclaimer) whose leading words happen to land in the Date column's X-range by coincidence,
    // but are not date-SHAPED text. Without this check that footer band was treated as a new
    // transaction row (hasDate = "any word landed in the Date bucket"), producing a row with no
    // Amount/Balance and crashing FinalizeRow. A genuine Date cell (transaction row or the
    // ยอดยกมา anchor) always matches DD-MM-YY (see ParseDdMmYyCe) — require that shape instead of
    // mere bucket occupancy.
    private static readonly Regex DateShapePattern = new(@"^\d{2}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private sealed record ColumnAnchor(string Key, double CenterX, double MinTop);

    public static ParsedStatement Assemble(IReadOnlyList<PositionedWord> words)
    {
        var pages = words.GroupBy(w => w.PageNo).OrderBy(g => g.Key).ToList();
        if (pages.Count == 0)
            throw new DomainException("bank.pdf_empty", "The statement PDF has no extractable pages.");

        var page1Words = pages[0].ToList();
        var anchors = DeriveColumnAnchors(page1Words);
        var metadata = ParseMetadata(page1Words);

        // WP-C (B-br F1) — the real K-Plus export prints a small vertical stamp/watermark of
        // single-character words in the page's LEFT MARGIN, well left of the "Date" column. Row
        // clustering (below) bands purely by Y-proximity and is X-blind, so this stamp's tightly-
        // stacked characters can bridge the Y-gap between two otherwise well-separated transaction
        // rows and fuse them into ONE row-band (two dates/amounts/balances joined by a space in
        // each column → FinalizeRow's decimal.Parse throws, surfacing as an unhandled 500). Fix:
        // derive an outer left/right bound for the table using the SAME symmetric-midpoint logic
        // AssignColumns already uses for INNER column boundaries (D9 — data-driven, never
        // hardcoded), and drop any word outside that span BEFORE clustering — filtering only at
        // AssignColumns time would be too late, since clustering already ran.
        var sortedAnchors = anchors.OrderBy(a => a.CenterX).ToList();
        var halfFirstGap = (sortedAnchors[1].CenterX - sortedAnchors[0].CenterX) / 2.0;
        var halfLastGap = (sortedAnchors[^1].CenterX - sortedAnchors[^2].CenterX) / 2.0;
        var tableLeftBound = sortedAnchors[0].CenterX - halfFirstGap;
        var tableRightBound = sortedAnchors[^1].CenterX + halfLastGap;

        decimal? openingBalance = null;
        decimal? runningBalance = null;
        var lines = new List<ParsedStatementLine>();
        var lineNo = 0;

        foreach (var page in pages)
        {
            var headerBottom = anchors.Min(a => a.MinTop);   // "(บาท)"/"วันที่มีผล" — lowest header sub-line
            var txnWords = page.Where(w => w.Top < headerBottom - 2.0)
                .Where(w => (w.Left + w.Right) / 2.0 is var cx && cx >= tableLeftBound && cx <= tableRightBound)
                .ToList();
            var rows = ClusterRows(txnWords);

            CoreRow? current = null;
            foreach (var row in rows)
            {
                var cols = AssignColumns(row, anchors);
                var hasDate = DateShapePattern.IsMatch(cols["Date"].Trim());

                if (hasDate)
                {
                    if (current is not null)
                        FinalizeRow(current, ref openingBalance, ref runningBalance, ref lineNo, lines);
                    current = new CoreRow(cols["Date"], cols["Time"], cols["Type"], cols["Amount"],
                        cols["Balance"], cols["Channel"], cols["Detail"]);
                }
                else if (current is not null)
                {
                    // Wrapped continuation line — no date, so it belongs to the PREVIOUS row.
                    if (cols["Channel"].Length > 0) current.Channel += " " + cols["Channel"];
                    if (cols["Detail"].Length > 0) current.Detail += " " + cols["Detail"];
                }
                // else: stray words above the first date row on this page — ignore.
            }
            if (current is not null)
                FinalizeRow(current, ref openingBalance, ref runningBalance, ref lineNo, lines);
        }

        if (openingBalance is null)
            throw new DomainException("bank.pdf_missing_metadata",
                "Could not locate the opening balance (ยอดยกมา) row.");

        return new ParsedStatement(
            AdapterCode, metadata.AccountNo, metadata.PeriodStart, metadata.PeriodEnd,
            openingBalance.Value, metadata.ClosingBalance,
            metadata.WithdrawalTotal, metadata.DepositTotal, metadata.WithdrawalCount, metadata.DepositCount,
            lines);
    }

    private sealed class CoreRow(string date, string time, string type, string amount,
        string balance, string channel, string detail)
    {
        public string Date { get; } = date;
        public string Time { get; } = time;
        public string Type { get; } = type;
        public string Amount { get; } = amount;
        public string Balance { get; } = balance;
        public string Channel { get; set; } = channel;
        public string Detail { get; set; } = detail;
    }

    private static void FinalizeRow(
        CoreRow row, ref decimal? openingBalance, ref decimal? runningBalance, ref int lineNo,
        List<ParsedStatementLine> lines)
    {
        if (row.Balance.Length == 0)
            throw new DomainException("bank.pdf_missing_balance", $"Row dated {row.Date} has no balance value.");
        var balance = ParseAmount(row.Balance);

        if (row.Type.Trim() == "ยอดยกมา")
        {
            runningBalance = balance;
            openingBalance ??= balance;   // first anchor across the whole statement = opening balance
            return;
        }

        if (runningBalance is null)
            throw new DomainException("bank.pdf_missing_metadata",
                $"Row dated {row.Date} has no preceding ยอดยกมา anchor to derive direction from.");
        if (row.Amount.Length == 0)
            throw new DomainException("bank.pdf_missing_amount", $"Row dated {row.Date} has no amount value.");

        // A zero delta (balance == runningBalance) falls through to MoneyOut — an arbitrary but
        // SAFE default: a genuine zero-movement row would only reach here with a printed Amount
        // of 0 too, and D10's shared balance-delta check (Amount == |delta|) still holds either
        // way (|0|==0); any REAL mismatch (e.g. Amount>0 with delta==0) is still flagged loudly
        // by BankStatementIntegrity.Validate, not silently accepted just because direction guessed.
        var direction = balance > runningBalance.Value ? StatementDirection.MoneyIn : StatementDirection.MoneyOut;
        var amount = ParseAmount(row.Amount);
        var description = row.Detail.Trim();
        var rawRefMatch = RawRefPattern.Match(description);

        lineNo++;
        lines.Add(new ParsedStatementLine(
            lineNo, ParseDdMmYyCe(row.Date), ParseTimeOrNull(row.Time), null,
            direction, amount, balance, row.Channel.Trim(), row.Type.Trim(), description,
            rawRefMatch.Success ? rawRefMatch.Groups[1].Value : null));

        runningBalance = balance;
    }

    // ── Column anchor derivation (D9 — from the header row's own positions, page 1 only; the
    // layout repeats identically on every page) ──────────────────────────────────────────────

    private static List<ColumnAnchor> DeriveColumnAnchors(IReadOnlyList<PositionedWord> page1Words)
    {
        // "วันที่" (date) is the one header label guaranteed unambiguous — it never recurs
        // elsewhere on the page. Use its Y position as a reference: some OTHER header labels
        // are NOT unique ("รายการ" is also the generic unit-word in the metadata totals lines,
        // "N รายการ" — picking the page's TOPMOST match would grab that metadata occurrence,
        // not the header). So for every label, pick whichever occurrence sits CLOSEST in Y to
        // the date header, not simply the topmost.
        var dateRef = page1Words.Where(w => w.Text == "วันที่").OrderByDescending(w => w.Top).FirstOrDefault()
            ?? throw new DomainException("bank.pdf_header_not_found", "Header column 'Date' not found.");
        var refTop = dateRef.Top;

        var anchors = new List<ColumnAnchor>();
        foreach (var (key, labels) in ColumnDefs)
        {
            var picks = labels
                .Select(l => page1Words.Where(w => w.Text == l)
                    .OrderBy(w => Math.Abs(w.Top - refTop)).FirstOrDefault())
                .Where(w => w is not null).Select(w => w!).ToList();
            if (picks.Count == 0)
                throw new DomainException("bank.pdf_header_not_found", $"Header column '{key}' not found.");
            var left = picks.Min(w => w.Left);
            var right = picks.Max(w => w.Right);
            var minTop = picks.Min(w => w.Top);
            anchors.Add(new ColumnAnchor(key, (left + right) / 2.0, minTop));
        }
        return anchors.OrderBy(a => a.CenterX).ToList();
    }

    private static Dictionary<string, string> AssignColumns(
        List<PositionedWord> rowWords, List<ColumnAnchor> anchorsByX)
    {
        var sortedAnchors = anchorsByX.OrderBy(a => a.CenterX).ToList();
        var boundaries = new double[sortedAnchors.Count - 1];
        for (var i = 0; i < boundaries.Length; i++)
            boundaries[i] = (sortedAnchors[i].CenterX + sortedAnchors[i + 1].CenterX) / 2.0;

        var buckets = sortedAnchors.ToDictionary(a => a.Key, _ => new List<PositionedWord>());
        foreach (var w in rowWords.OrderBy(w => w.Left))
        {
            var cx = (w.Left + w.Right) / 2.0;
            var idx = 0;
            while (idx < boundaries.Length && cx >= boundaries[idx]) idx++;
            buckets[sortedAnchors[idx].Key].Add(w);
        }
        return buckets.ToDictionary(kv => kv.Key, kv => string.Join(" ", kv.Value.Select(w => w.Text)));
    }

    // ── Row clustering — rolling-anchor Y-banding: a word joins the current band if it's within
    // tolerance of the LAST word added (chains a multi-line same-transaction spread), starts a
    // new band otherwise. Real rows span up to ~3.2pt; distinct rows/wrapped continuations are
    // always ~10pt+ apart. ─────────────────────────────────────────────────────────────────────

    private static List<List<PositionedWord>> ClusterRows(IEnumerable<PositionedWord> words)
    {
        var sorted = words.OrderByDescending(w => w.Top).ThenBy(w => w.Left).ToList();
        var rows = new List<List<PositionedWord>>();
        foreach (var w in sorted)
        {
            if (rows.Count > 0 && Math.Abs(rows[^1][^1].Top - w.Top) <= RowYTolerance)
                rows[^1].Add(w);
            else
                rows.Add([w]);
        }
        return rows;
    }

    // ── Metadata (label-matched, same spirit as the KBiz CSV adapter — search to the RIGHT of
    // the label word within a Y-tolerance, join in X order; first token = count, last = value) ──

    private sealed record Metadata(
        string AccountNo, DateOnly PeriodStart, DateOnly PeriodEnd, decimal ClosingBalance,
        decimal? WithdrawalTotal, decimal? DepositTotal, int? WithdrawalCount, int? DepositCount);

    private static Metadata ParseMetadata(IReadOnlyList<PositionedWord> page1Words)
    {
        string? Value(string label)
        {
            var labelWord = page1Words.FirstOrDefault(w => w.Text == label);
            if (labelWord is null) return null;
            var valueWords = page1Words
                .Where(w => w.Left > labelWord.Right && Math.Abs(w.Top - labelWord.Top) <= MetadataYTolerance)
                .OrderBy(w => w.Left)
                .ToList();
            return valueWords.Count > 0 ? string.Join(" ", valueWords.Select(w => w.Text)) : null;
        }

        var accountNo = Value("เลขที่บัญชีเงินฝาก")
            ?? throw new DomainException("bank.pdf_missing_metadata", "Account number not found.");
        var periodRaw = Value("รอบระหว่างวันที่")
            ?? throw new DomainException("bank.pdf_missing_metadata", "Statement period not found.");
        var closingRaw = Value("ยอดยกไป")
            ?? throw new DomainException("bank.pdf_missing_metadata", "Closing balance (ยอดยกไป) not found.");
        var withdrawRaw = Value("รวมถอนเงิน");
        var depositRaw = Value("รวมฝากเงิน");

        var periodParts = periodRaw.Split('-', 2);
        if (periodParts.Length != 2)
            throw new DomainException("bank.pdf_missing_metadata", "Could not parse the statement period.");
        var periodStart = ParseDdMmYyyyCe(periodParts[0].Trim());
        var periodEnd = ParseDdMmYyyyCe(periodParts[1].Trim());

        var (withdrawalCount, withdrawalTotal) = ParseCountAndAmount(withdrawRaw);
        var (depositCount, depositTotal) = ParseCountAndAmount(depositRaw);

        return new Metadata(accountNo, periodStart, periodEnd, ParseAmount(closingRaw),
            withdrawalTotal, depositTotal, withdrawalCount, depositCount);
    }

    private static (int? Count, decimal? Amount) ParseCountAndAmount(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var amount = ParseAmount(tokens[^1]);
        var count = tokens.Length > 1 && int.TryParse(tokens[0], out var c) ? c : (int?)null;
        return (count, amount);
    }

    private static decimal ParseAmount(string s) =>
        decimal.Parse(s.Trim().Replace(",", ""), CultureInfo.InvariantCulture);

    /// <summary>Metadata period dates — DD/MM/YYYY, CE (same convention as the KBiz CSV adapter).</summary>
    private static DateOnly ParseDdMmYyyyCe(string s)
    {
        var p = s.Split('/');
        return new DateOnly(int.Parse(p[2]), int.Parse(p[1]), int.Parse(p[0]));
    }

    /// <summary>Transaction dates — DD-MM-YY, 2-digit CE ("26" -&gt; 2026), same as the CSV adapter.</summary>
    private static DateOnly ParseDdMmYyCe(string s)
    {
        var p = s.Trim().Split('-');
        return new DateOnly(2000 + int.Parse(p[2]), int.Parse(p[1]), int.Parse(p[0]));
    }

    private static TimeOnly? ParseTimeOrNull(string s) =>
        TimeOnly.TryParseExact(s.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t : null;
}
