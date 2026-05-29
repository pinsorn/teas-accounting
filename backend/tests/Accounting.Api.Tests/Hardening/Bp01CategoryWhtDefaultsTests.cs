using Accounting.Api.Tests.Fixtures;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// BP-01 (RV2) — pins the §17.3 expense-category → tax.wht_types default mapping that the
/// Payment Voucher picker pre-fills. Seed 450 wires the six unambiguous mappings; seed 460
/// adds the missing WAGE wht_types row (ม.40(2) ภ.ง.ด.3 3%) and points the WAGE category at
/// it. SAL stays NULL by design (payroll ภ.ง.ด.1 is a separate subsystem — see seed 460
/// header). One test that exists so a future ALTER/UPDATE drift on either seed fails loudly.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Bp01CategoryWhtDefaultsTests
{
    private readonly PostgresFixture _fx;
    public Bp01CategoryWhtDefaultsTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Category_wht_defaults_match_section_17_3()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var opts = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention()
            .Options;
        // Inject a super-admin tenant: the global query filter shape
        // `_tenant == null || _tenant.IsSuperAdmin || ...` works fine with null in C#, but
        // EF Core 10 captures the field reference in the cached model and NREs at translate
        // time when accessing `_tenant.IsSuperAdmin` for some compiled query paths. A super-
        // admin stub bypasses the company filter at the IsSuperAdmin short-circuit cleanly.
        await using var db = new AccountingDbContext(
            opts, new StubTenant { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = true });

        // Resolve the per-category default to its wht_types.code so the assertion reads
        // the same shape as seed 450/460 (test is also tolerant to id renumbering). The
        // join is materialized client-side — the LEFT JOIN projection through EF's nullable
        // navigation rewrite tripped a NullReferenceException in EF Core 10's query compiler
        // for the conditional shape, and the table is tiny enough that an in-memory join is
        // strictly cheaper than chasing the translator.
        var cats = await db.ExpenseCategories.AsNoTracking()
            .Where(c => c.CompanyId == 1)
            .Select(c => new { c.CategoryCode, c.DefaultWhtTypeId })
            .ToListAsync();
        var whts = await db.WhtTypes.AsNoTracking()
            .Where(w => w.CompanyId == 1)
            .ToDictionaryAsync(w => w.WhtTypeId, w => new { w.Code, w.Rate });

        var byCode = cats.ToDictionary(
            c => c.CategoryCode,
            c => new
            {
                WhtCode = c.DefaultWhtTypeId is int id && whts.TryGetValue(id, out var w) ? w.Code : null,
                WhtRate = c.DefaultWhtTypeId is int id2 && whts.TryGetValue(id2, out var w2) ? (decimal?)w2.Rate : null,
            });

        // Seed 450 — six unambiguous §17.3 mappings.
        byCode["RENT"].WhtCode.Should().Be("RENT");
        byCode["RENT"].WhtRate.Should().Be(0.05m);

        byCode["PROF"].WhtCode.Should().Be("PROF");
        byCode["PROF"].WhtRate.Should().Be(0.03m);

        byCode["LEGAL"].WhtCode.Should().Be("PROF");   // ทนาย/บัญชี = วิชาชีพอิสระ
        byCode["LEGAL"].WhtRate.Should().Be(0.03m);

        byCode["MARK"].WhtCode.Should().Be("ADS");
        byCode["MARK"].WhtRate.Should().Be(0.02m);

        byCode["INTR"].WhtCode.Should().Be("INT");
        byCode["INTR"].WhtRate.Should().Be(0.01m);

        byCode["IT"].WhtCode.Should().Be("SVC");
        byCode["IT"].WhtRate.Should().Be(0.03m);

        // Seed 460 — WAGE ค่าจ้างแรงงาน (non-employee, individual) → ภ.ง.ด.3 ม.40(2) 3%.
        byCode["WAGE"].WhtCode.Should().Be("WAGE");
        byCode["WAGE"].WhtRate.Should().Be(0.03m);

        // SAL stays NULL — PND1 payroll progressive withholding is a separate subsystem;
        // a flat per-line default would be incorrect. Pinned so a future seed that wrongly
        // maps SAL to one of the PND3/53 rows fails this assertion.
        byCode["SAL"].WhtCode.Should().BeNull();
    }

    [SkippableFact]
    public async Task Wage_wht_type_is_seeded_with_correct_form_and_rate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var opts = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention()
            .Options;
        // Inject a super-admin tenant: the global query filter shape
        // `_tenant == null || _tenant.IsSuperAdmin || ...` works fine with null in C#, but
        // EF Core 10 captures the field reference in the cached model and NREs at translate
        // time when accessing `_tenant.IsSuperAdmin` for some compiled query paths. A super-
        // admin stub bypasses the company filter at the IsSuperAdmin short-circuit cleanly.
        await using var db = new AccountingDbContext(
            opts, new StubTenant { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = true });

        var wage = await db.WhtTypes.AsNoTracking()
            .FirstOrDefaultAsync(w => w.CompanyId == 1 && w.Code == "WAGE" && w.IsActive);
        wage.Should().NotBeNull("seed 460 must insert the WAGE row");
        wage!.Rate.Should().Be(0.03m, "§17.3 ค่าจ้างแรงงาน 3%");
        wage.FormType.Should().Be(Accounting.Domain.Enums.WhtFormType.Pnd3,
            "non-employee individual payee → ภ.ง.ด.3");
        wage.IncomeTypeCode.Should().Be("8",
            "ค่าจ้างแรงงาน = รับจ้างทำของ ม.40(8) per the official RD ภ.ง.ด.3 booklet (ลำดับ 8); " +
            "income_type_code stores the ม.40 sub-section verbatim (the ภ.ง.ด.3/50ทวิ income box)");
    }
}
