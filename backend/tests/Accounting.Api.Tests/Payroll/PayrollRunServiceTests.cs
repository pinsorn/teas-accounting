using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Expense;
using Accounting.Application.Ledger;
using Accounting.Application.Master;
using Accounting.Application.Payroll;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Domain.Payroll;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Payroll;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;
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

    private ServiceProvider Provider(int companyId = 1, int branchId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
                // Pin the pre-2569 SSO statutory params: these tests verify the clamp /
                // allowance MATH with stable goldens, not the current statute (the live
                // value rides appsettings — ฿17,500 since 1 ม.ค. 2569, phased to 23,000).
                ["Payroll:Sso:Rate"] = "0.05",
                ["Payroll:Sso:WageFloor"] = "1650",
                ["Payroll:Sso:WageCeiling"] = "15000",
                ["Payroll:Sso:MaxAllowanceForPit"] = "9000",
            }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private static string Nid() => "1" + Random.Shared.NextInt64(0, 999_999_999_999L).ToString("000000000000");
    // Distinct far-future YEAR per test — the shared fixture persists runs (unique per company+period).
    private static int RandYear() => 3000 + Random.Shared.Next(0, 6000);
    private static string Period(int year, int month) => $"{year:0000}{month:00}";

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\f", document.GetPages().Select(p => p.Text));
    }

    // §8 isolation: the shared teas_test DB accumulates posted runs across historical sessions, so a
    // bare RandYear() can collide with a prior run of the same (company, period) — duplicate create OR
    // polluted YTD. Only a year with NO existing company-1 run is safe (YTD sums the whole year).
    private static async Task<int> FreshYearAsync(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        while (true)
        {
            var y = RandYear();
            var prefix = $"{y:0000}";
            if (!await db.PayrollRuns.AnyAsync(r => r.PeriodYearMonth.StartsWith(prefix), default))
                return y;
        }
    }

    private static async Task<int> FreshOpeningYearAsync(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        for (var year = 2100; year >= 2000; year--)
        {
            var prefix = year.ToString();
            if (!await db.PayrollRuns.AnyAsync(r => r.PeriodYearMonth.StartsWith(prefix), default))
                return year;
        }
        throw new InvalidOperationException("No unused opening-YTD test year remains.");
    }
    private static async Task<string> FreshPeriodAsync(ServiceProvider sp, int month) =>
        Period(await FreshYearAsync(sp), month);

    /// <summary>R1/C3 (WP-5) — PostAsync/PayAsync now guard on the period being OPEN (I21). Every
    /// period this class posts into is a synthetic (often far-future) year/month chosen ONLY to
    /// dodge payroll.duplicate_period collisions in the shared, never-reset teas_test DB — it was
    /// never meant to model "is this period genuinely open", so PeriodCloseService.IsOpenAsync's
    /// default-fallback rule (a missing row is open ONLY for the current real Bangkok month) now
    /// refuses every one of them. Direct-seed an Open row, mirroring
    /// FixedAssetServiceTests.OpenPeriodsAsync — the same fallback bites there for the same reason.
    /// No-ops if a row already exists (e.g. a prior test in the same period, or the real current
    /// month, which is open by default and needs no row at all).</summary>
    private static async Task OpenPeriodAsync(ServiceProvider sp, int year, int month)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var companyId = s.ServiceProvider.GetRequiredService<ITenantContext>().CompanyId;
        if (await db.AccountingPeriods.AnyAsync(p => p.Year == year && p.Month == (short)month))
            return;
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            CompanyId = companyId, Year = year, Month = (short)month, Status = PeriodStatus.Open,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<long> AddEmployee(
        ServiceProvider sp, decimal salary, MaritalStatus marital = MaritalStatus.Single,
        bool spouseHasIncome = false, int children = 0, bool sso = true, bool isActive = true,
        int? ytdOpeningYear = null, decimal ytdOpeningIncome = 0m,
        decimal ytdOpeningPit = 0m, decimal ytdOpeningSso = 0m,
        DateOnly? hireDate = null, DateOnly? terminationDate = null,
        // R2/WP-5 (H9) — override the default name to plant an unencodable/over-length name.
        string? firstNameTh = null, string? lastNameTh = null)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var companyId = s.ServiceProvider.GetRequiredService<ITenantContext>().CompanyId;
        var e = new Employee
        {
            CompanyId = companyId, EmployeeCode = "EMP-" + Sfx(),
            FirstNameTh = firstNameTh ?? "ทดสอบ", LastNameTh = lastNameTh ?? Sfx(), NationalId = Nid(),
            // The default must predate EVERY period a test can pick. FreshOpeningYearAsync/
            // FreshYearAsync scan 2100 DOWNWARD for a year with no payroll run, and each suite
            // run consumes one — on the shared, never-reset teas_test that pool has drifted
            // below 2020, so a 2020 default eventually fails PayrollRunService's
            // `HireDate <= periodEnd` guard with "No employees are active in this period".
            // A far-past default decouples the helper from that drift entirely; tests that
            // actually exercise hire-date behaviour pass hireDate explicitly.
            HireDate = hireDate ?? new DateOnly(1900, 1, 1), TerminationDate = terminationDate,
            BaseSalary = salary,
            SsoApplicable = sso, MaritalStatus = marital,
            SpouseHasIncome = spouseHasIncome, ChildrenCount = children,
            YtdOpeningYear = ytdOpeningYear, YtdOpeningIncome = ytdOpeningIncome,
            YtdOpeningPit = ytdOpeningPit, YtdOpeningSso = ytdOpeningSso,
            IsActive = isActive, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Employees.Add(e);
        await db.SaveChangesAsync(default);
        return e.EmployeeId;
    }

    private static async Task<long> RunThroughPost(ServiceProvider sp, string period, DateOnly? payDate = null)
    {
        var pd = payDate ?? new DateOnly(int.Parse(period[..4]), int.Parse(period[4..]), 28);
        await OpenPeriodAsync(sp, pd.Year, pd.Month);   // R1/C3 (WP-5) — PostAsync now guards on this
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var id = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, pd, null), default);
        await svc.ApproveAsync(id, default);
        await svc.PostAsync(id, default);
        return id;
    }

    [SkippableFact]
    public async Task Full_run_computes_pit_sso_and_posts_a_balanced_gl()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 1);   // January → 12 months remaining

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
        var je = await db.JournalEntries.Include(j => j.Lines).FirstAsync(j => j.JournalId == run.JournalId);
        Math.Round(je.Lines.Sum(l => l.DebitAmount), 2).Should()
            .Be(Math.Round(je.Lines.Sum(l => l.CreditAmount), 2));
        je.TotalDebit.Should().BeGreaterThan(0m);
        var otherAccount = await db.ChartOfAccounts.SingleAsync(a => a.AccountCode == "2180");
        je.Lines.Should().NotContain(l => l.AccountId == otherAccount.AccountId,
            "a zero deduction must not emit an empty credit line");
    }

    [SkippableFact]
    public async Task Deduction_changes_net_only_rolls_up_and_posts_balanced_credit_2180()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        // R2/WP-5 (H8) — this test calls sso.BuildMonthlyFileAsync on company 1 below, whose seeded
        // profile carries no SsoEmployerAccountNo anywhere in the SqlScripts seed. Without this, the
        // new H8 guard would fail this test for a reason unrelated to what it actually exercises
        // (deduction round-tripping through the SSO file byte-for-byte). Not a workaround for a bug —
        // company 1 genuinely has no employer account configured; a real company would set one.
        await SetSsoEmployerAccountAsync(sp, "1234567890");
        var year = await FreshYearAsync(sp);
        var period = Period(year, 4);
        var employeeId = await AddEmployee(sp, 50_000m);
        await OpenPeriodAsync(sp, year, 4);   // R1/C3 (WP-5) — PostAsync below now guards on this

        await using var s = sp.CreateAsyncScope();
        var payroll = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(year, 4, 28), null), default);
        var before = (await payroll.GetAsync(runId, default))!;
        var slipBefore = before.Payslips.Single(p => p.EmployeeId == employeeId);

        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();
        // R2/WP-4 (H13) — pnd1.BuildPnd1MonthlyAsync/sso.BuildMonthlyFileAsync now refuse a non-Posted
        // run, so the byte-identity "deductions don't change the filed value" comparison for THESE two
        // artifacts moved below, into the same post-then-mutate window the ภ.ง.ด.1ก check already uses
        // (this test still exercises deduction-setting pre-Approve via UpdateDeductionsAsync, which is
        // the real Draft-only product feature — only the RENDER comparison moved).

        await payroll.UpdateDeductionsAsync(runId,
            new UpdatePayrollDeductionsRequest(
                [new PayrollDeductionLine(employeeId, 500m, "เรียกคืนเงินจ่ายเกิน")]), default);
        var after = (await payroll.GetAsync(runId, default))!;
        var slipAfter = after.Payslips.Single(p => p.EmployeeId == employeeId);

        slipAfter.NetPay.Should().Be(slipBefore.NetPay - 500m);
        slipAfter.OtherDeductions.Should().Be(500m);
        slipAfter.OtherDeductionsReason.Should().Be("เรียกคืนเงินจ่ายเกิน");
        slipAfter.GrossTaxable.Should().Be(slipBefore.GrossTaxable);
        slipAfter.PitWithheld.Should().Be(slipBefore.PitWithheld);
        slipAfter.SsoEmployee.Should().Be(slipBefore.SsoEmployee);
        after.TotalOtherDeductions.Should().Be(before.TotalOtherDeductions + 500m);
        after.TotalNet.Should().Be(before.TotalNet - 500m);

        await payroll.UpdateDeductionsAsync(runId, new UpdatePayrollDeductionsRequest([]), default);
        var cleared = (await payroll.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == employeeId);
        cleared.OtherDeductions.Should().Be(0m);
        cleared.OtherDeductionsReason.Should().BeNull();
        await payroll.UpdateDeductionsAsync(runId,
            new UpdatePayrollDeductionsRequest(
                [new PayrollDeductionLine(employeeId, 500m, "เรียกคืนเงินจ่ายเกิน")]), default);

        await payroll.ApproveAsync(runId, default);
        await payroll.PostAsync(runId, default);
        var posted = (await payroll.GetAsync(runId, default))!;
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.JournalId == posted.JournalId);
        var account2180 = await db.ChartOfAccounts.AsNoTracking().SingleAsync(a => a.AccountCode == "2180");
        je.Lines.Should().ContainSingle(l => l.AccountId == account2180.AccountId)
            .Which.CreditAmount.Should().Be(500m);
        var debits = Math.Round(je.Lines.Sum(l => l.DebitAmount), 2);
        var credits = Math.Round(je.Lines.Sum(l => l.CreditAmount), 2);
        debits.Should().Be(credits,
            "gross + employer SSO debits must exactly equal PIT + both SSO legs + net + deductions credits");

        var payslipPdf = s.ServiceProvider.GetRequiredService<IPayslipPdfService>();
        // PdfText drops Thai combining marks ("อื่น" extracts as "อื น"), so assert on the reason —
        // which has none — rather than on the label. See troubles-wiki.md.
        PdfText(await payslipPdf.BuildAsync(runId, employeeId, default)).Should()
            .Contain("(เรียกคืนเงินจ่ายเกิน)");

        // R2/WP-4 (H13) — run is Posted now, so the monthly ภ.ง.ด.1 + สปส.1-10 file byte-identity
        // checks (moved from pre-Post above) join the existing ภ.ง.ด.1ก dance: snapshot at the CURRENT
        // (500-deduction) state, mutate the deduction to 0 directly via the DB (UpdateDeductionsAsync
        // is Draft-only and would throw payroll.not_draft on a Posted run), re-render, assert identical,
        // then restore — proving none of the three filings reflect NetPay/OtherDeductions.
        var pnd1MonthlyBeforeMutate = await pnd1.BuildPnd1MonthlyAsync(runId, default);
        var ssoBeforeMutate = (await sso.BuildMonthlyFileAsync(runId, default)).Content;
        var pnd1aBefore = await pnd1.BuildPnd1aAnnualAsync(year, default);
        var storedSlip = await db.Payslips.SingleAsync(p => p.PayrollRunId == runId && p.EmployeeId == employeeId);
        var storedRun = await db.PayrollRuns.SingleAsync(r => r.PayrollRunId == runId);
        storedSlip.OtherDeductions = 0m;
        storedSlip.ComputeNet();
        storedRun.RecalculateTotals();
        await db.SaveChangesAsync();
        PdfText(await pnd1.BuildPnd1MonthlyAsync(runId, default)).Should().Be(PdfText(pnd1MonthlyBeforeMutate),
            "deductions must not change any rendered ภ.ง.ด.1 filing value");
        (await sso.BuildMonthlyFileAsync(runId, default)).Content.Should().Equal(ssoBeforeMutate,
            "deductions must not change สปส.1-10 output");
        PdfText(await pnd1.BuildPnd1aAnnualAsync(year, default)).Should().Be(PdfText(pnd1aBefore),
            "deductions must not change any rendered ภ.ง.ด.1ก filing value");
        storedSlip.OtherDeductions = 500m;
        storedSlip.ComputeNet();
        storedRun.RecalculateTotals();
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Deduction_validator_enforces_positive_prorated_cap_and_draft_only()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var hireDate = new DateOnly(year, 3, 16);
        var employeeId = await AddEmployee(sp, 31_000m, hireDate: hireDate);
        await OpenPeriodAsync(sp, year, 3);   // R1/C3 (WP-5) — PostAsync below now guards on this

        await using var s = sp.CreateAsyncScope();
        var payroll = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 3), new DateOnly(year, 3, 28), null), default);
        var slip = (await payroll.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == employeeId);
        slip.GrossTaxable.Should().Be(16_000m, "the March cap must use 16/31 prorated gross");
        var maximum = slip.GrossTaxable + slip.GrossNonTaxable - slip.PitWithheld - slip.SsoEmployee;
        IValidator<UpdatePayrollDeductionsRequest> validator = new UpdatePayrollDeductionsValidator(payroll);

        var zero = new UpdatePayrollDeductionsRequest(
            [new PayrollDeductionLine(employeeId, 0m, "ทดสอบ")]) { PayrollRunId = runId };
        (await validator.ValidateAsync(zero)).IsValid.Should().BeFalse();
        var excessive = new UpdatePayrollDeductionsRequest(
            [new PayrollDeductionLine(employeeId, maximum + 0.01m, "ทดสอบ")]) { PayrollRunId = runId };
        var excessiveResult = await validator.ValidateAsync(excessive);
        excessiveResult.IsValid.Should().BeFalse();
        excessiveResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("ต้องไม่เกินเงินได้สุทธิ"));
        (await payroll.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == employeeId)
            .NetPay.Should().Be(maximum, "boundary rejection must not persist a negative net pay");

        var serviceExcessive = () => payroll.UpdateDeductionsAsync(runId, excessive, default);
        (await serviceExcessive.Should().ThrowAsync<DomainException>()).Which.Code
            .Should().Be("payroll.deduction_exceeds_net");
        var unknown = new UpdatePayrollDeductionsRequest(
            [new PayrollDeductionLine(long.MaxValue, 1m, "ทดสอบ")]) { PayrollRunId = runId };
        var unknownWrite = () => payroll.UpdateDeductionsAsync(runId, unknown, default);
        (await unknownWrite.Should().ThrowAsync<DomainException>()).Which.Code
            .Should().Be("payroll.deduction_employee_not_found");

        var exactMaximum = new UpdatePayrollDeductionsRequest(
            [new PayrollDeductionLine(employeeId, maximum, "หักเต็มจำนวนสุทธิ")]) { PayrollRunId = runId };
        (await validator.ValidateAsync(exactMaximum)).IsValid.Should().BeTrue();
        await payroll.UpdateDeductionsAsync(runId, exactMaximum, default);
        (await payroll.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == employeeId)
            .NetPay.Should().Be(0m, "a deduction equal to the cap is legal");

        await payroll.ApproveAsync(runId, default);
        var approved = new UpdatePayrollDeductionsRequest(
            [new PayrollDeductionLine(employeeId, 1m, "ทดสอบ")]) { PayrollRunId = runId };
        (await validator.ValidateAsync(approved)).IsValid.Should().BeFalse();
        var approvedWrite = () => payroll.UpdateDeductionsAsync(runId, approved, default);
        (await approvedWrite.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("payroll.not_draft");
        await payroll.PostAsync(runId, default);
        var posted = (await payroll.GetAsync(runId, default))!;
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.JournalId == posted.JournalId);
        Math.Round(je.Lines.Sum(l => l.DebitAmount), 2).Should()
            .Be(Math.Round(je.Lines.Sum(l => l.CreditAmount), 2));
        (await validator.ValidateAsync(approved)).IsValid.Should().BeFalse();
        var postedWrite = () => payroll.UpdateDeductionsAsync(runId, approved, default);
        (await postedWrite.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("payroll.not_draft");
    }

    [SkippableFact]
    public async Task Deduction_journal_balances_when_pit_or_sso_credit_line_is_omitted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        async Task ExerciseAsync(decimal salary, bool ssoApplicable, string omittedAccountCode)
        {
            var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
            await using var sp = Provider(company.CompanyId, company.BranchId);
            var employeeId = await AddEmployee(sp, salary, sso: ssoApplicable);
            var year = await FreshYearAsync(sp);
            await OpenPeriodAsync(sp, year, 1);   // R1/C3 (WP-5) — PostAsync below now guards on this

            await using var scope = sp.CreateAsyncScope();
            var payroll = scope.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await payroll.CreateDraftAsync(
                new CreatePayrollRunRequest(Period(year, 1), new DateOnly(year, 1, 28), null), default);
            await payroll.UpdateDeductionsAsync(runId,
                new UpdatePayrollDeductionsRequest(
                    [new PayrollDeductionLine(employeeId, 100m, "ทดสอบสาขาเครดิตแบบมีเงื่อนไข")]), default);
            await payroll.ApproveAsync(runId, default);
            await payroll.PostAsync(runId, default);

            var run = (await payroll.GetAsync(runId, default))!;
            if (omittedAccountCode == "2153") run.TotalPit.Should().Be(0m);
            if (omittedAccountCode == "2160")
                (run.TotalSsoEmployee + run.TotalSsoEmployer).Should().Be(0m);
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var omittedAccountId = await db.ChartOfAccounts.AsNoTracking()
                .Where(a => a.AccountCode == omittedAccountCode).Select(a => a.AccountId).SingleAsync();
            var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
                .SingleAsync(j => j.JournalId == run.JournalId);
            je.Lines.Should().NotContain(l => l.AccountId == omittedAccountId);
            Math.Round(je.Lines.Sum(l => l.DebitAmount), 2).Should()
                .Be(Math.Round(je.Lines.Sum(l => l.CreditAmount), 2));
        }

        await ExerciseAsync(10_000m, ssoApplicable: true, omittedAccountCode: "2153");
        await ExerciseAsync(80_000m, ssoApplicable: false, omittedAccountCode: "2160");
    }

    [SkippableFact]
    public async Task Multi_employee_deductions_roll_up_with_untouched_employees_and_balance()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        var employee1 = await AddEmployee(sp, 30_000m);
        var employee2 = await AddEmployee(sp, 20_000m);
        var employee3 = await AddEmployee(sp, 40_000m);
        var year = await FreshYearAsync(sp);
        await OpenPeriodAsync(sp, year, 2);   // R1/C3 (WP-5) — PostAsync below now guards on this

        await using var scope = sp.CreateAsyncScope();
        var payroll = scope.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 2), new DateOnly(year, 2, 28), null), default);
        var before = (await payroll.GetAsync(runId, default))!;
        await payroll.UpdateDeductionsAsync(runId,
            new UpdatePayrollDeductionsRequest(
            [
                new PayrollDeductionLine(employee1, 100m, "หักรายการหนึ่ง"),
                new PayrollDeductionLine(employee3, 250m, "หักรายการสอง"),
            ]), default);
        var draft = (await payroll.GetAsync(runId, default))!;
        draft.TotalOtherDeductions.Should().Be(350m);
        draft.TotalNet.Should().Be(before.TotalNet - 350m);
        draft.Payslips.Single(p => p.EmployeeId == employee1).OtherDeductions.Should().Be(100m);
        draft.Payslips.Single(p => p.EmployeeId == employee2).OtherDeductions.Should().Be(0m);
        draft.Payslips.Single(p => p.EmployeeId == employee3).OtherDeductions.Should().Be(250m);

        await payroll.ApproveAsync(runId, default);
        await payroll.PostAsync(runId, default);
        var posted = (await payroll.GetAsync(runId, default))!;
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.JournalId == posted.JournalId);
        Math.Round(je.Lines.Sum(l => l.DebitAmount), 2).Should()
            .Be(Math.Round(je.Lines.Sum(l => l.CreditAmount), 2));
    }

    [SkippableFact]
    public async Task Account_2180_exists_for_seeded_and_freshly_created_companies()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using (var seeded = Provider())
        await using (var s = seeded.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await db.ChartOfAccounts.AsNoTracking().SingleAsync(a => a.AccountCode == "2180"))
                .AccountNameTh.Should().Be("เงินหักจากพนักงานค้างนำส่ง");
        }

        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var fresh = TestCompanyFactory.BuildProvider(
            _fx.ConnectionString, company.CompanyId, company.BranchId);
        await using var freshScope = fresh.CreateAsyncScope();
        var freshDb = freshScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var account = await freshDb.ChartOfAccounts.AsNoTracking().SingleAsync(a => a.AccountCode == "2180");
        account.AccountNameEn.Should().Be("Employee Deductions Payable");
        account.AccountType.Should().Be(AccountType.Liability);
        account.NormalBalance.Should().Be(NormalBalance.Credit);
    }

    [SkippableFact]
    public async Task Posted_run_is_immutable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 1);
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
        var period = await FreshPeriodAsync(sp, 3);
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
        var period = await FreshPeriodAsync(sp, 5);
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
    public async Task Payslip_pdf_and_run_zip_render()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 6);
        var emp = await AddEmployee(sp, 40_000m);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var pdfSvc = s.ServiceProvider.GetRequiredService<IPayslipPdfService>();

        var pdf = await pdfSvc.BuildAsync(runId, emp, default);
        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");

        var (zip, name) = await pdfSvc.BuildRunZipAsync(runId, default);
        zip.Should().NotBeEmpty();
        name.Should().Contain(period);
    }

    [SkippableFact]
    public async Task Pnd1_monthly_fills_the_official_acroform()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 7);
        await AddEmployee(sp, 45_000m);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var pdf = await svc.BuildPnd1MonthlyAsync(runId, default);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        using var ms = new System.IO.MemoryStream(pdf);
        var doc = PdfSharp.Pdf.IO.PdfReader.Open(ms, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().BeGreaterThanOrEqualTo(3);   // main (2) + ≥1 ใบแนบ
    }

    // P-D #4 — annual employee 50ทวิ (ม.50ทวิ): aggregates the employee's POSTED slips by
    // payment year into one ม.40(1) row, rendered as the official 2-copy certificate.
    [SkippableFact]
    public async Task Employee_annual_wht50tawi_renders_two_copies()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var y = await FreshYearAsync(sp);
        var empId = await AddEmployee(sp, 60_000m);
        await RunThroughPost(sp, Period(y, 1));
        await RunThroughPost(sp, Period(y, 2));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var pdf = await svc.BuildEmployeeWht50TawiAsync(empId, y, default);

        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        using var ms = new System.IO.MemoryStream(pdf);
        var doc = PdfSharp.Pdf.IO.PdfReader.Open(ms, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        doc.PageCount.Should().Be(2, "RD requires ฉบับที่ 1 + ฉบับที่ 2");

        // No slips PAID to this employee in the prior year (filter is per-employee) → no_data.
        var act = () => svc.BuildEmployeeWht50TawiAsync(empId, y - 1, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("payroll.no_data");
    }

    // ม.52/ม.59 + ม.58(1) — ภ.ง.ด.1/1ก follow the PAYMENT date, not the payroll period:
    // a December-period run PAID in January belongs to the NEXT year's filings.
    [SkippableFact]
    public async Task Pnd1_filings_follow_payment_date_not_period()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var y = await FreshYearAsync(sp);
        await AddEmployee(sp, 30_000m);
        // R1/C3 (WP-5) §3.5 AMENDED 2026-08-12 (commit 97ace1c) — the period-END ceiling this test
        // used to have to route around (by seeding posted state directly) is GONE: arrears pay
        // across a period boundary is a first-class capability again, so this drives the REAL
        // service exactly as before WP-5. RunThroughPost opens PayDate's OWN month (y+1, ม.ค.),
        // not the run's period month (y, ธ.ค.) — see its R1/C3 comment.
        await RunThroughPost(sp, Period(y, 12), payDate: new DateOnly(y + 1, 1, 5));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();

        // ภ.ง.ด.1ก of the PAYMENT year (y+1) includes the run …
        var pdf = await svc.BuildPnd1aAnnualAsync(y + 1, default);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");

        // … and the PERIOD year (y) has nothing paid in it (y is a fresh year and all other
        // tests pay within their own period month) → payroll.no_data.
        var act = () => svc.BuildPnd1aAnnualAsync(y, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.no_data");
    }

    [SkippableFact]
    public async Task Sso_monthly_aggregates_insured_employees_with_clamped_contributory_wage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 8);
        var e50k   = await AddEmployee(sp, 50_000m);              // sso 750 → contributory wage clamped 15,000
        var e10k   = await AddEmployee(sp, 10_000m);              // sso 500 → contributory wage 10,000
        var eNoSso = await AddEmployee(sp, 30_000m, sso: false);  // excluded — no contribution
        var runId  = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var run = (await s.ServiceProvider.GetRequiredService<IPayrollRunService>().GetAsync(runId, default))!;
        var nid50    = run.Payslips.Single(p => p.EmployeeId == e50k).NationalId;
        var nid10    = run.Payslips.Single(p => p.EmployeeId == e10k).NationalId;
        var nidNoSso = run.Payslips.Single(p => p.EmployeeId == eNoSso).NationalId;

        var model = await s.ServiceProvider.GetRequiredService<ISsoFilingService>().BuildMonthlyAsync(runId, default);

        model.PeriodMonth.Should().Be(8);
        model.PeriodYearBE.Should().Be(model.PeriodYearCE + 543);

        var l50 = model.Lines.Single(l => l.NationalId == nid50);
        l50.EmployeeContribution.Should().Be(750m);              // 5% of the ฿15,000 ceiling
        l50.EmployerContribution.Should().Be(750m);              // equal 5% legs
        l50.Wage.Should().Be(50_000m);                           // ACTUAL wage (un-capped) — not the contributory base

        var l10 = model.Lines.Single(l => l.NationalId == nid10);
        l10.EmployeeContribution.Should().Be(500m);              // 5% of 10,000 (under the ceiling)
        l10.Wage.Should().Be(10_000m);

        model.Lines.Should().NotContain(l => l.NationalId == nidNoSso);   // SsoApplicable=false ⇒ no row

        // Run-level invariants robust to other tests' employees pooled into the same run.
        model.TotalEmployeeContribution.Should().Be(model.TotalEmployerContribution);
        model.GrandTotalContribution.Should().Be(model.TotalEmployeeContribution + model.TotalEmployerContribution);
        model.EmployeeCount.Should().Be(model.Lines.Count);
        model.Lines.Should().OnlyContain(l => l.EmployeeContribution > 0m);
    }

    // ── R2/WP-5 (H8/H9) — nothing silently wrong goes into a government file ──────────────

    private static async Task SetSsoEmployerAccountAsync(ServiceProvider sp, string accountNo)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ICompanyProfileService>();
        var before = await svc.GetAsync(default);
        // Every field must be echoed back: UpdateSoftAsync overwrites the WHOLE record, so an omitted
        // member silently NULLs it — and on the shared, never-reset teas_test that damage is permanent.
        await svc.UpdateSoftAsync(new UpdateCompanyProfileSoftRequest(
            before!.TradeName, before.LogoUrl, before.Phone, before.Email, before.Website,
            before.ContactName, before.BankName, before.BankAccountNo, before.BankAccountName,
            SsoEmployerAccountNo: accountNo, DefaultDocNotes: before.DefaultDocNotes), default);
    }

    [SkippableFact]
    public async Task T14_sso_file_refuses_a_blank_employer_account_instead_of_zero_filling()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // A fresh TestCompanyFactory company has SsoEmployerAccountNo=null on its profile, and this
        // suite's Provider() never configures Payroll:Sso:EmployerAccountNo — so the resolved
        // model.EmployerAccountNo is blank, exactly the H8 scenario (today: silently zero-filled to
        // "0000000000" in the file). RunThroughPost (not a bare Draft) so WP-4's future Posted-run
        // guard cannot break this test when it lands.
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        await AddEmployee(sp, 30_000m);
        var period = await FreshPeriodAsync(sp, 1);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();

        var act = () => sso.BuildMonthlyFileAsync(runId, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("sso_batch.missing_employer_account");
    }

    [SkippableFact]
    public async Task T15_sso_pdf_refuses_missing_employer_account_but_sso_schedule_still_renders()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        await AddEmployee(sp, 30_000m);
        var period = await FreshPeriodAsync(sp, 2);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();

        var actPdf = () => sso.BuildMonthlyPdfAsync(runId, default);
        (await actPdf.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("sso_batch.missing_employer_account");

        // O11-alt's on-screen สปส.1-10 ส่วนที่ 2 schedule must keep rendering — it SHOWS the user
        // what is missing rather than shutting them out (guard lives only in the file/PDF builders).
        var model = await sso.BuildMonthlyAsync(runId, default);
        model.EmployerAccountNo.Should().BeNullOrWhiteSpace();
    }

    [SkippableFact]
    public async Task T16_sso_file_refuses_a_name_with_a_non_cp874_character_naming_the_employee_and_code_point()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        await SetSsoEmployerAccountAsync(sp, "1234567890");
        // U+09AE (Bengali "MA") looks near-identical to Thai ม (U+0E21) but is a
        // DIFFERENT, non-cp874 character — a real corruption repro (co5 shipped 37 '?' bytes
        // from a character in this family). Written as the C# escape below, never the literal
        var empId = await AddEmployee(sp, 30_000m, firstNameTh: "สม\u09AEชาย");
        var period = await FreshPeriodAsync(sp, 3);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var nationalId = await db.Employees.AsNoTracking()
            .Where(e => e.EmployeeId == empId).Select(e => e.NationalId).SingleAsync();

        var act = () => sso.BuildMonthlyFileAsync(runId, default);
        var ex = (await act.Should().ThrowAsync<DomainException>()).Which;
        ex.Code.Should().Be("sso_batch.unencodable_name");
        ex.Message.Should().Contain("U+09AE", "the offending character's code point must be named");
        ex.Message.Should().Contain(nationalId, "the offending employee must be identifiable");
    }

    [SkippableFact]
    public async Task T17_sso_file_still_truncates_an_over_length_name_instead_of_refusing_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Pins §3.5(c) as DELIBERATE: 30/35/45 is the fixed-width file's own capacity, not a
        // defect. A future reviewer must not "fix" this into a refusal — that would leave a
        // company with a long legal name unable to file at all (the exact dead-end class R1's
        // review caught twice).
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        await SetSsoEmployerAccountAsync(sp, "1234567890");
        var longFirst = new string('ก', 35);   // > ชื่อ's 30-char field width
        await AddEmployee(sp, 30_000m, firstNameTh: longFirst, lastNameTh: "นามสกุลทดสอบ");
        var period = await FreshPeriodAsync(sp, 4);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();

        var (content, _) = await sso.BuildMonthlyFileAsync(runId, default);   // must NOT throw
        content.Should().NotBeEmpty();
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var text = System.Text.Encoding.GetEncoding(SpsBatchFormat.CodePage).GetString(content);
        var detail = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1];
        detail.Substring(17, 30).TrimEnd().Should()
            .Be(longFirst[..30], "truncated to the field's own capacity, not refused (I5's carve-out)");
    }

    // ── R2/WP-4 (H13) — a filing artifact only comes from a POSTED run ────────────────────────

    [SkippableFact]
    public async Task T9_pnd1_pdf_refuses_a_draft_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        await AddEmployee(sp, 30_000m);

        await using var s = sp.CreateAsyncScope();
        var payroll = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 5), new DateOnly(year, 5, 28), null), default);

        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var act = () => pnd1.BuildPnd1MonthlyAsync(runId, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.not_posted_for_filing");
    }

    [SkippableFact]
    public async Task T10_sso_file_and_pdf_refuse_a_draft_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        await AddEmployee(sp, 30_000m);

        await using var s = sp.CreateAsyncScope();
        var payroll = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 5), new DateOnly(year, 5, 28), null), default);

        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();
        var actFile = () => sso.BuildMonthlyFileAsync(runId, default);
        (await actFile.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.not_posted_for_filing");
        var actPdf = () => sso.BuildMonthlyPdfAsync(runId, default);
        (await actPdf.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.not_posted_for_filing");
    }

    [SkippableFact]
    public async Task T11_sso_schedule_refuses_a_draft_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        await AddEmployee(sp, 30_000m);

        await using var s = sp.CreateAsyncScope();
        var payroll = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 5), new DateOnly(year, 5, 28), null), default);

        // sso-schedule (PayrollEndpoints.cs:131-134, the O11-alt on-screen ส่วนที่ 2) calls
        // BuildMonthlyAsync directly with no other filter — this pins the SAME method T10 exercises
        // via its two artifact wrappers, proving the shared-loader guard placement covers it too.
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();
        var act = () => sso.BuildMonthlyAsync(runId, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.not_posted_for_filing");
    }

    [SkippableFact]
    public async Task T12_all_four_filing_surfaces_succeed_on_a_posted_run_with_journal_backing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        // WP-5's H8 guard (missing SSO employer account) sits AFTER this guard in the same builders —
        // set it so this test exercises ONLY the H13 posted-run guard, not a collateral H8 refusal.
        await SetSsoEmployerAccountAsync(sp, "1234567890");
        await AddEmployee(sp, 30_000m);
        var period = await FreshPeriodAsync(sp, 6);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();

        (await pnd1.BuildPnd1MonthlyAsync(runId, default)).Should().NotBeEmpty();
        (await sso.BuildMonthlyFileAsync(runId, default)).Content.Should().NotBeEmpty();
        (await sso.BuildMonthlyPdfAsync(runId, default)).Should().NotBeEmpty();
        (await sso.BuildMonthlyAsync(runId, default)).Should().NotBeNull();   // sso-schedule's own loader

        // I4's stated equivalence — Status == Posted iff JournalId != null.
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var run = await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.PayrollRunId == runId);
        run.Status.Should().Be(DocumentStatus.Posted);
        run.JournalId.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task T13_draft_runs_payslips_are_excluded_from_pnd1a_and_wht50tawi()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var empId = await AddEmployee(sp, 30_000m);

        await using var s = sp.CreateAsyncScope();
        var payroll = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 6), new DateOnly(year, 6, 28), null), default);
        // Deliberately left Draft — never Approved/Posted. This is a REGRESSION PIN: the exclusion
        // already exists (Pnd1FilingService.cs :79-82 / :118-120 filter Status == Posted) — no new
        // guard added for it, per §2.2's disposition. A year with only a Draft run has zero posted
        // data, so both surfaces correctly report payroll.no_data.

        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var actAnnual = () => pnd1.BuildPnd1aAnnualAsync(year, default);
        (await actAnnual.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.no_data");

        var actTawi = () => pnd1.BuildEmployeeWht50TawiAsync(empId, year, default);
        (await actTawi.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.no_data");
    }

    // ── R2/WP-4 §10 E3 (SEPARATE, REVERTABLE COMMIT) — a payslip may render once Approved; only ──
    // ── a Draft run is refused (an internal document, not an RD/SSO filing artifact). ───────────

    [SkippableFact]
    public async Task Payslip_refuses_a_draft_run_but_renders_once_approved()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var empId = await AddEmployee(sp, 30_000m);

        await using var s = sp.CreateAsyncScope();
        var payroll = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 6), new DateOnly(year, 6, 28), null), default);

        var payslipPdf = s.ServiceProvider.GetRequiredService<IPayslipPdfService>();
        var actDraft = () => payslipPdf.BuildAsync(runId, empId, default);
        (await actDraft.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.not_approved_for_payslip");
        var actZipDraft = () => payslipPdf.BuildRunZipAsync(runId, default);
        (await actZipDraft.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.not_approved_for_payslip");

        await payroll.ApproveAsync(runId, default);
        (await payslipPdf.BuildAsync(runId, empId, default)).Should().NotBeEmpty();
        var (zip, _) = await payslipPdf.BuildRunZipAsync(runId, default);
        zip.Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task Ytd_carries_so_constant_salary_withholds_evenly_across_two_months()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var empId = await AddEmployee(sp, 50_000m);

        var run1 = await RunThroughPost(sp, Period(year, 1));
        var run2 = await RunThroughPost(sp, Period(year, 2));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var p1 = (await svc.GetAsync(run1, default))!.Payslips.Single(p => p.EmployeeId == empId);
        var p2 = (await svc.GetAsync(run2, default))!.Payslips.Single(p => p.EmployeeId == empId);

        p1.PitWithheld.Should().Be(1_716.67m);
        p2.PitWithheld.Should().Be(p1.PitWithheld);
        p2.YtdIncome.Should().Be(100_000m);
        p2.YtdPit.Should().Be(p1.PitWithheld + p2.PitWithheld);
    }
    [SkippableFact]
    public async Task Opening_ytd_is_included_in_midyear_projection_and_sso_allowance()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshOpeningYearAsync(sp);
        var employeeId = await AddEmployee(sp, 80_000m,
            ytdOpeningYear: year, ytdOpeningIncome: 480_000m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 7), new DateOnly(year, 7, 28), null), default);
        var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == employeeId);

        const int monthsRemaining = 6;
        var sso = SsoContribution.Monthly(80_000m, 0.05m, 1_650m, 15_000m);
        var projected = 480_000m + 80_000m * monthsRemaining;
        const decimal priorInSystemSso = 0m;
        const decimal openingSso = 0m;
        var allowances = PayrollAllowanceRates.Default().Annual(
            MaritalStatus.Single, false, 0,
            Math.Min(priorInSystemSso + openingSso + sso * monthsRemaining, 9_000m));
        var expected = ThaiPitCalculator.MonthlyWithholding(
            projected, allowances, 0m, monthsRemaining, PitSchedule.Current());

        slip.PitWithheld.Should().Be(expected);
        slip.YtdIncome.Should().Be(560_000m);
    }

    [SkippableFact]
    public async Task January_without_opening_keeps_twelve_month_projection_and_sso_allowance()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var employeeId = await AddEmployee(sp, 80_000m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 1), new DateOnly(year, 1, 28), null), default);
        var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == employeeId);

        const int monthsRemaining = 12;
        var sso = SsoContribution.Monthly(80_000m, 0.05m, 1_650m, 15_000m);
        var allowances = PayrollAllowanceRates.Default().Annual(
            MaritalStatus.Single, false, 0, Math.Min(sso * monthsRemaining, 9_000m));
        var expected = ThaiPitCalculator.MonthlyWithholding(
            80_000m * monthsRemaining, allowances, 0m, monthsRemaining, PitSchedule.Current());

        slip.PitWithheld.Should().Be(expected);
    }

    [SkippableFact]
    public async Task Midyear_without_opening_uses_only_remaining_months_for_sso_allowance()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var employeeId = await AddEmployee(sp, 80_000m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(Period(year, 7), new DateOnly(year, 7, 28), null), default);
        var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == employeeId);

        const int monthsRemaining = 6;
        var sso = SsoContribution.Monthly(80_000m, 0.05m, 1_650m, 15_000m);
        const decimal priorInSystemSso = 0m;
        const decimal openingSso = 0m;
        var allowances = PayrollAllowanceRates.Default().Annual(
            MaritalStatus.Single, false, 0,
            Math.Min(priorInSystemSso + openingSso + sso * monthsRemaining, 9_000m));
        var expected = ThaiPitCalculator.MonthlyWithholding(
            80_000m * monthsRemaining, allowances, 0m, monthsRemaining, PitSchedule.Current());

        slip.PitWithheld.Should().Be(expected);
    }

    [SkippableFact]
    public async Task Pay_posts_wages_payable_to_selected_bank_and_blocks_double_pay()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 9);
        await AddEmployee(sp, 35_000m);

        int bankAccountId;
        await using (var setup = sp.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bankGl = await db.ChartOfAccounts.SingleAsync(a => a.AccountCode == "1120");
            var bank = new BankAccount
            {
                CompanyId = 1, BankCode = "TST" + Sfx(), BankName = "ธนาคารทดสอบ",
                AccountNo = Sfx(), GlCashAccountId = bankGl.AccountId,
                IsActive = true, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            bankAccountId = bank.BankAccountId;
        }

        var runId = await RunThroughPost(sp, period);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var beforePay = (await svc.GetAsync(runId, default))!;
        await svc.PayAsync(runId, new PayPayrollRunRequest(bankAccountId), default);

        var dbAfter = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var settlement = await dbAfter.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.Description == $"Payroll settlement {beforePay.DocNo}");
        var accountIds = settlement.Lines.Select(l => l.AccountId).ToArray();
        var codes = await dbAfter.ChartOfAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.AccountId))
            .ToDictionaryAsync(a => a.AccountId, a => a.AccountCode);
        settlement.DocDate.Should().Be(beforePay.PayDate);
        settlement.Lines.Single(l => codes[l.AccountId] == "2170").DebitAmount.Should().Be(beforePay.TotalNet);
        settlement.Lines.Single(l => codes[l.AccountId] == "1120").CreditAmount.Should().Be(beforePay.TotalNet);
        settlement.TotalDebit.Should().Be(settlement.TotalCredit);

        var doublePay = () => svc.PayAsync(runId, new PayPayrollRunRequest(bankAccountId), default);
        (await doublePay.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("payroll.already_paid");
    }

    [SkippableFact]
    public async Task Pay_without_any_active_bank_credits_cash_1110()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        List<BankAccount> activeBanks;
        await using var setup = sp.CreateAsyncScope();
        var setupDb = setup.ServiceProvider.GetRequiredService<AccountingDbContext>();
        activeBanks = await setupDb.BankAccounts.Where(b => b.IsActive).ToListAsync();
        foreach (var bank in activeBanks) bank.IsActive = false;
        await setupDb.SaveChangesAsync();

        try
        {
            var period = await FreshPeriodAsync(sp, 10);
            await AddEmployee(sp, 35_000m);
            var runId = await RunThroughPost(sp, period);

            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var beforePay = (await svc.GetAsync(runId, default))!;
            await svc.PayAsync(runId, new PayPayrollRunRequest(null), default);

            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var settlement = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
                .SingleAsync(j => j.Description == $"Payroll settlement {beforePay.DocNo}");
            var creditAccountId = settlement.Lines.Single(l => l.CreditAmount == beforePay.TotalNet).AccountId;
            var creditCode = await db.ChartOfAccounts.AsNoTracking()
                .Where(a => a.AccountId == creditAccountId).Select(a => a.AccountCode).SingleAsync();
            creditCode.Should().Be("1110");
        }
        finally
        {
            foreach (var bank in activeBanks) bank.IsActive = true;
            await setupDb.SaveChangesAsync();
        }
    }

    // ---- O8 — calendar-day salary proration (specs/payroll-proration-o8.md, §Test list B) ----

    [SkippableFact]
    public async Task B1_full_month_control_gross_taxable_is_unrounded_base_salary()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 7);   // July — always 31 days
        var empId = await AddEmployee(sp, 45_678.9012m);
        try
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(
                new CreatePayrollRunRequest(period, new DateOnly(int.Parse(period[..4]), 7, 28), null), default);
            var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == empId);

            slip.GrossTaxable.Should().Be(45_678.9012m);   // exact, un-rounded — full-month short-circuit
            slip.NetPay.Should().Be(slip.GrossTaxable - slip.PitWithheld - slip.SsoEmployee);
        }
        finally
        {
            // R1/C1 (WP-3) — this employee's salary is DELIBERATELY 4dp to prove the full-month
            // short-circuit does no rounding (above). But every AddEmployee row is permanent and
            // has no TerminationDate (§8 class invariant: "the run pools EVERY active company-1
            // employee"), so left active it silently joins every OTHER test's payroll GL total
            // forever — invisible until JournalEntry.MarkPosted's new precision guard (I14) refuses
            // to post any run whose aggregate salary expense inherits this employee's extra
            // decimals. Found via WP-3's RED/GREEN sweep: 41 copies of this exact fixture had
            // accumulated in the shared teas_test DB across historical runs and broke 17 unrelated
            // payroll tests system-wide (troubles-wiki.md). `finally` (mirrors
            // Pay_without_any_active_bank_credits_cash_1110's try/finally below) so a FAILED
            // assertion above still deactivates this employee — an exception here must never
            // re-leak the exact poison this block exists to clean up.
            await using var cleanupScope = sp.CreateAsyncScope();
            var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var emp = await cleanupDb.Employees.SingleAsync(e => e.EmployeeId == empId);
            emp.IsActive = false;
            await cleanupDb.SaveChangesAsync();
        }
    }

    [SkippableFact]
    public async Task B2_mid_month_hire_prorates_gross_and_derives_pit_and_sso_from_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var period = Period(year, 7);
        var empId = await AddEmployee(sp, 60_000m, hireDate: new DateOnly(year, 7, 15));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(year, 7, 28), null), default);
        var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == empId);

        slip.GrossTaxable.Should().Be(32_903.23m);
        slip.YtdIncome.Should().Be(32_903.23m);
        slip.SsoEmployee.Should().Be(750m);   // ceiling (test config 15,000) still binds
        slip.SsoEmployer.Should().Be(750m);

        const int monthsRemaining = 6;
        var allowances = PayrollAllowanceRates.Default().Annual(
            MaritalStatus.Single, false, 0, Math.Min(750m * monthsRemaining, 9_000m));
        var expectedPit = ThaiPitCalculator.MonthlyWithholding(
            32_903.23m * monthsRemaining, allowances, 0m, monthsRemaining, PitSchedule.Current());
        slip.PitWithheld.Should().Be(expectedPit);
    }

    [SkippableFact]
    public async Task B3_mid_month_leave_prorates_the_final_month()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var period = Period(year, 7);
        var empId = await AddEmployee(sp, 60_000m, terminationDate: new DateOnly(year, 7, 10));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(year, 7, 28), null), default);
        var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == empId);

        slip.GrossTaxable.Should().Be(19_354.84m);
        slip.NetPay.Should().Be(19_354.84m - slip.PitWithheld - slip.SsoEmployee);
    }

    [SkippableFact]
    public async Task B4_hire_and_leave_in_the_same_month()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var period = Period(year, 7);
        var empId = await AddEmployee(sp, 60_000m,
            hireDate: new DateOnly(year, 7, 10), terminationDate: new DateOnly(year, 7, 20));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(year, 7, 28), null), default);
        var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == empId);

        slip.GrossTaxable.Should().Be(21_290.32m);
    }

    [SkippableFact]
    public async Task B5_past_month_termination_is_excluded_not_a_zero_payslip()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var period = Period(year, 7);
        var control = await AddEmployee(sp, 30_000m);
        var pastLeaver = await AddEmployee(sp, 30_000m, terminationDate: new DateOnly(year, 6, 30));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(year, 7, 28), null), default);
        var run = (await svc.GetAsync(runId, default))!;

        run.Payslips.Should().Contain(p => p.EmployeeId == control);
        run.Payslips.Should().NotContain(p => p.EmployeeId == pastLeaver);   // no bogus ฿0 row
    }

    [SkippableFact]
    public async Task B6_sso_recomputed_on_the_prorated_wage_ties_out_on_sps110()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var period = Period(year, 7);
        var empId = await AddEmployee(sp, 20_000m, hireDate: new DateOnly(year, 7, 15));

        // R2/WP-4 (H13) — sso.BuildMonthlyAsync now refuses a non-Posted run, so this test posts
        // first (RunThroughPost, same as its sibling Sso_monthly_aggregates_insured_employees_...
        // above) instead of reading straight off the Draft. Post does not change the computed
        // wage/SSO figures (only adds Status/DocNo/JournalId), so the golden assertions are unaffected.
        var runId = await RunThroughPost(sp, period, new DateOnly(year, 7, 28));

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == empId);

        slip.GrossTaxable.Should().Be(10_967.74m);
        slip.SsoEmployee.Should().Be(548.39m);    // un-prorated would be 750 — this IS the §3 proof
        slip.SsoEmployer.Should().Be(548.39m);

        var model = await s.ServiceProvider.GetRequiredService<ISsoFilingService>().BuildMonthlyAsync(runId, default);
        var line = model.Lines.Single(l => l.NationalId == slip.NationalId);
        line.Wage.Should().Be(10_967.74m);
        line.EmployeeContribution.Should().Be(548.39m);
    }

    // ---- O11-alt — สปส.1-10 ส่วนที่ 2 on-screen schedule (specs/sso-schedule-onscreen-o11alt.md) ----
    // SsoScheduleDto.FromModel is a pure PROJECTION of BuildMonthlyAsync's own model, so these tests
    // pin the projection to the SAME model the ส่วนที่ 1 PDF/file already use — they can never disagree.

    [SkippableFact]
    public async Task Sso_schedule_dto_projects_the_same_lines_totals_and_count_as_the_run_summary()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 11);
        var e50k   = await AddEmployee(sp, 50_000m);
        var e10k   = await AddEmployee(sp, 10_000m);
        var eNoSso = await AddEmployee(sp, 30_000m, sso: false);   // not insured — must be absent
        var runId  = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var model = await s.ServiceProvider.GetRequiredService<ISsoFilingService>().BuildMonthlyAsync(runId, default);
        var dto = SsoScheduleDto.FromModel(model);
        var run = (await s.ServiceProvider.GetRequiredService<IPayrollRunService>().GetAsync(runId, default))!;

        // §8 isolation: the shared teas_test DB pools EVERY active company-1 employee into this run
        // (documented class invariant, see B8 above), so assert on THESE employees + run-level
        // invariants, never on a fixed row count.
        dto.EmployeeCount.Should().Be(model.EmployeeCount);
        dto.TotalWage.Should().Be(model.TotalWage);
        dto.TotalEmployeeContribution.Should().Be(model.TotalEmployeeContribution);
        dto.TotalEmployerContribution.Should().Be(model.TotalEmployerContribution);

        // A1 invariant — totals/count tie out EXACTLY to what ส่วนที่ 1 (the run summary) reports.
        dto.TotalEmployeeContribution.Should().Be(run.TotalSsoEmployee);
        dto.TotalEmployerContribution.Should().Be(run.TotalSsoEmployer);
        dto.EmployeeCount.Should().Be(run.Payslips.Count(p => p.SsoEmployee > 0m));

        foreach (var line in dto.Lines)
        {
            var slip = run.Payslips.Single(p => p.NationalId == line.NationalId);
            line.Wage.Should().Be(slip.GrossTaxable);
            line.EmployeeContribution.Should().Be(slip.SsoEmployee);
            line.EmployerContribution.Should().Be(slip.SsoEmployer);
        }
        dto.Lines.Select(l => l.No).Should().BeEquivalentTo(Enumerable.Range(1, dto.Lines.Count));

        var nid50k = run.Payslips.Single(p => p.EmployeeId == e50k).NationalId;
        var nid10k = run.Payslips.Single(p => p.EmployeeId == e10k).NationalId;
        var excludedNid = run.Payslips.Single(p => p.EmployeeId == eNoSso).NationalId;
        dto.Lines.Should().ContainSingle(l => l.NationalId == nid50k)
            .Which.EmployeeContribution.Should().Be(750m);
        dto.Lines.Should().ContainSingle(l => l.NationalId == nid10k)
            .Which.EmployeeContribution.Should().Be(500m);
        dto.Lines.Should().NotContain(l => l.NationalId == excludedNid,
            "SsoApplicable=false ⇒ zero contribution ⇒ absent from the schedule, same as ส่วนที่ 1");
    }

    [SkippableFact]
    public async Task Sso_schedule_dto_shows_the_prorated_wage_for_a_mid_month_joiner_not_the_base_salary()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var period = Period(year, 7);
        // Same fixture as B6 (20,000 base salary, hired the 15th of a 31-day month) — reused per the
        // spec's instruction to tie this to O8 rather than invent a new proration golden.
        var empId = await AddEmployee(sp, 20_000m, hireDate: new DateOnly(year, 7, 15));
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var model = await s.ServiceProvider.GetRequiredService<ISsoFilingService>().BuildMonthlyAsync(runId, default);
        var dto = SsoScheduleDto.FromModel(model);
        var slip = (await s.ServiceProvider.GetRequiredService<IPayrollRunService>().GetAsync(runId, default))!
            .Payslips.Single(p => p.EmployeeId == empId);
        var line = dto.Lines.Single(l => l.NationalId == slip.NationalId);

        line.Wage.Should().Be(10_967.74m);     // B6's own prorated golden — NOT the 20,000 base salary
        line.Wage.Should().NotBe(20_000m);
        line.EmployeeContribution.Should().Be(548.39m);
    }

    [SkippableFact]
    public async Task B7_prorates_using_the_real_month_length_28_29_and_30_days()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);

        // February — the random year's Feb may be 28 or 29 days; derive both the day count and the
        // expected amount from the REAL month length (a leap February gives 15 days, not 14).
        var febEmp = await AddEmployee(sp, 28_000m, hireDate: new DateOnly(year, 2, 15));
        await using (var sFeb = sp.CreateAsyncScope())
        {
            var svc = sFeb.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(
                new CreatePayrollRunRequest(Period(year, 2), new DateOnly(year, 2, 28), null), default);
            var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == febEmp);

            var dim = DateTime.DaysInMonth(year, 2);
            var days = dim - 15 + 1;
            var expected = decimal.Round(28_000m * days / dim, 2, MidpointRounding.AwayFromZero);
            slip.GrossTaxable.Should().Be(expected);
        }

        // April is always 30 days → hired the 16th = 15 days = an exact hardcoded golden.
        var aprEmp = await AddEmployee(sp, 30_000m, hireDate: new DateOnly(year, 4, 16));
        await using (var sApr = sp.CreateAsyncScope())
        {
            var svc = sApr.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(
                new CreatePayrollRunRequest(Period(year, 4), new DateOnly(year, 4, 28), null), default);
            var slip = (await svc.GetAsync(runId, default))!.Payslips.Single(p => p.EmployeeId == aprEmp);

            slip.GrossTaxable.Should().Be(15_000.00m);
        }
    }

    [SkippableFact]
    public async Task B8_tie_out_printed_pnd1_matches_the_prorated_gl_and_payslip()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = await FreshYearAsync(sp);
        var period = Period(year, 7);
        // Terminated at the period's own last day (harmless — still clips to periodEnd, so the day
        // count/gross are unaffected) so this employee does NOT stay "active forever" and bleed a
        // full, un-prorated ฿61,234 into some LATER test's run that happens to draw a bigger random
        // year — teas_test is shared and never reset (troubles-wiki).
        var empId = await AddEmployee(sp, 61_234m,
            hireDate: new DateOnly(year, 7, 15), terminationDate: new DateOnly(year, 7, 31));

        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var run = (await svc.GetAsync(runId, default))!;
        var slip = run.Payslips.Single(p => p.EmployeeId == empId);
        // 61,234 × 17 / 31 = 33,579.935483... → 33,579.94 half-up (verified independently; the
        // spec's stated 33,580.58 golden for this "distinctive number" was an arithmetic slip —
        // every OTHER spec golden, including A15-A19/B2-B7 on the same formula, checks out exactly).
        slip.GrossTaxable.Should().Be(33_579.94m);                        // 1. payslip

        // 2. GL — account 5400 debit == run's total prorated gross, and the JE balances.
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.JournalId == run.JournalId);
        var accountIds = je.Lines.Select(l => l.AccountId).ToArray();
        var codes = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.AccountId))
            .ToDictionaryAsync(a => a.AccountId, a => a.AccountCode);
        je.Lines.Single(l => codes[l.AccountId] == "5400").DebitAmount.Should().Be(run.TotalGrossTaxable);
        je.TotalDebit.Should().Be(je.TotalCredit);

        // 3. Printed ภ.ง.ด.1 — extract text and assert the PRORATED figure appears, the un-prorated
        // figure does NOT — the exact defect the army leg found in the real PDF.
        var pnd1Svc = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var pdfBytes = await pnd1Svc.BuildPnd1MonthlyAsync(runId, default);
        using var doc = PdfDocument.Open(pdfBytes);
        var text = string.Concat(doc.GetPages().Select(p => p.Text));
        // Strip whitespace AND the dashes the form inserts into the National ID (Pnd1FormFiller.
        // FormatTaxId: X-XXXX-XXXXX-XX-X) so a plain-digit NationalId still matches as a substring.
        var normalized = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        // The run pools EVERY active company-1 employee in the shared teas_test DB (documented class
        // invariant), so a whole-document substring check is unsound — some unrelated employee's own
        // (unprorated, coincidental) full-month gross can share digits with ours. Scope the check to
        // OUR employee's own printed line by anchoring on its unique NationalId.
        var idx = normalized.IndexOf(slip.NationalId, StringComparison.Ordinal);
        idx.Should().BeGreaterThanOrEqualTo(0, "our employee's ใบแนบ line must be on the form");
        var ourLine = normalized.Substring(idx, Math.Min(120, normalized.Length - idx));
        ourLine.Should().Contain("33,579.94");
        ourLine.Should().Contain(slip.PitWithheld.ToString("#,##0.00"));
        ourLine.Should().NotContain("61,234.00");   // the un-prorated figure — the army leg's defect

        // 4. Payslip PDF still renders.
        var pdfSvc = s.ServiceProvider.GetRequiredService<IPayslipPdfService>();
        var payslipPdf = await pdfSvc.BuildAsync(runId, empId, default);
        System.Text.Encoding.ASCII.GetString(payslipPdf, 0, 5).Should().Be("%PDF-");
    }

    // ── R1/C1 (WP-3) — T14: payroll deduction / expense-claim / PV line precision. I14, I16. ──

    [SkippableFact]
    public async Task T14_3dp_deduction_expense_and_pv_lines_are_refused_valid_2dp_deduction_posts_and_ties_to_2170()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // ── negative (I14): a 3dp amount is refused at every money-carrying validator ──
        await using (var sp0 = Provider())
        await using (var s0 = sp0.CreateAsyncScope())
        {
            var payrollForValidator = s0.ServiceProvider.GetRequiredService<IPayrollRunService>();
            IValidator<UpdatePayrollDeductionsRequest> deductionValidator =
                new UpdatePayrollDeductionsValidator(payrollForValidator);
            var badDeduction = new UpdatePayrollDeductionsRequest(
                [new PayrollDeductionLine(1, 100.129m, "ทดสอบ WP-3")]) { PayrollRunId = 0 };
            var deductionResult = await deductionValidator.ValidateAsync(badDeduction);
            deductionResult.IsValid.Should().BeFalse();
            deductionResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("2 decimal"));
        }

        var expenseLineResult = new ExpenseClaimLineInputValidator().Validate(
            new ExpenseClaimLineInput(1, null, "ทดสอบ WP-3", null, 100.005m, null, 0m, false));
        expenseLineResult.IsValid.Should().BeFalse();
        expenseLineResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("2 decimal"));

        var pvResult = new CreatePaymentVoucherValidator().Validate(new CreatePaymentVoucherRequest(
            new SystemClock().TodayInBangkok(), 1, 1, PaymentMethod.Cash, null, null, null,
            "THB", 1m, null, null,
            [new PaymentVoucherLineInput(null, "ทดสอบ WP-3", 100.005m, null, 0m, false, null, 0m)]));
        pvResult.IsValid.Should().BeFalse();
        pvResult.Errors.Should().Contain(e => e.ErrorMessage.Contains("2 decimal"));

        // ── positive (I16): a valid 2dp deduction posts, and the payslip's printed net foots
        // EXACTLY to the 2170 credit. Uses a FRESH, single-employee company (TestCompanyFactory)
        // rather than the shared company-1 pool — that pool accumulates employees across the
        // whole suite, so the run-level 2170 credit (Σ every active employee's NetPay) would
        // never equal one specific payslip's NetPay there (see the class-level isolation comment
        // above). A brand-new company has exactly the one employee this test adds.
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spX = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var employeeId = await AddEmployee(spX, 30_000m);
        var period = await FreshPeriodAsync(spX, 1);
        await OpenPeriodAsync(spX, int.Parse(period[..4]), 1);   // R1/C3 (WP-5) — PostAsync guards on this

        await using var scopeX = spX.CreateAsyncScope();
        var payroll = scopeX.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runId = await payroll.CreateDraftAsync(new CreatePayrollRunRequest(
            period, new DateOnly(int.Parse(period[..4]), 1, 28), null), default);
        await payroll.UpdateDeductionsAsync(runId, new UpdatePayrollDeductionsRequest(
            [new PayrollDeductionLine(employeeId, 100.13m, "ทดสอบ WP-3 valid 2dp")]), default);
        await payroll.ApproveAsync(runId, default);
        await payroll.PostAsync(runId, default);

        var posted = (await payroll.GetAsync(runId, default))!;
        var slip = posted.Payslips.Single(p => p.EmployeeId == employeeId);

        var db = scopeX.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.JournalId == posted.JournalId);
        var netWagesAccountId = await db.ChartOfAccounts.AsNoTracking()
            .Where(a => a.AccountCode == "2170").Select(a => a.AccountId).SingleAsync();
        var netCredit = je.Lines.Single(l => l.AccountId == netWagesAccountId).CreditAmount;

        netCredit.Should().Be(slip.NetPay,
            "I16 — the payslip's printed net (what PayslipPdf.cs renders) must foot exactly to the 2170 credit");
        decimal.Round(netCredit, 2).Should().Be(netCredit, "the posted credit itself must carry no more than 2dp");
        je.TotalDebit.Should().Be(je.TotalCredit);
    }

    // ── R1/C3 (WP-5) — payroll period + future-pay-date guard. T18–T20 prove I21–I23. ──

    [SkippableFact]
    public async Task T18_post_and_pay_refuse_into_a_closed_period_no_je_created()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Fresh, single-purpose company — this test CLOSES real period rows, which must never
        // touch company 1's shared periods (other tests post "today"-dated documents there).
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, company.CompanyId, company.BranchId);
        var today = new SystemClock().TodayInBangkok();

        // (a) a period closed BEFORE any run targets it → post is refused, no JE, run stays Approved.
        var closedMonth = today.AddMonths(-2);
        await AddEmployee(sp, 28_000m);
        await using (var closeScope = sp.CreateAsyncScope())
            await closeScope.ServiceProvider.GetRequiredService<IPeriodCloseService>()
                .CloseAsync(closedMonth.Year, closedMonth.Month, "T18 setup", default);

        await using (var s1 = sp.CreateAsyncScope())
        {
            var svc = s1.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(new CreatePayrollRunRequest(
                Period(closedMonth.Year, closedMonth.Month),
                new DateOnly(closedMonth.Year, closedMonth.Month, 28), null), default);
            await svc.ApproveAsync(runId, default);

            var post = () => svc.PostAsync(runId, default);
            var ex = (await post.Should().ThrowAsync<DomainException>()).Which;
            ex.Code.Should().Be("payroll.period_closed");
            ex.Message.Should().Contain("reopen");

            var stillUnposted = (await svc.GetAsync(runId, default))!;
            stillUnposted.Status.Should().Be("APPROVED");
            stillUnposted.JournalId.Should().BeNull();
        }

        // (b) a run posted while its own period was OPEN, then that period is closed afterward →
        // Pay is refused. Proves the guard also protects PayAsync, not just PostAsync.
        await AddEmployee(sp, 32_000m);
        long runId2;
        await using (var s2 = sp.CreateAsyncScope())
        {
            var svc = s2.ServiceProvider.GetRequiredService<IPayrollRunService>();
            runId2 = await svc.CreateDraftAsync(new CreatePayrollRunRequest(
                Period(today.Year, today.Month), today, null), default);
            await svc.ApproveAsync(runId2, default);
            await svc.PostAsync(runId2, default);   // succeeds — current month is open by default
        }
        await using (var closeScope2 = sp.CreateAsyncScope())
            await closeScope2.ServiceProvider.GetRequiredService<IPeriodCloseService>()
                .CloseAsync(today.Year, today.Month, "T18 close-after-post", default);
        await using (var s3 = sp.CreateAsyncScope())
        {
            var svc = s3.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var pay = () => svc.PayAsync(runId2, new PayPayrollRunRequest(null), default);
            var ex = (await pay.Should().ThrowAsync<DomainException>()).Which;
            ex.Code.Should().Be("payroll.period_closed");
            ex.Message.Should().Contain("reopen");
        }
    }

    // R1/C3 §3.5 AMENDED 2026-08-12 (commit 97ace1c) — the period-END ceiling this test used to
    // assert is GONE (Fable's own error, reversed): it made arrears pay impossible and caught
    // nothing the IsOpenAsync check below doesn't already catch. Only a FLOOR remains (cannot pay
    // a period before it starts) plus the real guard (PayDate's own month must be open).
    [SkippableFact]
    public async Task T19_pay_date_before_period_start_is_refused_arrears_and_pre_payday_pay_both_work()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // (a) FLOOR — PayDate before the run's OWN period starts → refused, no JE. A sanity check,
        // not a business rule: you cannot pay a period before it has begun.
        await using (var spA = Provider())
        {
            var periodA = await FreshPeriodAsync(spA, 6);
            var yearA = int.Parse(periodA[..4]);
            await AddEmployee(spA, 25_000m);
            await using var sA = spA.CreateAsyncScope();
            var svc = sA.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(new CreatePayrollRunRequest(
                periodA, new DateOnly(yearA, 5, 31), null), default);   // 1 day before June starts
            await svc.ApproveAsync(runId, default);
            var post = () => svc.PostAsync(runId, default);
            var ex = (await post.Should().ThrowAsync<DomainException>()).Which;
            ex.Code.Should().Be("payroll.pay_date_outside_period");
            (await svc.GetAsync(runId, default))!.JournalId.Should().BeNull();
        }

        // (b) generalises the reported period=209912/payDate=2099-12-31 case: PayDate is INSIDE
        // the period (so the floor in (a) does not fire — there is no ceiling to fire either) but
        // the period was never opened (a distant, never-touched future month) → refused via
        // IsOpenAsync, the ONLY thing that catches this case (the removed ceiling never did).
        await using (var spB = Provider())
        {
            var yearB = await FreshYearAsync(spB);
            await AddEmployee(spB, 25_000m);
            var periodB = Period(yearB, 12);
            await using var sB = spB.CreateAsyncScope();
            var svc = sB.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(new CreatePayrollRunRequest(
                periodB, new DateOnly(yearB, 12, 31), null), default);
            await svc.ApproveAsync(runId, default);
            var post = () => svc.PostAsync(runId, default);
            (await post.Should().ThrowAsync<DomainException>()).Which.Code
                .Should().Be("payroll.period_closed");
        }

        // (c) REGRESSION — the case Ham's original decision protects: a run for the CURRENT
        // period with PayDate still in the future *inside* that period posts normally (post
        // today, pay at period end — always >= today, so exercisable on any calendar day). A
        // fresh company isolates the REAL current month, which random years can't dodge.
        var companyC = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spC = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyC.CompanyId, companyC.BranchId);
        await AddEmployee(spC, 40_000m);
        var today = new SystemClock().TodayInBangkok();
        var periodEndC = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        await using var sC = spC.CreateAsyncScope();
        var svcC = sC.ServiceProvider.GetRequiredService<IPayrollRunService>();
        var runIdC = await svcC.CreateDraftAsync(new CreatePayrollRunRequest(
            Period(today.Year, today.Month), periodEndC, null), default);
        await svcC.ApproveAsync(runIdC, default);
        await svcC.PostAsync(runIdC, default);   // must NOT throw
        (await svcC.GetAsync(runIdC, default))!.Status.Should().Be("POSTED");

        // (d) NEW — ARREARS PAY WORKS: a December-period run PAID in January (the following
        // month, ม.52/ม.59) posts AND pays normally once January is open. This is exactly the
        // capability the ceiling used to break — Pnd1_filings_follow_payment_date_not_period
        // exists for the same reason and now drives the real service again (no more seed-bypass).
        // Fresh company, not the shared Provider() — company 1 accumulates multiple active bank
        // accounts across the whole suite's history (every Pay_posts_wages_payable_to_selected_
        // bank_and_blocks_double_pay-style test leaves one behind), which makes PayAsync's
        // no-explicit-bank branch throw payroll.bank_required (activeBanks.Count > 1) — a
        // pre-existing DB-pollution hazard unrelated to this guard. A brand-new company starts
        // with zero bank accounts, so PayAsync falls through to crediting cash (1110) directly.
        var companyD = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using (var spD = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyD.CompanyId, companyD.BranchId))
        {
            var yearD = await FreshYearAsync(spD);
            await AddEmployee(spD, 27_000m);
            var arrearsPayDate = new DateOnly(yearD + 1, 1, 5);
            await OpenPeriodAsync(spD, arrearsPayDate.Year, arrearsPayDate.Month);
            await using var sD = spD.CreateAsyncScope();
            var svc = sD.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(new CreatePayrollRunRequest(
                Period(yearD, 12), arrearsPayDate, null), default);
            await svc.ApproveAsync(runId, default);
            await svc.PostAsync(runId, default);      // must NOT throw — arrears pay is legal again
            await svc.PayAsync(runId, new PayPayrollRunRequest(null), default);
            var paid = (await svc.GetAsync(runId, default))!;
            paid.Status.Should().Be("POSTED");
            paid.PaidAt.Should().NotBeNull();
        }
    }

    [SkippableFact]
    public async Task T20_current_month_posts_and_pays_back_month_recovers_via_o14_reopen()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, company.CompanyId, company.BranchId);
        var today = new SystemClock().TodayInBangkok();

        // Current-month run posts and pays normally.
        await AddEmployee(sp, 33_000m);
        await using (var s1 = sp.CreateAsyncScope())
        {
            var svc = s1.ServiceProvider.GetRequiredService<IPayrollRunService>();
            var runId = await svc.CreateDraftAsync(new CreatePayrollRunRequest(
                Period(today.Year, today.Month), today, null), default);
            await svc.ApproveAsync(runId, default);
            await svc.PostAsync(runId, default);
            await svc.PayAsync(runId, new PayPayrollRunRequest(null), default);
            (await svc.GetAsync(runId, default))!.Status.Should().Be("POSTED");
        }

        // Back month: close it, a run for it is refused, the message names the reopen route,
        // reopening via that exact route unblocks Post, then the period re-closes.
        var backMonth = today.AddMonths(-1);
        await using (var closeScope = sp.CreateAsyncScope())
            await closeScope.ServiceProvider.GetRequiredService<IPeriodCloseService>()
                .CloseAsync(backMonth.Year, backMonth.Month, "T20 back-month setup", default);

        await AddEmployee(sp, 33_000m);
        long backRunId;
        await using (var s2 = sp.CreateAsyncScope())
        {
            var svc = s2.ServiceProvider.GetRequiredService<IPayrollRunService>();
            backRunId = await svc.CreateDraftAsync(new CreatePayrollRunRequest(
                Period(backMonth.Year, backMonth.Month), backMonth, null), default);
            await svc.ApproveAsync(backRunId, default);
            var post = () => svc.PostAsync(backRunId, default);
            var ex = (await post.Should().ThrowAsync<DomainException>()).Which;
            ex.Code.Should().Be("payroll.period_closed");
            ex.Message.Should().Contain($"/periods/{backMonth.Year}/{backMonth.Month}/reopen");
        }

        await using (var reopenScope = sp.CreateAsyncScope())
            await reopenScope.ServiceProvider.GetRequiredService<IPeriodCloseService>()
                .ReopenAsync(backMonth.Year, backMonth.Month, "T20 verifying the escape hatch", default);

        await using (var s3 = sp.CreateAsyncScope())
        {
            var svc = s3.ServiceProvider.GetRequiredService<IPayrollRunService>();
            await svc.PostAsync(backRunId, default);   // must succeed now
            (await svc.GetAsync(backRunId, default))!.Status.Should().Be("POSTED");
        }

        await using var recloseScope = sp.CreateAsyncScope();
        await recloseScope.ServiceProvider.GetRequiredService<IPeriodCloseService>()
            .CloseAsync(backMonth.Year, backMonth.Month, "T20 re-close", default);
    }

    // R2 Tier-2 F2 — the dead end itself. Before the fix, CreatePayrollRunValidator only checked
    // PeriodYearMonth; a typo'd PayDate (wrong month) sailed through Create, got Approved, then Post
    // refused it FOREVER (payroll.pay_date_outside_period) with no delete/un-approve/replace path.
    // This pins the fix at the boundary where it belongs: the validator the Create endpoint actually
    // runs (PayrollEndpoints.cs:37) — CreateDraftAsync itself must stay unguarded on this (T19(a)
    // deliberately builds the same bad state THROUGH the service to prove Post's own floor fires).
    [Fact]
    public void CreatePayrollRunValidator_refuses_pay_date_before_period_start()
    {
        var validator = new CreatePayrollRunValidator();
        var req = new CreatePayrollRunRequest("202605", new DateOnly(2026, 4, 25), null);   // one day
        var result = validator.Validate(req);                                              // before June
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreatePayrollRunRequest.PayDate));
    }

    // Arrears pay must still validate at creation — no ceiling. Mirrors T19(d)'s December-period/
    // January-PayDate case, just at the validator boundary instead of the live service.
    [Fact]
    public void CreatePayrollRunValidator_accepts_arrears_pay_date_in_the_following_month()
    {
        var validator = new CreatePayrollRunValidator();
        var req = new CreatePayrollRunRequest("202512", new DateOnly(2026, 1, 5), null);
        validator.Validate(req).IsValid.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Approved_never_posted_run_can_be_deleted_and_its_period_reused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = await FreshPeriodAsync(sp, 5);
        var year = int.Parse(period[..4]);
        await AddEmployee(sp, 22_000m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPayrollRunService>();

        // Reproduce the stuck run EXACTLY as it happens for real: through the service (which the
        // validator does not gate), so a typo'd PayDate before the period reaches Approved with a
        // null JournalId — never Posted.
        var badPayDate = new DateOnly(year, 4, 25);   // before period 5 (May) starts
        var runId = await svc.CreateDraftAsync(new CreatePayrollRunRequest(period, badPayDate, null), default);
        await svc.ApproveAsync(runId, default);
        var stuck = (await svc.GetAsync(runId, default))!;
        stuck.Status.Should().Be("APPROVED");
        stuck.JournalId.Should().BeNull();

        await svc.DeleteDraftAsync(runId, default);          // RED before the fix: payroll.not_draft
        (await svc.GetAsync(runId, default)).Should().BeNull();

        // duplicate_period has no status filter (AnyAsync on PeriodYearMonth alone) — proves the
        // hard delete above is what clears it; no separate fix to duplicate_period was needed.
        var replacementId = await svc.CreateDraftAsync(
            new CreatePayrollRunRequest(period, new DateOnly(year, 5, 25), null), default);
        replacementId.Should().BeGreaterThan(0);
    }

    // ── R2/L1-1 (PLAN-fix-findings-r2.md, Unit U1) — ภ.ง.ด.1/ภ.ง.ด.1ก/สปส.1-10 must never
    // render the employer's payer Tax ID as a blank/all-zero placeholder. Seed 637 repaired
    // master.companies.tax_id but master.company_profile.tax_id (read FIRST by
    // `prof?.TaxId ?? c?.TaxId ?? ""`) was untouched — seed 638 closes that desync; these tests
    // pin the F10-mirrored refuse guard that now also covers a placeholder profile row on any
    // OTHER company/environment where 638 hasn't (yet) run. ──────────────────────────────────

    // CompanyProfile.TaxId is a HARD field (no service-layer update path — read-only via UI in
    // Phase 1, see MasterDataServices.cs's CreateAsync comment), so tests mutate it directly,
    // mirroring WhtPayerTaxIdGuardTests' direct db.Companies.TaxId mutation for the F10 case.
    private static async Task SetCompanyProfileTaxIdAsync(ServiceProvider sp, string taxId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var companyId = s.ServiceProvider.GetRequiredService<ITenantContext>().CompanyId;
        var prof = await db.CompanyProfiles.SingleAsync(p => p.CompanyId == companyId);
        prof.TaxId = taxId;
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task T21_pnd1_and_sso_refuse_a_placeholder_company_profile_tax_id_even_though_companies_tax_id_is_real()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // A fresh TestCompanyFactory company stamps a REAL TaxId on both master.companies AND
        // master.company_profile (CompanyService.CreateAsync mirrors req.TaxId onto both) — this
        // test deliberately corrupts ONLY the profile row's copy, exactly reproducing L1-1's
        // real-world shape (companies repaired by 637, profile left behind).
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        await SetSsoEmployerAccountAsync(sp, "1234567890");   // isolate from the pre-existing H8 guard
        await SetCompanyProfileTaxIdAsync(sp, "0000000000000");
        await AddEmployee(sp, 30_000m);
        var year = await FreshYearAsync(sp);
        var period = Period(year, 4);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();

        var actMonthly = () => pnd1.BuildPnd1MonthlyAsync(runId, default);
        (await actMonthly.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("filing.payer_tax_id_missing");

        var actAnnual = () => pnd1.BuildPnd1aAnnualAsync(year, default);
        (await actAnnual.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("filing.payer_tax_id_missing");

        var actSsoFile = () => sso.BuildMonthlyFileAsync(runId, default);
        (await actSsoFile.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("filing.payer_tax_id_missing");

        var actSsoPdf = () => sso.BuildMonthlyPdfAsync(runId, default);
        (await actSsoPdf.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("filing.payer_tax_id_missing");

        // Mirrors T15 — the on-screen สปส.1-10 schedule must keep rendering so the user can see
        // what's wrong; only the two artifact builders (file/PDF) refuse.
        var model = await sso.BuildMonthlyAsync(runId, default);
        model.EmployerTaxId.Should().Be("0000000000000");
    }

    [SkippableFact]
    public async Task T22_company1_files_clean_with_the_638_repaired_tax_id_post_seed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Company 1 is the exact row seed 637/638 target — no mutation here, just proving the
        // real demo company's filings now carry 0105000000012 (not the 0000000000000 placeholder
        // the rendered PDF in findings-r2/findings-leg1.md L1-1 showed).
        await using var sp = Provider();
        await SetSsoEmployerAccountAsync(sp, "1234567890");   // company 1's profile has none seeded
        await AddEmployee(sp, 25_000m);
        var year = await FreshYearAsync(sp);
        var period = Period(year, 6);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var sso = s.ServiceProvider.GetRequiredService<ISsoFilingService>();

        var monthlyDigits = new string(PdfText(await pnd1.BuildPnd1MonthlyAsync(runId, default))
            .Where(char.IsDigit).ToArray());
        monthlyDigits.Should().Contain("0105000000012");

        var annualDigits = new string(PdfText(await pnd1.BuildPnd1aAnnualAsync(year, default))
            .Where(char.IsDigit).ToArray());
        annualDigits.Should().Contain("0105000000012");

        var model = await sso.BuildMonthlyAsync(runId, default);
        model.EmployerTaxId.Should().Be("0105000000012");
    }

    [SkippableFact]
    public async Task T23_pnd1_falls_back_to_companies_tax_id_when_the_profile_row_is_absent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        await AddEmployee(sp, 30_000m);
        var period = await FreshPeriodAsync(sp, 7);
        var runId = await RunThroughPost(sp, period);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var realTaxId = await db.Companies.AsNoTracking()
            .Where(c => c.CompanyId == company.CompanyId).Select(c => c.TaxId).SingleAsync();

        // company_profile is optional 1:1 (both filing services already null-check `prof`) —
        // deleting it exercises the `?? c?.TaxId` fallback the new guard must not break.
        var prof = await db.CompanyProfiles.SingleAsync(p => p.CompanyId == company.CompanyId);
        db.CompanyProfiles.Remove(prof);
        await db.SaveChangesAsync();

        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();
        var digits = new string(PdfText(await pnd1.BuildPnd1MonthlyAsync(runId, default))
            .Where(char.IsDigit).ToArray());
        digits.Should().Contain(realTaxId.Trim());
    }

    // R2/L1-1 extension (Fable scope ruling, 2026-08-19) — the 50ทวิ path
    // (BuildEmployeeWht50TawiAsync) has the identical unguarded fallback pattern as the three
    // originally-named call sites and is itself a compliance artifact handed to an employee, so
    // it gets the same PayerTaxIdRules guard. Same repro shape as T21: a fresh company's Tax ID
    // is real on `companies` but corrupted on `company_profile` only.
    [SkippableFact]
    public async Task T24_employee_wht50tawi_refuses_a_placeholder_company_profile_tax_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(company.CompanyId, company.BranchId);
        await SetCompanyProfileTaxIdAsync(sp, "0000000000000");
        var empId = await AddEmployee(sp, 60_000m);
        var year = await FreshYearAsync(sp);
        await RunThroughPost(sp, Period(year, 1));

        await using var s = sp.CreateAsyncScope();
        var pnd1 = s.ServiceProvider.GetRequiredService<IPnd1FilingService>();

        var act = () => pnd1.BuildEmployeeWht50TawiAsync(empId, year, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("filing.payer_tax_id_missing");
    }
}
