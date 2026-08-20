using Accounting.Api.Endpoints;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Domain.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Endpoints;

/// <summary>
/// specs/fix-postfix-review-2026-08-20.md — Finding 1. <c>ReasonBody</c> (Quotation
/// reject/cancel, Billing Note cancel, Purchase Order cancel) had no validator plumbing:
/// its records live in Accounting.Api, out of reach of the Application-assembly
/// FluentValidation scan (<c>AddValidatorsFromAssembly</c>). A 501-char reason reached the
/// DB raw and surfaced as a raw 500 (Npgsql 22001); a whitespace-only reason was silently
/// accepted. <see cref="SalesChainEndpoints.RequireReason"/> is the shared guard every
/// ReasonBody-consuming endpoint now calls BEFORE the service layer — pure, no DB, so this
/// is a plain xunit fact (no PostgresFixture needed).
/// </summary>
public sealed class ReasonValidationTests
{
    [Fact]
    public void Reason_over_500_chars_throws_typed_DomainException_not_a_raw_db_error()
    {
        var tooLong = new string('a', 501);
        var act = () => SalesChainEndpoints.RequireReason(tooLong);
        act.Should().Throw<DomainException>()
            .Which.Message.Should().NotContain("22001", "the caller must never see the raw Npgsql/DB error text");
    }

    [Fact]
    public void Whitespace_only_reason_throws_typed_DomainException()
    {
        var act = () => SalesChainEndpoints.RequireReason("   ");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Null_reason_throws_typed_DomainException()
    {
        var act = () => SalesChainEndpoints.RequireReason(null);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Reason_exactly_500_chars_is_accepted_and_returned_trimmed()
    {
        var exact500 = new string('b', 500);
        var result = SalesChainEndpoints.RequireReason($"  {exact500}  ");
        result.Should().Be(exact500);
        result.Length.Should().Be(500);
    }

    [Fact]
    public void Reason_with_surrounding_whitespace_is_trimmed_before_persist()
    {
        var result = SalesChainEndpoints.RequireReason("  a real reason  ");
        result.Should().Be("a real reason");
    }
}

/// <summary>
/// specs/fix-postfix-review-2026-08-20.md — item 3. Program.cs's <c>GET /system/info</c>
/// handler now catches <see cref="InvalidOperationException"/> around
/// <c>ICompanyTaxConfigService.GetAsync</c> so a companyId=0 super-admin (the onboarding
/// wizard — the ONE page that most needs the version) gets a version-only response instead
/// of a raw 500. This test proves the PREMISE the catch clause depends on:
/// <c>CompanyTaxConfigService</c> really does throw exactly
/// <see cref="InvalidOperationException"/> (not some other type the catch would miss) when
/// the tenant's company id resolves to no row — collocated here, not a new file, to stay
/// within the spec's blast-radius cap. No live onboarding-flow browser check was done for
/// this half of item 3 (it needs a genuine zero-company super-admin session, which would
/// mean creating a new super-admin account in the shared dev DB — judged disproportionate
/// for this task); this DB-backed test plus the unauthenticated-phase screenshot (no
/// regression) are the verification for the backend half.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SystemInfoResilienceTests
{
    private readonly PostgresFixture _fx;
    public SystemInfoResilienceTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task GetAsync_for_an_unresolvable_company_throws_InvalidOperationException()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // companyId 0 never matches a real company row (ids start at 1) — exactly the
        // onboarding super-admin's tenant scope before they create their first company.
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 0, branchId: 0);
        await using var scope = sp.CreateAsyncScope();
        var taxCfg = scope.ServiceProvider.GetRequiredService<ICompanyTaxConfigService>();

        var act = async () => await taxCfg.GetAsync(default);
        await act.Should().ThrowAsync<InvalidOperationException>(
            "Program.cs's /system/info handler only catches this exact exception type");
    }
}
