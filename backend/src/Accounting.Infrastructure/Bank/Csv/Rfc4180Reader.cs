using System.Text;

namespace Accounting.Infrastructure.Bank.Csv;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B2.2) — minimal RFC4180 CSV tokenizer:
/// handles quoted fields containing commas, embedded newlines, and the <c>""</c> escape. No
/// CsvHelper dependency (Ponytail — stdlib string/char handling only). UTF-8 BOM is
/// auto-detected and stripped by <see cref="StreamReader"/>.
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

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;   // swallow; the following \n (or a bare \n) ends the row
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }
}
