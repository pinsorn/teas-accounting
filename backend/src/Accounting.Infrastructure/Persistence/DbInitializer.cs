using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// 2. Applies the <c>*.sql</c> under <c>Infrastructure/Migrations/SqlScripts</c> in lexical
///    order so RLS policies, triggers, audit append-only constraints, and SYSTEM/reference seed
///    data are present.
/// 3. Records the applied scripts in <c>sys.applied_sql_scripts</c> to keep runs idempotent.
///
/// SYSTEM vs DEMO split (first-run-bootstrap spec, 2026-06-17):
///   A brand-new clone must seed NO placeholder data — no demo companies, no seeded
///   <c>admin</c> user. The super-admin is created during onboarding
///   (<c>POST /system/setup/bootstrap-admin</c>), then the first company via the onboarding
///   wizard. Therefore the scripts are partitioned into two sets:
///     • SYSTEM (always applied): RBAC roles/permissions/templates, RLS, triggers, append-only
///       constraints, document prefixes, system tax-codes and reference data the app needs to run.
///     • DEMO (applied only when <c>Database:SeedDemoData=true</c>): the <c>admin</c>/approver/e2e
///       users and the demo companies (co1 manual demo, co2 VAT, co3 non-VAT) + their data.
///   The DEMO set is the explicit <see cref="DemoScripts"/> allowlist below. The default
///   (committed <c>appsettings.json</c> → prod / fresh clone) is <c>false</c>;
///   <c>appsettings.Development.json</c> and the integration-test fixture set it <c>true</c> so
///   local dev keeps the <c>admin</c> login and the test suite keeps companies 1/2/3.
/// </summary>
public static class DbInitializer
{
    /// <summary>
    /// SQL scripts whose PRIMARY purpose is placeholder/demo data (demo users + demo companies
    /// and the data hanging off them). Applied ONLY when <c>Database:SeedDemoData=true</c>.
    /// Kept here (not by folder/convention) so the split is a single auditable list that both
    /// <see cref="DbInitializer"/> and the test <c>PostgresFixture</c> share verbatim.
    ///
    /// Everything NOT in this set is SYSTEM/reference and always applies. The reconcile scripts
    /// 530 (rbac grant reconcile) and 560 (identity-sequence fix) reference company 1 but are
    /// idempotent no-ops when it is absent (SELECT…JOIN / MAX), so they stay SYSTEM safely.
    /// </summary>
    public static readonly IReadOnlySet<string> DemoScripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "120_seed_demo_company.sql",                 // demo company 2 + minimal CoA
        "130_seed_admin_and_customer.sql",           // the seeded 'admin'/Admin@1234 + demo customer (co1)
        "160_seed_approver_user.sql",                // SoD approver (super-admin, co1)
        "181_seed_demo_pv_users.sql",                // ap_clerk / sales_staff demo users (split out of 180)
        "400_seed_manual_demo_company.sql",          // manual-demo company (co1 tenant data)
        "410_seed_manual_demo_company_profile.sql",
        "420_seed_company1_profile.sql",             // company_id=1 profile (demo admin tenant)
        "440_seed_nonvat_demo_company.sql",          // demo company 3 (non-VAT)
        "450_seed_demo_company_tax_codes.sql",       // tax codes for demo cos
        "490_seed_company1_structured_address.sql",  // company_id=1 structured address (demo)
        "550_seed_rbac_e2e_users.sql",               // one login per role (co1/co3) — e2e only
        "561_seed_manual_demo_employees.sql",        // payroll demo employees (co1)
        "562_seed_onboarding_setup_admin.sql",       // 'setup-admin' no-company super-admin (wizard walkthrough)
    };

    public static async Task InitializeAsync(IServiceProvider sp, CancellationToken ct = default)
    {
        await using var scope = sp.CreateAsyncScope();
        var db  = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
                      .CreateLogger("DbInitializer");

        // Default FALSE: a fresh clone / prod seeds NO placeholder data. Dev + tests opt in.
        var seedDemo = cfg.GetValue("Database:SeedDemoData", false);
        log.LogInformation("DbInitializer: SeedDemoData={SeedDemo}", seedDemo);

        // Extensions must exist before migrations in case a future migration
        // references pgcrypto/uuid-ossp. Schemas are handled by EF EnsureSchema.
        await db.Database.ExecuteSqlRawAsync(
            "CREATE EXTENSION IF NOT EXISTS pgcrypto; CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";", ct);

        log.LogInformation("DbInitializer: applying EF migrations");
        await db.Database.MigrateAsync(ct);

        await EnsureLedgerTableAsync(db, ct);
        await ApplyScriptsAsync(db, log, seedDemo, ct);
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

    private static async Task ApplyScriptsAsync(AccountingDbContext db, ILogger log, bool seedDemo, CancellationToken ct)
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
            // Demo/placeholder seeds (admin user, demo companies + their data) apply ONLY when
            // Database:SeedDemoData=true. On a fresh clone/prod this stays false → no admin, no co2/co3.
            // NOTE: skipped demo scripts are intentionally NOT recorded in sys.applied_sql_scripts, so
            // flipping the flag true later will apply them on the next startup.
            if (!seedDemo && DemoScripts.Contains(name))
            {
                log.LogInformation("Skip DEMO script {Script} — SeedDemoData=false.", name);
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
