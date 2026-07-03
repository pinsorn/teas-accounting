using Accounting.Infrastructure.Pdf;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

// cont.119 — the PDF header sizes an uploaded logo box to the image's own aspect
// (ImageProbe), so a wide/non-square logo is not cropped. These lock the header parsing.
public class ImageProbeTests
{
    [Fact]
    public void Reads_png_dimensions_from_ihdr()
    {
        // 89 50 4E 47 signature; width@16, height@20 (big-endian) = 340 × 96.
        var b = new byte[24];
        b[0] = 0x89; b[1] = 0x50; b[2] = 0x4E; b[3] = 0x47;
        b[16] = 0; b[17] = 0; b[18] = 0x01; b[19] = 0x54;   // 0x0154 = 340
        b[20] = 0; b[21] = 0; b[22] = 0; b[23] = 0x60;      // 0x60   = 96
        ImageProbe.Dimensions(b).Should().Be((340, 96));
        ImageProbe.AspectRatio(b).Should().BeApproximately(340f / 96f, 0.001f);
    }

    [Fact]
    public void Reads_jpeg_dimensions_from_sof0()
    {
        // FF D8 (SOI), FF C0 (SOF0), len 0x0011, precision 8, H=0x0060(96), W=0x0154(340).
        var b = new byte[] { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x60, 0x01, 0x54, 0x00 };
        var (w, h) = ImageProbe.Dimensions(b);
        w.Should().Be(340);
        h.Should().Be(96);
    }

    [Fact]
    public void Unknown_or_short_header_falls_back_to_square()
    {
        ImageProbe.Dimensions(new byte[] { 1, 2, 3 }).Should().Be((0, 0));
        ImageProbe.AspectRatio(new byte[] { 1, 2, 3 }).Should().Be(1f);   // → square box
        ImageProbe.AspectRatio(System.Array.Empty<byte>()).Should().Be(1f);
    }
}
