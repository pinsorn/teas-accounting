using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Accounting.Api.Tests.Fixtures;

/// <summary>
/// Per-company-vat-mode spec (§4.6) — VAT mode/rate/ภ.พ.30 mode live on
/// <c>master.companies</c>, so VAT-mode-dependent scenarios pick their COMPANY,
/// not an in-memory <c>Tax:VatMode</c> flag (now a silent no-op).
///
/// Non-VAT scenarios MUST run against a fresh company created here. NEVER flip
/// company 1's row: the suite shares the long-lived <c>teas_test</c> DB and other
/// collections assume company 1 is VAT-registered with the seeded master data.
///
/// Creation path: <see cref="ICompanyService.CreateAsync"/> (the real onboarding
/// path — copies the 13 default WHT types incl. SVC, the 1180 WHT-Receivable
/// account and the default tax-code set), then mirrors what company 1 got from
/// SQL seeds 120/130/240: HQ branch 00000, the minimum CoA set (+5350
/// irrecoverable-VAT expense) and one VAT-registered demo customer.
/// </summary>
public static class TestCompanyFactory
{
    public sealed record SeededCompany(int CompanyId, int BranchId, long CustomerId, string NameTh);

    /// <summary>Creates a company + HQ branch + CoA + customer and returns the ids.
    /// CAUTION: <paramref name="vatRate"/> = 0 is NOT persisted on insert — the EF
    /// mapping HasDefaultValue(0.07m) treats the decimal CLR default 0 as "unset",
    /// so the DB default 0.07 wins. Behaviour is governed by VatRegistered anyway
    /// (VatRate is "effective only when VatRegistered" per the entity doc).</summary>
    public static async Task<SeededCompany> CreateAsync(
        string connectionString, bool vatRegistered,
        decimal vatRate = 0.07m, string pnd30SubmissionMode = "manual")
    {
        var nameTh = $"บริษัททดสอบ {TestIds.Suffix()}";

        int companyId;
        await using (var sp = BuildProvider(connectionString, companyId: 1, branchId: 1))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            // Seeds 120/400 insert company_id 1/2 and branch_id 1/2 with EXPLICIT ids,
            // which does not advance the identity sequences — on a fresh teas_test the
            // first EF-generated id would collide (23505). Align sequences to MAX+1.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT setval(pg_get_serial_sequence('master.companies','company_id'), " +
                "(SELECT COALESCE(MAX(company_id),0)+1 FROM master.companies), false);" +
                "SELECT setval(pg_get_serial_sequence('master.branches','branch_id'), " +
                "(SELECT COALESCE(MAX(branch_id),0)+1 FROM master.branches), false);");

            var svc = s.ServiceProvider.GetRequiredService<ICompanyService>();
            companyId = await svc.CreateAsync(new CreateCompanyRequest(
                TestIds.TaxId(), nameTh, null, LegalEntityType.LimitedCompany,
                null, vatRegistered, vatRegistered ? new DateOnly(2020, 1, 1) : null,
                1, "99 ถ.ทดสอบ กรุงเทพฯ 10110", null, null, null, null, null, null,
                null, vatRate, pnd30SubmissionMode), default);
        }

        // Master data the sales chain needs, scoped to the NEW tenant.
        await using var sp2 = BuildProvider(connectionString, companyId, branchId: 1);
        await using var s2 = sp2.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var branch = new Branch
        {
            CompanyId = companyId, BranchCode = "00000",
            NameTh = "สำนักงานใหญ่", IsHeadOffice = true, IsActive = true,
        };
        db2.Branches.Add(branch);

        foreach (var (code, th, type, bal) in SeedAccounts)
            db2.ChartOfAccounts.Add(new ChartOfAccount
            {
                CompanyId = companyId, AccountCode = code, AccountNameTh = th,
                AccountType = type, NormalBalance = bal,
                IsHeader = false, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
            });

        var customer = new Customer
        {
            CompanyId = companyId, CustomerCode = TestIds.CustomerCode(),
            CustomerType = CustomerType.Corporate, NameTh = "ลูกค้าทดสอบ จำกัด",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
            BillingAddress = "99 ถ.ทดสอบ กรุงเทพฯ 10110",
            CreditLimit = 0, PaymentTermDays = 30, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db2.Customers.Add(customer);
        await db2.SaveChangesAsync();

        return new SeededCompany(companyId, branch.BranchId, customer.CustomerId, nameTh);
    }

    /// <summary>Infrastructure DI container with a <see cref="StubTenant"/> for the
    /// given company/branch. VAT behaviour comes from the company row (§4.6) —
    /// there is no Tax:VatMode config any more.</summary>
    public static ServiceProvider BuildProvider(
        string connectionString, int companyId, int branchId, long userId = 1)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
        }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // Mirror of seed 120 (minimum CoA) + seed 240 (5350 irrecoverable input VAT).
    // 1180 WHT-Receivable is NOT listed — CompanyService.CreateAsync already adds it.
    private static readonly (string Code, string NameTh, AccountType Type, NormalBalance Bal)[] SeedAccounts =
    [
        ("1110", "เงินสด",                     AccountType.Asset,     NormalBalance.Debit),
        ("1120", "เงินฝากธนาคาร",              AccountType.Asset,     NormalBalance.Debit),
        ("1130", "ลูกหนี้การค้า",              AccountType.Asset,     NormalBalance.Debit),
        ("1170", "ภาษีซื้อ",                   AccountType.Asset,     NormalBalance.Debit),
        ("2110", "เจ้าหนี้การค้า",             AccountType.Liability, NormalBalance.Credit),
        ("2151", "ภาษีขายค้างจ่าย",            AccountType.Liability, NormalBalance.Credit),
        ("2152", "ภาษีหัก ณ ที่จ่ายค้างจ่าย",   AccountType.Liability, NormalBalance.Credit),
        ("4000", "รายได้จากการขาย",            AccountType.Revenue,   NormalBalance.Credit),
        ("4100", "รับคืน / ส่วนลด",            AccountType.Revenue,   NormalBalance.Debit),
        ("5100", "ค่าใช้จ่ายค่าเช่า",           AccountType.Expense,   NormalBalance.Debit),
        ("5200", "ค่าใช้จ่ายค่าบริการ",         AccountType.Expense,   NormalBalance.Debit),
        ("5300", "ค่าใช้จ่ายโฆษณา",            AccountType.Expense,   NormalBalance.Debit),
        ("5350", "ภาษีซื้อขอคืนไม่ได้",         AccountType.Expense,   NormalBalance.Debit),
    ];
}
