using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Accounting.Api.Tests.Rbac;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Master;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// PLAN-test-hardening.md WS-4 / C3 — cross-SEED consistency invariants that no existing test
/// checks. Pure C# assertions: reflects into <see cref="CompanyService"/>'s private default
/// seed tables and reads the 450 seed script's mapping VALUES as plain text. NO DB connection
/// anywhere in this file (unlike every other test in this folder) — cheap, always runs (even
/// with Postgres unavailable), and can never be masked by the superuser-bypasses-RLS blind spot
/// that hides most seed-script bugs from the rest of the suite. Exactly the class of check that
/// would have caught INTR→5200 (a category pointing at an account/WHT code nothing actually
/// seeds, silently falling back instead of failing loud) at write time, for pennies.
/// </summary>
public sealed class SeedConsistencyTests
{
    private static T[] SeedArray<T>(string fieldName) =>
        (T[])typeof(CompanyService)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    [Fact]
    public void Every_expense_category_preferred_account_exists_in_default_chart_of_accounts()
    {
        var coa = SeedArray<(string Code, string Th, string? En, AccountType Type, NormalBalance Normal)>(
            "DefaultChartOfAccounts");
        var specs = SeedArray<(string Code, string Th, string? En, string PreferredAcct,
            bool Recoverable, bool Capex, bool Cogs)>("DefaultExpenseCategorySpecs");

        var coaCodes = coa.Select(a => a.Code).ToHashSet();
        coaCodes.Should().NotBeEmpty();
        specs.Should().NotBeEmpty();

        foreach (var spec in specs)
            coaCodes.Should().Contain(spec.PreferredAcct,
                $"expense category {spec.Code}'s preferred account {spec.PreferredAcct} must be " +
                "seeded in DefaultChartOfAccounts, or CreateAsync silently falls back to account 5200 " +
                "(exactly the INTR→5200 mis-seed class)");
    }

    [Fact]
    public void Every_wht_type_pnd2_income_code_is_set_iff_form_type_is_pnd2_and_rate_is_a_fraction()
    {
        var whtTypes = SeedArray<(string Code, string Th, string? En, string Inc,
            WhtFormType Form, decimal Rate, string? Pnd2Inc)>("DefaultWhtTypes");
        whtTypes.Should().NotBeEmpty();

        foreach (var w in whtTypes)
        {
            (w.Pnd2Inc is not null).Should().Be(w.Form == WhtFormType.Pnd2,
                $"{w.Code}: Pnd2IncomeCode must be set exactly when FormType is Pnd2 (was {w.Form})");
            w.Rate.Should().BeInRange(0m, 1m,
                $"{w.Code}: WHT rate must be a fraction (0..1), not a whole percent");
        }
    }

    [Fact]
    public void Seed_450_category_wht_defaults_reference_codes_that_exist_in_default_wht_types()
    {
        var whtTypes = SeedArray<(string Code, string Th, string? En, string Inc,
            WhtFormType Form, decimal Rate, string? Pnd2Inc)>("DefaultWhtTypes");
        var whtCodes = whtTypes.Select(w => w.Code).ToHashSet();

        var sqlPath = Path.Combine(RbacTestPaths.RepoRoot(), "backend", "src",
            "Accounting.Infrastructure", "Migrations", "SqlScripts",
            "450_seed_category_wht_defaults.sql");
        File.Exists(sqlPath).Should().BeTrue($"seed script must exist at {sqlPath}");
        var sql = File.ReadAllText(sqlPath);

        // The mapping VALUES rows look like ('RENT',  'RENT'), i.e. (category_code, wht_code).
        var pairs = Regex.Matches(sql, @"\(\s*'([A-Z0-9-]+)'\s*,\s*'([A-Z0-9-]+)'\s*\)")
            .Select(m => (CatCode: m.Groups[1].Value, WhtCode: m.Groups[2].Value))
            .ToList();
        pairs.Should().NotBeEmpty(
            "regression guard against the mapping VALUES clause being reshaped unnoticed — " +
            "if this is empty the regex stopped matching the SQL, not that the seed is empty");

        foreach (var (catCode, whtCode) in pairs)
            whtCodes.Should().Contain(whtCode,
                $"seed 450 maps expense category {catCode} to tax.wht_types.code '{whtCode}', " +
                "which must exist in DefaultWhtTypes");
    }
}
