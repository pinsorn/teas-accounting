namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// Reads the pixel dimensions straight from a raster header so the PDF header can size an
/// uploaded logo box to the image's own aspect (no QuestPDF public API exposes this). Handles
/// PNG + JPEG — the formats a company logo is realistically stored as; anything else falls back
/// to a 1:1 aspect (a square box), which is safe because the caller renders with FitArea.
/// Pure + allocation-free; a bad/short header yields the 1:1 fallback rather than throwing.
/// </summary>
public static class ImageProbe
{
    /// <summary>Width ÷ height, or 1.0 when the header is unreadable / not PNG or JPEG.</summary>
    public static float AspectRatio(byte[] bytes)
    {
        var (w, h) = Dimensions(bytes);
        return h > 0 ? (float)w / h : 1f;
    }

    /// <summary>(width, height) in pixels, or (0, 0) when unknown.</summary>
    public static (int Width, int Height) Dimensions(byte[] b)
    {
        if (b is null || b.Length < 4) return (0, 0);

        // PNG: 8-byte signature, then IHDR chunk with width@16, height@20 (big-endian).
        if (b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return b.Length >= 24 ? (Be32(b, 16), Be32(b, 20)) : (0, 0);

        // JPEG: FF D8, then a chain of FF-marker segments; the SOFn frame header carries
        // height@+5, width@+7. Skip other segments by their 2-byte big-endian length.
        if (b[0] == 0xFF && b[1] == 0xD8)
        {
            int p = 2;
            while (p + 9 < b.Length)
            {
                if (b[p] != 0xFF) { p++; continue; }
                var marker = b[p + 1];
                // SOF0/1/2/3, 5/6/7, 9/10/11, 13/14/15 = frame headers carrying the size.
                bool isSof = (marker >= 0xC0 && marker <= 0xCF)
                             && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isSof) return (Be16(b, p + 7), Be16(b, p + 5));
                if (marker is 0xD8 or 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) { p += 2; continue; }
                var len = Be16(b, p + 2);
                if (len < 2) break;
                p += 2 + len;
            }
        }

        return (0, 0);
    }

    private static int Be16(byte[] b, int i) => (b[i] << 8) | b[i + 1];
    private static int Be32(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
}
