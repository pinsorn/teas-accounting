using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Accounting.Application.Abstractions;

namespace Accounting.Api.Endpoints;

/// <summary>
/// Shared constants for the git-ignored first-run secrets file. The file name is the
/// single source of truth so Program.cs (which loads it) and the setup endpoint (which
/// writes it) can never drift to different paths — a drift would silently make the
/// written values never take effect.
/// </summary>
public static class InstanceSecrets
{
    public const string FileName = "appsettings.Secrets.json";

    /// <summary>
    /// Absolute path of the secrets file. Resolves against ContentRootPath — the SAME base
    /// <c>AddJsonFile(FileName, …)</c> uses — so the write target equals the read source.
    /// Overridable via config key <c>InstanceSecrets:Path</c> (tests point it at a temp dir
    /// so they never write into the source tree).
    /// </summary>
    public static string ResolvePath(IConfiguration cfg, IHostEnvironment env)
    {
        var overridePath = cfg["InstanceSecrets:Path"];
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        return Path.Combine(env.ContentRootPath, FileName);
    }
}

/// <summary>
/// First-run instance setup (security-critical). A brand-new super-admin (companyId==0,
/// possibly no permission claims) sets the instance MFA AES key + JWT access-token lifetime
/// during onboarding. These are written to the git-ignored <see cref="InstanceSecrets.FileName"/>
/// — NEVER a committed appsettings file, NEVER logged. reloadOnChange + IOptionsMonitor make
/// them take effect live (no API restart).
///
/// Authorization: explicit IsSuperAdmin claim check (NOT a permission policy — at first-run the
/// super-admin carries no permission claims, so a permission gate would wrongly 403). Mirrors the
/// defence-in-depth handler check in AuthEndpoints switch-company.
/// </summary>
public static class InstanceSetupEndpoints
{
    /// <summary>Request body. mfaAesKeyBase64 must decode to exactly 32 bytes (AES-256);
    /// jwtAccessTokenMinutes in [5,1440].</summary>
    public sealed record SetupRequest(string MfaAesKeyBase64, int JwtAccessTokenMinutes);

    private const int MinMinutes = 5;
    private const int MaxMinutes = 1440;

    public static IEndpointRouteBuilder MapInstanceSetupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/system/setup/instance-keys", async (
            SetupRequest req,
            ITenantContext tenant,
            IConfiguration cfg,
            IHostEnvironment env,
            ILoggerFactory logFactory,
            CancellationToken ct) =>
        {
            var log = logFactory.CreateLogger("InstanceSetup");

            // --- Authz: super-admin only (first-run; companyId may be 0, no perm claims). ---
            if (!tenant.IsSuperAdmin)
            {
                return Results.Problem(
                    title: "forbidden",
                    detail: "Only a super-admin may configure instance keys.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // --- Validate JWT lifetime. ---
            if (req.JwtAccessTokenMinutes < MinMinutes || req.JwtAccessTokenMinutes > MaxMinutes)
            {
                return Results.Problem(
                    title: "validation",
                    detail: $"jwtAccessTokenMinutes must be between {MinMinutes} and {MaxMinutes}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // --- Validate MFA key: base64 → exactly 32 bytes (AES-256). NEVER log the value. ---
            byte[] keyBytes;
            try
            {
                keyBytes = Convert.FromBase64String(req.MfaAesKeyBase64 ?? "");
            }
            catch (FormatException)
            {
                return Results.Problem(
                    title: "validation",
                    detail: "mfaAesKeyBase64 is not valid base64.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            if (keyBytes.Length != 32)
            {
                CryptographicOperations.ZeroMemory(keyBytes);
                return Results.Problem(
                    title: "validation",
                    detail: "mfaAesKeyBase64 must decode to exactly 32 bytes (AES-256 key).",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            CryptographicOperations.ZeroMemory(keyBytes);

            var path = InstanceSecrets.ResolvePath(cfg, env);

            // --- Read existing secrets (may already hold other secrets). Read-modify-write so we
            //     never clobber a future SigningKey etc. MFA key is DESTRUCTIVE to rotate (would
            //     orphan every enrolled user's encrypted secret) → refuse to overwrite a non-empty
            //     existing MFA key. This is what "first-run only" means for the MFA key. ---
            JsonObject root;
            try
            {
                if (File.Exists(path))
                {
                    var existingText = await File.ReadAllTextAsync(path, ct);
                    root = string.IsNullOrWhiteSpace(existingText)
                        ? new JsonObject()
                        : JsonNode.Parse(existingText) as JsonObject ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }
            }
            catch (JsonException)
            {
                return Results.Problem(
                    title: "secrets_corrupt",
                    detail: $"{InstanceSecrets.FileName} exists but is not valid JSON; resolve manually.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var mfa = root["Mfa"] as JsonObject ?? new JsonObject();
            var existingMfaKey = mfa["MfaAesKeyBase64"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(existingMfaKey))
            {
                return Results.Problem(
                    title: "already_configured",
                    detail: "The instance MFA key is already set. Rotating it would orphan every "
                          + "enrolled user's TOTP secret; rotation is a deliberate, separate operation.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            // --- Merge + write (temp-file-then-rename so the file watcher never reads a partial). ---
            mfa["MfaAesKeyBase64"] = req.MfaAesKeyBase64;
            root["Mfa"] = mfa;

            var jwt = root["Jwt"] as JsonObject ?? new JsonObject();
            jwt["AccessTokenMinutes"] = req.JwtAccessTokenMinutes;
            root["Jwt"] = jwt;

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, path, overwrite: true);

            // Structured log — record THAT setup ran, never the key value.
            log.LogInformation(
                "Instance keys configured by super-admin user {UserId}: MFA key set (32 bytes), "
                + "JWT access-token lifetime = {Minutes} min.",
                tenant.UserId, req.JwtAccessTokenMinutes);

            return Results.Ok(new { ok = true, jwtAccessTokenMinutes = req.JwtAccessTokenMinutes });
        })
        .RequireAuthorization()   // authenticated; super-admin enforced in handler (first-run safe)
        .WithTags("System");

        return app;
    }
}
