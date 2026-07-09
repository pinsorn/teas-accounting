using Accounting.Domain.Common;
using Accounting.Infrastructure.Bank.Pdf;
using FluentAssertions;
using PdfSharp.Pdf;
using Xunit;

namespace Accounting.Api.Tests.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md T4, §Security) — KPlusPdfTextExtractor
/// decryption smoke test. Generates a TINY password-protected PDF with PDFsharp at setup (no
/// real bank data, no real password — "test-pw-1234" is a throwaway literal for this test
/// only). Asserts: the right password opens it; a wrong password fails with the generic
/// `bank.pdf_password` code and a message that does NOT contain the attempted password
/// anywhere (message text, ToString(), or any nested data) — the mandatory §Security contract.
/// </summary>
public sealed class KPlusPdfTextExtractorTests
{
    private const string CorrectPassword = "test-pw-1234";
    private const string WrongPassword = "definitely-not-it-9999";

    private static MemoryStream BuildEncryptedPdf(string userPassword)
    {
        var doc = new PdfDocument();
        doc.SecuritySettings.UserPassword = userPassword;
        doc.SecuritySettings.OwnerPassword = userPassword;
        doc.AddPage();   // blank page — this test only exercises decryption, not word extraction
        var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Extract_opens_successfully_with_the_correct_password()
    {
        using var pdf = BuildEncryptedPdf(CorrectPassword);
        var act = () => KPlusPdfTextExtractor.Extract(pdf, CorrectPassword);
        act.Should().NotThrow();
    }

    [Fact]
    public void Extract_wrong_password_throws_generic_error_without_echoing_the_attempt()
    {
        using var pdf = BuildEncryptedPdf(CorrectPassword);

        var act = () => KPlusPdfTextExtractor.Extract(pdf, WrongPassword);

        var ex = act.Should().Throw<DomainException>().Which;
        ex.Code.Should().Be("bank.pdf_password");
        ex.Message.Should().NotContain(WrongPassword);
        ex.Message.Should().NotContain(CorrectPassword);
        ex.Message.Should().Be("Could not open the statement PDF — check the password.");
        // DomainException has no InnerException slot at all — nothing to leak via wrapping.
        ex.InnerException.Should().BeNull();
    }

    [Fact]
    public void Extract_missing_password_throws_generic_error_without_echoing_the_attempt()
    {
        using var pdf = BuildEncryptedPdf(CorrectPassword);

        var act = () => KPlusPdfTextExtractor.Extract(pdf, null);

        var ex = act.Should().Throw<DomainException>().Which;
        ex.Code.Should().Be("bank.pdf_password");
        ex.Message.Should().NotContain(CorrectPassword);
    }
}
