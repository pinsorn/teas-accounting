using System.Text.RegularExpressions;
using Accounting.Application.Abstractions;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Endpoints;

/// <summary>
/// First-run super-admin bootstrap (SECURITY-CRITICAL). On a fresh install no <c>admin</c> user is
/// seeded (Database:SeedDemoData=false), so the very first super-admin must be created here, anonymously,
/// during onboarding — there is nobody to authenticate as yet.
///
/// THE GATE: this endpoint succeeds ONLY when the system has ZERO users. The instant any user exists it
/// refuses with 409. That single invariant is what makes an anonymous create-admin endpoint safe: it can
/// NEVER be used to mint an admin on a live system. The check + insert run inside one transaction guarded
/// by a Postgres transaction-level advisory lock, so two simultaneous first-run requests cannot both pass
/// the "zero users" test (TOCTOU). Mirrors the defensive style of <see cref="InstanceSetupEndpoints"/>
/// (the instance-keys first-run endpoint).
///
/// The created user is a GLOBAL super-admin with NO user_roles row → its JWT carries companyId=0, the
/// "no company yet" signal the dashboard layout uses to send it to /onboarding to create the first
/// company (mirrors seed 562). Password is BCrypt-hashed via the same <see cref="IPasswordHasher"/> the
/// login path verifies against.
/// </summary>
public static class BootstrapAdminEndpoints
{
    /// <summary>Request body. username 3–64 chars; password ≥ 12 chars (matches the seeded admin strength).</summary>
    public sealed record BootstrapAdminRequest(string Username, string Password, string? Email, string? FullName);

    // 64-bit constant key for pg_advisory_xact_lock — any fixed value unique to this operation.
    private const long BootstrapLockKey = 0x7EA5_B007_57A2_7000L; // "TEAS BOOT STRAP"-ish

    private const int MinUsernameLen = 3;
    private const int MaxUsernameLen = 64;
    private const int MinPasswordLen = 12;

    public static IEndpointRouteBuilder MapBootstrapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/system/setup/bootstrap-admin", async (
            BootstrapAdminRequest req,
            AccountingDbContext db,
            IPasswordHasher hasher,
            ILoggerFactory logFactory,
            CancellationToken ct) =>
        {
            var log = logFactory.CreateLogger("BootstrapAdmin");

            // --- Validate input (NEVER log the password). ---
            var username = (req.Username ?? "").Trim();
            if (username.Length < MinUsernameLen || username.Length > MaxUsernameLen
                || !Regex.IsMatch(username, "^[A-Za-z0-9_.-]+$"))
            {
                return Results.Problem(
                    title: "validation",
                    detail: $"username must be {MinUsernameLen}-{MaxUsernameLen} chars (letters, digits, . _ -).",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            if ((req.Password ?? "").Length < MinPasswordLen)
            {
                return Results.Problem(
                    title: "validation",
                    detail: $"password must be at least {MinPasswordLen} characters.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
            var email = string.IsNullOrWhiteSpace(req.Email) ? $"{username}@teas.local" : req.Email.Trim();
            var fullName = string.IsNullOrWhiteSpace(req.FullName) ? "System Administrator" : req.FullName.Trim();

            // --- Gate + insert in ONE transaction under an advisory lock (TOCTOU-safe). ---
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Serialise concurrent first-run attempts: the lock is held until COMMIT/ROLLBACK.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock({0})", [BootstrapLockKey], ct);

            // THE GATE: zero users total. Any existing user (super or not) ⇒ refuse. This is what
            // prevents the anonymous endpoint from ever minting an admin on a live system.
            var anyUser = await db.Users.IgnoreQueryFilters().AnyAsync(ct);
            if (anyUser)
            {
                return Results.Problem(
                    title: "already_initialised",
                    detail: "The system already has at least one user; the first-run bootstrap is closed. "
                          + "Create further users via the user-management UI as an existing admin.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var now = DateTimeOffset.UtcNow;
            var user = new User
            {
                Username = username,
                Email = email,
                PasswordHash = hasher.Hash(req.Password!),
                FullName = fullName,
                IsSuperAdmin = true,   // first user is the instance super-admin
                IsActive = true,
                FailedLoginCount = 0,
                MustChangePassword = false,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 0,
                // Deliberately NO UserRole rows → JWT companyId=0 → onboarding create-company step (seed 562 pattern).
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // Structured log — record THAT bootstrap ran + the username, NEVER the password.
            log.LogInformation(
                "First-run super-admin bootstrapped: userId={UserId} username={Username}.",
                user.UserId, user.Username);

            return Results.Ok(new { ok = true, userId = user.UserId, username = user.Username });
        })
        .AllowAnonymous()   // first-run: there is no user to authenticate as. The zero-users gate is the guard.
        .WithTags("System");

        // Anonymous first-run probe. The frontend login page calls this to decide where to send a
        // visitor with no session: needs_setup=true (ZERO users → fresh install) routes to the
        // /onboarding wizard; false routes to /login. Leaks a single boolean (no data, no user info),
        // safe to expose pre-auth — and it is the same zero-users invariant the bootstrap gate enforces.
        app.MapGet("/system/setup/status", async (AccountingDbContext db, CancellationToken ct) =>
        {
            var anyUser = await db.Users.IgnoreQueryFilters().AnyAsync(ct);
            return Results.Ok(new { needs_setup = !anyUser });
        })
        .AllowAnonymous()
        .WithTags("System");

        return app;
    }
}
