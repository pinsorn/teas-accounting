using Accounting.Domain.Common;

namespace Accounting.Application.Pdf;

/// <summary>Signature/stamp upload rules (doc-signature spec §E4). PNG/JPEG/WebP ONLY —
/// image/svg+xml is deliberately excluded because it is NOT in FileStorage:AllowedMimeTypes
/// (LocalDiskFileStorage.cs), so AttachmentService.UploadAsync would reject it after our own
/// check passed anyway; an SVG is also a parser surface we have no reason to accept here.
/// 1 MB, mirroring the company-logo cap (CompanyProfileService.UpdateLogoAsync).</summary>
public static class SignatureImage
{
    public const long MaxBytes = 1L * 1024 * 1024;
    public static readonly string[] AllowedMimes = { "image/png", "image/jpeg", "image/webp" };

    public static void Validate(string mimeType, long sizeBytes, string codePrefix)
    {
        if (!AllowedMimes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
            throw new DomainException($"{codePrefix}.bad_mime",
                $"MIME type '{mimeType}' is not allowed (png/jpeg/webp only).");
        if (sizeBytes > MaxBytes)
            throw new DomainException($"{codePrefix}.too_large",
                "File exceeds the 1 MB limit.");
    }

    /// <summary>Tier-2 finding (2026-07-30, MED) — the caller-supplied MIME STRING can lie
    /// (Content-Type is client-controlled); <see cref="Validate"/> alone does not prove the bytes
    /// are actually decodable image content, and QuestPDF's <c>.Image()</c> throws on malformed
    /// bytes (would otherwise brick <c>GET /pdf</c> for every document a corrupted-upload user
    /// signs). Checks the real magic number, independent of the MIME string: PNG (89 50 4E 47),
    /// JPEG (FF D8 FF), WebP (RIFF....WEBP). Used on BOTH the read side (render-time, decorative —
    /// mismatch → null slot, never an exception) and the write side (upload-time — mismatch →
    /// the existing *.bad_mime error, better UX than a silently-blank box).</summary>
    public static bool HasValidImageMagic(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true; // PNG
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true; // JPEG
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return true; // WebP (RIFF container, WEBP fourcc at offset 8)
        return false;
    }
}
