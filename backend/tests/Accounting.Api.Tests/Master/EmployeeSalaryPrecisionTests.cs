using Accounting.Application.Master;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Master;

/// <summary>
/// R1/C1 (WP-3, round 2 — Fable FIX A, 2026-08-12) — <c>Employee.BaseSalary</c> had no scale
/// rule, so an employee with a &gt;2dp salary (accepted today by POST /employees) flows RAW
/// into the payroll JE line (<c>PayrollMath.MonthlyGross</c> returns a full-month
/// <c>BaseSalary</c> unrounded, by design) and now hits <c>je.precision</c> at post time —
/// refusing the WHOLE company's payroll run, naming a journal line the user cannot edit
/// instead of the employee record that caused it. Same mechanism that produced the 41 poisoned
/// <c>B1</c> fixture employees (troubles-wiki.md), but on the production-input side. Pure
/// FluentValidation unit tests — no DB, no <c>PostgresFixture</c> needed.
/// </summary>
public sealed class EmployeeSalaryPrecisionTests
{
    private static CreateEmployeeRequest CreateReq(decimal salary) => new(
        "EMP-TEST", null, "ทดสอบ", "เงินเดือน", null, null, null,
        "1234567890123", null, null,
        new DateOnly(2024, 1, 1), null, salary,
        null, null, null, false, null,
        "SINGLE", false, 0);

    private static UpdateEmployeeRequest UpdateReq(decimal salary) => new(
        null, "ทดสอบ", "เงินเดือน", null, null, null,
        "1234567890123", null, null,
        new DateOnly(2024, 1, 1), null, salary,
        null, null, null, false, null,
        "SINGLE", false, 0, IsActive: true);

    [Fact]
    public void Create_with_more_than_2dp_salary_is_rejected_at_the_edge()
    {
        // The exact fixture value that leaked 41 poisoned rows into teas_test.
        var result = new CreateEmployeeValidator().Validate(CreateReq(45_678.9012m));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BaseSalary" && e.ErrorMessage.Contains("2 decimal"));
    }

    [Fact]
    public void Update_with_more_than_2dp_salary_is_rejected_at_the_edge()
    {
        var result = new UpdateEmployeeValidator().Validate(UpdateReq(45_678.9012m));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BaseSalary" && e.ErrorMessage.Contains("2 decimal"));
    }

    [Fact]
    public void A_2dp_salary_still_validates_cleanly_on_create_and_update()
    {
        new CreateEmployeeValidator().Validate(CreateReq(45_678.90m)).IsValid.Should().BeTrue();
        new UpdateEmployeeValidator().Validate(UpdateReq(45_678.90m)).IsValid.Should().BeTrue();
    }
}
