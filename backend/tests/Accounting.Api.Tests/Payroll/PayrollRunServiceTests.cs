using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Payroll;

/// <summary>
/// Payroll P-C — DB-backed run lifecycle. A run aggregates EVERY company-1 employee active in the
/// period, so assertions target the specific employee a test created (by EmployeeId) + the run-level
/// invariant that the GL JV balances — both robust to other tests' employees on the shared DB.
/// Pure PIT / SSO / allowance math is covered by the Domain golden tests.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PayrollRunServiceTests
{
    private readonly PostgresFixture _fx;
    public PayrollRunServiceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private static string Nid() => "1" + Random.Shared.NextInt64(0, 999_999_999_999L).ToString("000000000000");
    // Distinct far-future YEAR per test — the shared fixture persists runs (unique per company+period).
    private static int RandYear() => 3000 + Random.Shared.Next(0, 6000);
    private static string Period(int year, int month) => $"{year:0000}{month:00}";

    private static async Task<long> AddEmployee(
        ServiceProvider sp, decimal salary, MaritalStatus marital = MaritalStatus.Single,
        bool spouseHasIncome = false, int children = 0, bool sso = true, bool isActive = true)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var e = new Employee
        {
            CompanyId = 1, EmployeeCode = "EMP-" + Sfx(),
            FirstNameTh = "ทดสอบ", LastNameTh = Sfx(), NationalId = Nid(),
            HireDate = new DateOnly(2020, 1, 1), BaseSalary = salary,
            SsoApplicable = sso, MaritalStatus = marital,
            SpouseHasIncome = spouseHasIncome, ChildrenCount = children,
            IsActive = isActive, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Employees.Add(e);
        await db.SaveChangesAsync(default);
        return e.EmployeeId;
    }

    private static async Task<long> RunThroughPost(ServiceProvider sp, string period)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var id = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(int.Parse(period[..4]), int.Parse(period[4..]), 28), null),
            default);
        await svc.ApproveAsync(id, default);
        await svc.PostAsync(id, default);
        return id;
    }

    [SkippableFact]
    public async Task Full_run_computes_pit_sso_and_posts_a_balanced_gl()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = Period(RandYear(), 1);   // January → 12 months remaining

        var e50k = await AddEmployee(sp, 50_000m);                       // pit 1,716.67 · sso 750
        var e10k = await AddEmployee(sp, 10_000m);                       // pit 0 · sso 500 (below ceiling)
        var e80k = await AddEmployee(sp, 80_000m);                       // pit 6,100 · sso 750 (ceiling clamp)

        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var run = (await svc.GetAsync(runId, default))!;
        run.Status.Should().Be("POSTED");
        run.DocNo.Should().NotBeNullOrEmpty();

        var s50 = run.Payslips.Single(p => p.EmployeeId == e50k);
        s50.PitWithheld.Should().Be(1_716.67m);   // matches the ม.50(1) golden figure
        s50.SsoEmployee.Should().Be(750m);
        s50.SsoEmployer.Should().Be(750m);
        s50.NetPay.Should().Be(50_000m - 1_716.67m - 750m);

        var s10 = run.Payslips.Single(p => p.EmployeeId == e10k);
        s10.PitWithheld.Should().Be(0m);           // low earner → no tax
        s10.SsoEmployee.Should().Be(500m);         // 10,000 × 5% (under the ceiling)

        var s80 = run.Payslips.Single(p => p.EmployeeId == e80k);
        s80.PitWithheld.Should().Be(6_100m);
        s80.SsoEmployee.Should().Be(750m);         // clamped at the 15,000 ceiling

        // GL JV balances (whatever the full employee set is).
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.FirstAsync(j => j.JournalId == run.JournalId);
        je.TotalDebit.Should().Be(je.TotalCredit);
        je.TotalDebit.Should().BeGreaterThan(0m);
    }

    [SkippableFact]
    public async Task Posted_run_is_immutable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = Period(RandYear(), 1);
        await AddEmployee(sp, 30_000m);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();

        var reApprove = () => svc.ApproveAsync(runId, default);
        (await reApprove.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("payroll.not_draft");
        var del = () => svc.DeleteDraftAsync(runId, default);
        (await del.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("payroll.not_draft");
    }

    [SkippableFact]
    public async Task Duplicate_period_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = Period(RandYear(), 3);
        await AddEmployee(sp, 25_000m);
        await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var act = () => svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(int.Parse(period[..4]), 3, 28), null), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("payroll.duplicate_period");
    }

    [SkippableFact]
    public async Task Deactivated_employee_is_excluded_from_the_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = Period(RandYear(), 5);
        var active   = await AddEmployee(sp, 20_000m);
        var inactive = await AddEmployee(sp, 20_000m, isActive: false);   // soft-deactivated, no term date

        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var run = (await svc.GetAsync(runId, default))!;
        run.Payslips.Should().Contain(p => p.EmployeeId == active);
        run.Payslips.Should().NotContain(p => p.EmployeeId == inactive);   // IsActive gate (advisor #1)
    }

    [SkippableFact]
    public async Task Ytd_carries_so_constant_salary_withholds_evenly_across_two_months()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = RandYear();
        var empId = await AddEmployee(sp, 50_000m);   // the only employee we assert on (YTD is per-employee)

        // Month 1 (Jan): 12 months remaining → 1,716.67.
        var run1 = await RunThroughPost(sp, Period(year, 1));
        // Month 2 (Feb): YTD income 50,000 / YTD PIT 1,716.67 → 11 months remaining → same 1,716.67.
        var run2 = await RunThroughPost(sp, Period(year, 2));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var p1 = (await svc.GetAsync(run1, default))!.Payslips.Single(p => p.EmployeeId == empId);
        var p2 = (await svc.GetAsync(run2, default))!.Payslips.Single(p => p.EmployeeId == empId);

        p1.PitWithheld.Should().Be(1_716.67m);
        p2.PitWithheld.Should().Be(1_716.67m);            // even spread — YTD subtraction is correct
        p2.YtdIncome.Should().Be(100_000m);              // 2 × 50,000
        p2.YtdPit.Should().Be(3_433.34m);                // 2 × 1,716.67
    }
}
