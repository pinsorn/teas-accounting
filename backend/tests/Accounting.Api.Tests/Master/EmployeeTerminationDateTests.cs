using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Master;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Master;

/// <summary>
/// O9 (specs/fix-army-findings-2026-07-22.md) — the army leg found no termination-date field
/// anywhere in the employee UI and had to PUT it directly. The backend (CreateEmployeeRequest/
/// UpdateEmployeeRequest/Employee entity/EmployeeService) already carries TerminationDate end to
/// end — this only proves that round-trip so the new FE input (settings/employees/page.tsx) has a
/// regression guard, independent of the FE change itself.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class EmployeeTerminationDateTests(PostgresFixture fx)
{
    [SkippableFact]
    public async Task Create_without_termination_date_then_update_sets_and_clears_it()
    {
        Skip.If(fx.SkipReason is not null, fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(fx.ConnectionString, vatRegistered: true);

        await using var sp = TestCompanyFactory.BuildProvider(fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IEmployeeService>();
        var nationalId = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString();

        var id = await svc.CreateAsync(new CreateEmployeeRequest(
            $"EMP-{TestIds.Suffix()}", null, "ทดสอบ", "สิ้นสุดสัญญา", null, null, null,
            nationalId, null, null,
            new DateOnly(2024, 1, 1), TerminationDate: null, 20000m,
            null, null, null, false, null,
            "SINGLE", false, 0), default);

        var afterCreate = await svc.GetAsync(id, default);
        afterCreate!.TerminationDate.Should().BeNull();

        var termDate = new DateOnly(2026, 7, 10);
        await svc.UpdateAsync(id, new UpdateEmployeeRequest(
            null, "ทดสอบ", "สิ้นสุดสัญญา", null, null, null,
            nationalId, null, null,
            new DateOnly(2024, 1, 1), TerminationDate: termDate, 20000m,
            null, null, null, false, null,
            "SINGLE", false, 0, IsActive: true), default);

        var afterUpdate = await svc.GetAsync(id, default);
        afterUpdate!.TerminationDate.Should().Be(termDate);

        // Clearing it back to null (e.g. a data-entry correction) must also round-trip.
        await svc.UpdateAsync(id, new UpdateEmployeeRequest(
            null, "ทดสอบ", "สิ้นสุดสัญญา", null, null, null,
            nationalId, null, null,
            new DateOnly(2024, 1, 1), TerminationDate: null, 20000m,
            null, null, null, false, null,
            "SINGLE", false, 0, IsActive: true), default);

        var afterClear = await svc.GetAsync(id, default);
        afterClear!.TerminationDate.Should().BeNull();
    }
}
