using System.IO;

namespace Accounting.Api.Tests.Pdf;

internal static class Templates
{
    public static byte[] Load(string file)
    {
        var asm = typeof(Accounting.Infrastructure.Pdf.RdAcroFormFiller).Assembly;
        using var s = asm.GetManifestResourceStream($"Accounting.Infrastructure.Pdf.Templates.{file}")!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
