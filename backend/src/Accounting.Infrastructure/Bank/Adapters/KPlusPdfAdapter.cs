using Accounting.Application.Bank;
using Accounting.Infrastructure.Bank.Pdf;

namespace Accounting.Infrastructure.Bank.Adapters;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B3.4) — K-Plus (KBank mobile) PDF statement
/// adapter. Thin wire: <see cref="KPlusPdfTextExtractor"/> (PdfPig decrypt + word extraction)
/// feeds <see cref="KPlusPdfLineAssembler"/> (pure row/column reconstruction, D9).
/// </summary>
public sealed class KPlusPdfAdapter : IBankStatementAdapter
{
    public string AdapterCode => "KPLUS_PDF";

    public bool CanHandle(string fileName, string mimeType) =>
        fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public ParsedStatement Parse(Stream content, string? password) =>
        KPlusPdfLineAssembler.Assemble(KPlusPdfTextExtractor.Extract(content, password));
}
