using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using Accounting.Infrastructure.ETax;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.ETax;

/// <summary>
/// Sprint 13c — pure-logic units (no DB): recipient redirect/whitelist,
/// backoff schedule, XSD validator graceful-skip + valid/invalid, Mock RD
/// shape, RD HTTP skeleton instantiation. (In Api.Tests because the types live
/// in Accounting.Infrastructure — Domain.Tests references Domain only.)
/// </summary>
public sealed class ETaxRecipientResolverTests
{
    [Fact]
    public void Redirect_set_diverts_both_to_and_cc()
    {
        var r = ETaxRecipientResolver.Resolve("cust@acme.co", "csemail@rd.go.th", "dev@localhost");
        r.To.Should().Be("dev@localhost");
        r.Cc.Should().Be("dev@localhost");
        r.Redirected.Should().BeTrue();
    }

    [Fact]
    public void Redirect_null_sends_to_real_customer_and_rd()
    {
        var r = ETaxRecipientResolver.Resolve("cust@acme.co", "csemail@rd.go.th", null);
        r.To.Should().Be("cust@acme.co");
        r.Cc.Should().Be("csemail@rd.go.th");
        r.Redirected.Should().BeFalse();
    }

    [Fact]
    public void Whitelist_allows_approved_domain_and_rejects_others()
    {
        string[] wl = ["company.com"];
        ETaxRecipientResolver.IsWhitelisted("a@company.com", wl).Should().BeTrue();
        ETaxRecipientResolver.IsWhitelisted("a@evil.com", wl).Should().BeFalse();
        ETaxRecipientResolver.IsWhitelisted("a@evil.com", null).Should().BeTrue();   // no list = open

        var act = () => ETaxRecipientResolver.EnsureWhitelisted("a@evil.com", wl);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("etax.email.whitelist_violation");
    }
}

public sealed class ETaxBackoffTests
{
    private static readonly string[] Sched = ["1m", "5m", "15m", "1h", "4h", "24h"];

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 300)]
    [InlineData(6, 86400)]
    public void NextDelay_maps_attempt_to_schedule(int attempt, int expectSeconds)
        => ETaxBackoff.NextDelay(attempt, Sched)!.Value
            .Should().Be(TimeSpan.FromSeconds(expectSeconds));

    [Fact]
    public void NextDelay_returns_null_when_schedule_exhausted()
        => ETaxBackoff.NextDelay(7, Sched).Should().BeNull();   // → dead-letter

    [Fact]
    public void ParseToken_units()
    {
        ETaxBackoff.ParseToken("90s").Should().Be(TimeSpan.FromSeconds(90));
        ETaxBackoff.ParseToken("2h").Should().Be(TimeSpan.FromHours(2));
        ETaxBackoff.ParseToken("1d").Should().Be(TimeSpan.FromDays(1));
    }
}

public sealed class LocalXsdValidatorTests
{
    private static LocalXsdValidator Validator(string dir) =>
        new(Options.Create(new ETaxValidationOptions { XsdSchemaDir = dir }));

    [Fact]
    public async Task Empty_schema_dir_is_graceful_skip()
    {
        var v = Validator(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid()));
        var r = await v.ValidateAsync("<anything/>", default);
        r.IsValid.Should().BeTrue();
        r.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Valid_and_invalid_xml_against_a_loaded_schema()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xsd-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "t.xsd"), """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="Inv">
                <xs:complexType><xs:sequence>
                  <xs:element name="No" type="xs:string"/>
                </xs:sequence></xs:complexType>
              </xs:element>
            </xs:schema>
            """);
        var v = Validator(dir);

        (await v.ValidateAsync("<Inv><No>A1</No></Inv>", default)).IsValid.Should().BeTrue();

        var bad = await v.ValidateAsync("<Inv><Wrong>x</Wrong></Inv>", default);
        bad.IsValid.Should().BeFalse();
        bad.Errors.Should().NotBeEmpty();

        Directory.Delete(dir, true);
    }
}

public sealed class MockRdEfilingClientTests
{
    [Fact]
    public async Task SubmitPnd30_returns_accepted_shape()
    {
        var r = await new MockRdEfilingClient().SubmitPnd30Async(1, 202605, [1], default);
        r.Submitted.Should().BeTrue();
        r.HttpStatusCode.Should().Be(200);
        r.SubmissionId.Should().StartWith("MOCK-PND30-1-202605-");
        r.AckReference.Should().StartWith("ACK-MOCK-PND30-");
    }

    [Fact]
    public async Task GetStatus_acknowledges_any_mock_id()
        => (await new MockRdEfilingClient().GetStatusAsync("MOCK-X", default))
            .Status.Should().Be("Acknowledged");

    [Fact]
    public void RdHttp_skeleton_constructs_from_config_only()
    {
        var c = new RdHttpEfilingClient(new HttpClient(),
            Options.Create(new RdApiOptions { Provider = "RdUat", BaseUrl = "http://localhost:1080" }));
        c.Should().NotBeNull();   // no RD call — credentials are Phase 0
    }
}
