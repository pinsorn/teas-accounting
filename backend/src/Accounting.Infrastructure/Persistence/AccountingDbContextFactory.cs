using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Accounting.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c>. Bypasses the API host (and its
/// startup-time DbInitializer / DB connection) so migrations can be generated
/// offline. The connection string is only used for provider/SQL generation,
/// not opened during `migrations add`.
/// </summary>
public sealed class AccountingDbContextFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("TEAS_TEST_PG")
            ?? "Host=localhost;Port=5432;Database=accounting_dev;Username=accounting;Password=accounting_dev_password";

        var options = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(conn, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations", "sys");
                npg.MigrationsAssembly(typeof(AccountingDbContext).Assembly.FullName);
            })
            .UseSnakeCaseNamingConvention()
            .Options;

        // tenant == null → migration-time context bypasses the tenant query filter.
        return new AccountingDbContext(options, tenant: null);
    }
}
