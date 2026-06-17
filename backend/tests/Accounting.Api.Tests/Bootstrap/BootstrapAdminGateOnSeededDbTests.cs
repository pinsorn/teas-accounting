using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Bootstrap;

/// <summary>
/// The bootstrap-admin gate against the SHARED, already-seeded teas_test (which has the demo 'admin' +
/// other users). Independently of the ephemeral end-to-end test, this proves that on ANY non-empty
/// system the anonymous endpoint refuses with 409 — the single invariant that keeps it safe.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BootstrapAdminGateOnSeededDbTests
{
    private readonly PostgresFixture _fx;
    public BootstrapAdminGateOnSeededDbTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Refuses_with_409_when_users_already_exist()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var resp = await client.PostAsJsonAsync("/system/setup/bootstrap-admin",
            new { username = "intruder", password = "Sh0uldNeverWork!1", email = (string?)null, fullName = "Nope" });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "teas_test already has users (demo seeded) → the first-run bootstrap is closed");
    }

    [SkippableFact]
    public async Task Rejects_weak_password_before_touching_the_gate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var resp = await client.PostAsJsonAsync("/system/setup/bootstrap-admin",
            new { username = "owner", password = "short", email = (string?)null, fullName = "Owner" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "password < 12 chars must be rejected with a 400 validation error");
    }
}
