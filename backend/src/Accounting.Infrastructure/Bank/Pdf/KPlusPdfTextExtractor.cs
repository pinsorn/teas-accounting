using Accounting.Domain.Common;
using UglyToad.PdfPig;

namespace Accounting.Infrastructure.Bank.Pdf;

/// <summary>A word + its bounding-box position, extracted from one page of a PDF. Coordinates
/// are PdfPig's own (PDF space — Y increases upward); the assembler only needs internal
/// consistency, not a particular origin.</summary>
public sealed record PositionedWord(int PageNo, string Text, double Left, double Right, double Top, double Bottom);

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B3.2) — thin PdfPig decrypt + word-with-
/// position extraction. Deliberately dumb: all the row/column reconstruction logic lives in the
/// PURE <see cref="KPlusPdfLineAssembler"/> (testability seam, D9). Smoke-tested only (T4).
///
/// §Security (MANDATORY): on ANY failure to open the document (missing/wrong password, or a
/// corrupted/non-PDF file), this throws a FRESH, generic <see cref="DomainException"/> with a
/// HARDCODED message — never the caught exception's own message, never wrapped as an inner
/// exception (DomainException has no InnerException slot at all), and nothing is logged here.
/// Verified against <c>DomainExceptionMiddleware</c>: it surfaces <c>DomainException.Message</c>
/// verbatim to the client, and the generic catch-all branch ALSO leaks inner-exception chains in
/// Development — so a hardcoded, caller-independent message is the only safe design regardless
/// of environment. The password itself lives only in this method's local parameter scope.
/// </summary>
public static class KPlusPdfTextExtractor
{
    public static IReadOnlyList<PositionedWord> Extract(Stream content, string? password)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(content, new ParsingOptions { Password = password ?? string.Empty });
        }
        catch
        {
            throw new DomainException("bank.pdf_password",
                "Could not open the statement PDF — check the password.");
        }

        using (document)
        {
            var words = new List<PositionedWord>();
            foreach (var page in document.GetPages())
            {
                foreach (var w in page.GetWords())
                {
                    words.Add(new PositionedWord(
                        page.Number, w.Text,
                        w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Top, w.BoundingBox.Bottom));
                }
            }
            return words;
        }
    }
}
