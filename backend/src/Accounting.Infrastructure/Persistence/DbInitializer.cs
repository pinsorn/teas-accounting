using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// Bootstraps the database in environments where running a full <c>dotnet ef database update</c>
/// is not yet wired (dev, Testcontainers, smoke tests). The production deploy still relies on
/// EF Migrations — this helper bridges Phase 1 until the first migration has been generated.
///
/// Order:
/// 1. <c>MigrateAsync()</c> — applies EF migrations (schema from <c>Migrations/</c>).
/// 2. Applies every <c>*.sql</c> under <c>Infrastructure/Migrations/SqlScripts</c> in lexical
///    order so RLS policies, triggers, audit append-only constraints, and seed data are present.
/// 3. Records the applied scripts in <c>sys.applied_sql_scripts</c> to keep runs idempotent.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        await using var scope = sp.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                      .CreateLogger("DbInitializer");

        // Extensions must exist before migrations in case a future migration
        // references pgcrypto/uuid-ossp. Schemas are handled by EF EnsureSchema.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE EXTENSION IF NOT EXISTS pgcrypto; CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";", ct);

        log.LogInformation("DbInitializer: applying EF migrations");
        await db.Database.MigrateAsync(ct);

        await EnsureLedgerTableAsync(db, ct);
        await ApplyScriptsAsync(db, log, ct);
    }

    private static async Task EnsureLedgerTableAsync(AccountingDbContext db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA IF NOT EXISTS sys;
            CREATE TABLE IF NOT EXISTS sys.applied_sql_scripts (
                script_name TEXT PRIMARY KEY,
                applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            """, ct);
    }

    private static async Task ApplyScriptsAsync(AccountingDbContext db, ILogger log, CancellationToken ct)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Migrations", "SqlScripts");
        if (!Directory.Exists(dir))
        {
            log.LogWarning("SQL script dir not found at {Dir} — skipping triggers/RLS/seed.", dir);
            return;
        }

        var applied = (await db.Database
                .SqlQueryRaw<string>("SELECT script_name AS \"Value\" FROM sys.applied_sql_scripts")
                .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(dir, "*.sql").OrderBy(p => p))
        {
            var name = Path.GetFileName(path);
            if (applied.Contains(name))
            {
                log.LogDebug("Skip {Script} — already applied.", name);
                continue;
            }
            log.LogInformation("Apply SQL script: {Script}", name);
            var sql = await File.ReadAllTextAsync(path, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync(sql, ct);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO sys.applied_sql_scripts(script_name) VALUES ({0})",
                [name], ct);
            await tx.CommitAsync(ct);
        }
    }
}
