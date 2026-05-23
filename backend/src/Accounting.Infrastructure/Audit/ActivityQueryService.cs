using System.Text.Json;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Audit;

// Sprint 13j-FE D1 — resolves audit.activity_log entries for one document.
// Tenant-scoped (gotcha §26 belt-and-braces on top of the global query filter).
// Read-only; the append-only audit table is never mutated here.
// Sprint 13k §4.8 — from/to status + note are unpacked from MetadataJson (the
// shape ActivityRecorder writes); print rows (different metadata shape) simply
// yield nulls for those fields.
public sealed class ActivityQueryService(
    AccountingDbContext db, ITenantContext tenant) : IActivityQueryService
{
    public async Task<IReadOnlyList<ActivityEntryDto>> GetForDocumentAsync(
        string entityType, long id, CancellationToken ct)
    {
        var rows = await db.ActivityLogs.AsNoTracking()
            .Where(a => a.CompanyId == tenant.CompanyId
                && a.EntityType == entityType
                && a.EntityId == id)
            .OrderBy(a => a.ActivityAt)
            .Select(a => new { a.Username, a.ActivityType, a.ActivityAt, a.MetadataJson })
            .ToListAsync(ct);

        return rows.Select(a =>
        {
            var (from, to, note) = ParseMetadata(a.MetadataJson);
            return new ActivityEntryDto(
                a.Username ?? "system", a.ActivityType, from, to, a.ActivityAt, note);
        }).ToList();
    }

    private static (string? from, string? to, string? note) ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (Str(root, "fromStatus"), Str(root, "toStatus"), Str(root, "note"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }

        static string? Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString() : null;
    }
}
