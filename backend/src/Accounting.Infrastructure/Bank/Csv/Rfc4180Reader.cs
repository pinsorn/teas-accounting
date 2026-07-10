using System.Text;
using Accounting.Domain.Common;

namespace Accounting.Infrastructure.Bank.Csv;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B2.2) — minimal RFC4180 CSV tokenizer:
/// handles quoted fields containing commas, embedded newlines, and the <c>""</c> escape. No
/// CsvHelper dependency (Ponytail — stdlib string/char handling only). UTF-8 BOM is
/// auto-detected and stripped by <see cref="StreamReader"/>.
///
/// Codex review finding #8 (2026-07-10) — strictness hardening: a quote may only OPEN at the
/// very start of a fresh field; EOF while still inside a quoted field is a truncated/corrupt
/// file, not a silently-accepted field; and no unquoted "trailing junk" may follow a field's
/// closing quote before the next delimiter/newline. All three previously parsed silently WRONG
/// instead of failing loud — throws <see cref="DomainException"/> (bank.csv_malformed), same
/// convention as the adapters/BankStatementIntegrity that call this.
/// </summary>
internal static class Rfc4180Reader
{
    public static List<string[]> ReadAll(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        // True right after a field's closing quote, until the next delimiter/newline resets it —
        // any further char before that is disallowed "trailing junk" (e.g. "abc"def).
        var quoteJustClosed = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { inQuotes = false; quoteJustClosed = true; }
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    // A quote may only open at the very start of a fresh field — not mid-field
                    // (field already has content) and not right after a just-closed quote.
                    if (field.Length > 0 || quoteJustClosed)
                        throw new DomainException("bank.csv_malformed",
                            $"Unexpected quote mid-field at position {i}.");
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    quoteJustClosed = false;
                    break;
                case '\r':
                    break;   // swallow; the following \n (or a bare \n) ends the row
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row = [];
                    quoteJustClosed = false;
                    break;
                default:
                    if (quoteJustClosed)
                        throw new DomainException("bank.csv_malformed",
                            $"Unexpected content after a closing quote at position {i}.");
                    field.Append(c);
                    break;
            }
        }
        if (inQuotes)
            throw new DomainException("bank.csv_malformed",
                "Unterminated quoted field — reached end of file while still inside quotes.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }
}
