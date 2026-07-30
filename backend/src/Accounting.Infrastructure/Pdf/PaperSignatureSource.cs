using Accounting.Application.Abstractions;
using Accounting.Application.Pdf;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Pdf;

/// <summary>
/// doc-signature spec (§D4) — resolves the signature strip's imagery + signer positions at
/// RENDER time from the persisted actor ids + status — never baked in at click time, because
/// PDFs are produced on GET. A structural sibling of <see cref="PaperSellerSource.ResolveLogoAsync"/>.
/// Returns null for a document that has not reached its signed status (A1), so a Draft renders
/// the empty box exactly as today. Everything here is DECORATIVE: a missing row, a disallowed
/// MIME, or any read error yields a null slot, NEVER an exception — this must not fail a legal
/// document (same contract as PaperSellerSource.ResolveLogoAsync).
/// </summary>
public static class PaperSignatureSource
{
    public static async Task<PaperSignatures?> ResolveAsync(
        AccountingDbContext db, IFileStorageService? storage,
        long? leftActorUserId, long? middleActorUserId, bool stampOnMiddle,
        bool isSigned, CancellationToken ct)
    {
        // §A1 — the gate lives HERE, once, for all ten mappers.
        if (!isSigned || storage is null) return null;

        var userIds = new[] { leftActorUserId, middleActorUserId }
            .Where(x => x is > 0).Select(x => x!.Value).Distinct().ToArray();
        if (userIds.Length == 0) return null;

        // One attachment query — signature rows for the actor(s) + the (single) company stamp
        // row. No CompanyId predicate — the global query filter + G1 RLS already supply it
        // (§1.4), matching ResolveLogoAsync's convention. §16 F5 — the SAME two-key ordering
        // (UploadedAt then AttachmentId, both descending) as RbacAdminService/CompanyProfileService,
        // so a same-millisecond re-upload race resolves deterministically everywhere.
        var rows = await db.Set<Attachment>().AsNoTracking()
            .Where(a => a.DeletedAt == null
                && ((a.ParentType == AttachmentParentType.UserSignature && userIds.Contains(a.ParentId))
                    || a.ParentType == AttachmentParentType.CompanyStamp))
            .OrderByDescending(a => a.UploadedAt)
            .ThenByDescending(a => a.AttachmentId)
            .Select(a => new { a.AttachmentId, a.ParentType, a.ParentId, a.StoragePath, a.MimeType })
            .ToListAsync(ct);

        // Latest-wins per (ParentType, ParentId): first row of each group after the sort above.
        var latestSignature = rows
            .Where(r => r.ParentType == AttachmentParentType.UserSignature)
            .GroupBy(r => r.ParentId)
            .ToDictionary(g => g.Key, g => g.First());
        var latestStamp = rows.FirstOrDefault(r => r.ParentType == AttachmentParentType.CompanyStamp);

        // One user query for the positions AND the names (both rendered — §A3, §A4).
        var users = await db.Set<User>().AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .Select(u => new { u.UserId, u.Position, u.FullName })
            .ToListAsync(ct);
        var userById = users.ToDictionary(u => u.UserId);

        async Task<byte[]?> ReadAsync(string storagePath, string mimeType)
        {
            if (!SignatureImage.AllowedMimes.Contains(mimeType, StringComparer.OrdinalIgnoreCase))
                return null;
            try
            {
                await using var s = await storage.OpenReadAsync(storagePath, ct);
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms, ct);
                if (ms.Length == 0) return null;
                var bytes = ms.ToArray();
                // Tier-2 finding (2026-07-30, MED, I8) — the stored MIME string can be wrong/stale
                // (a spoofed Content-Type at upload time, or corruption at rest); QuestPDF's
                // .Image() throws on genuinely undecodable bytes, which would brick GET /pdf for
                // every document this actor signs. Verify the real magic number before ever
                // handing bytes to the renderer — mismatch is just another decorative-null case.
                return SignatureImage.HasValidImageMagic(bytes) ? bytes : null;
            }
            catch
            {
                return null; // decorative — never fail a legal document
            }
        }

        byte[]? leftBytes = null, middleBytes = null, stampBytes = null;
        string? leftUrl = null, middleUrl = null, stampUrl = null;
        string? leftPosition = null, middlePosition = null, leftName = null, middleName = null;

        if (leftActorUserId is { } lid && latestSignature.TryGetValue(lid, out var lrow))
        {
            leftBytes = await ReadAsync(lrow.StoragePath, lrow.MimeType);
            if (leftBytes is not null) leftUrl = $"/attachments/{lrow.AttachmentId}/download";
        }
        if (leftActorUserId is { } lid2 && userById.TryGetValue(lid2, out var luser))
        {
            leftPosition = luser.Position;
            leftName = luser.FullName;
        }

        if (middleActorUserId is { } mid && latestSignature.TryGetValue(mid, out var mrow))
        {
            middleBytes = await ReadAsync(mrow.StoragePath, mrow.MimeType);
            if (middleBytes is not null) middleUrl = $"/attachments/{mrow.AttachmentId}/download";
        }
        if (middleActorUserId is { } mid2 && userById.TryGetValue(mid2, out var muser))
        {
            middlePosition = muser.Position;
            middleName = muser.FullName;
        }

        if (latestStamp is not null)
        {
            stampBytes = await ReadAsync(latestStamp.StoragePath, latestStamp.MimeType);
            if (stampBytes is not null) stampUrl = $"/attachments/{latestStamp.AttachmentId}/download";
        }

        return new PaperSignatures(
            LeftUrl: leftUrl, MiddleUrl: middleUrl, StampUrl: stampUrl,
            LeftPosition: leftPosition, MiddlePosition: middlePosition,
            LeftName: leftName, MiddleName: middleName,
            StampOnMiddle: stampOnMiddle,
            LeftBytes: leftBytes, MiddleBytes: middleBytes, StampBytes: stampBytes);
    }
}
