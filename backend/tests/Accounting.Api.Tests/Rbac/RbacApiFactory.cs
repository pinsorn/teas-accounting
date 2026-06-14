using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Accounting.Api.Tests.Rbac;

/// <summary>
/// Sprint 13k Plan 2 — boots the real API (full middleware pipeline, real
/// PermissionPolicyProvider + PermissionHandler) against the shared teas_test DB
/// so the Cartesian RBAC test exercises end-to-end HTTP authorization (genuine
/// 401/403), not a reflected approximation.
///
/// Config overrides (in-memory = highest precedence):
///   • ConnectionStrings:Postgres → teas_test (already migrated+seeded by the fixture)
///   • Database:RunInitializerOnStartup=false → no double bootstrap
///   • Jwt:* → fixed test issuer/audience/key; the test mints tokens with the
///     SAME values via <see cref="Accounting.Infrastructure.Identity.JwtTokenIssuer"/>,
///     so a *denied* request returns 403 (authenticated-but-forbidden), never 401.
/// </summary>
public sealed class RbacApiFactory : WebApplicationFactory<Program>
{
    public const string JwtIssuer    = "teas-rbac-cartesian-issuer";
    public const string JwtAudience  = "teas-rbac-cartesian-audience";
    // 49 bytes — comfortably over the 32-byte (256-bit) HMAC-SHA256 minimum.
    public const string JwtSigningKey = "teas-rbac-cartesian-signing-key-0123456789-ABCDEF";

    private readonly string _connectionString;

    public RbacApiFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development → loads appsettings.Development.json (GlAccounts, Mfa, ETax mock,
        // RdApi mock, FileStorage) so the composition root starts cleanly. We then
        // override only the handful of keys the test must own.
        //
        // CRITICAL: use UseSetting (host configuration), NOT ConfigureAppConfiguration.
        // In minimal hosting the top-level Program reads builder.Configuration eagerly
        // (Jwt section + AddInfrastructure's ConnectionStrings:Postgres) BEFORE app-level
        // ConfigureAppConfiguration callbacks run, so those overrides arrive too late and
        // the app would bind appsettings.Development (accounting_dev + dev Jwt key) → 401.
        // UseSetting values are part of the host config WebApplicationBuilder seeds from,
        // so they are visible at top-level read time.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
        builder.UseSetting("Database:RunInitializerOnStartup", "false");
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        builder.UseSetting("Jwt:SigningKey", JwtSigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "60");
    }
}
