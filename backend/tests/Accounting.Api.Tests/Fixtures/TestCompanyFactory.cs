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
/// path — copies the 13 default WHT types incl. SVC, the default tax-code set, and
/// the full default chart of accounts covering every GlAccountsOptions code), then
/// adds an HQ branch 00000 and one VAT-registered demo customer for the sales chain.
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
            // Province + PostalCode are now required (ม.86/4 founding registered address) — every
            // company gets a real address + an auto-created company_profile.
            companyId = await svc.CreateAsync(new CreateCompanyRequest(
                TestIds.TaxId(), nameTh, null, LegalEntityType.LimitedCompany,
                null, vatRegistered, vatRegistered ? new DateOnly(2020, 1, 1) : null,
                1, "99 ถ.ทดสอบ กรุงเทพฯ 10110", "ทุ่งมหาเมฆ", "เขตสาทร", "กรุงเทพมหานคร", "10110",
                null, null, null, vatRate, pnd30SubmissionMode), default);
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
        // CoA is fully seeded by ICompanyService.CreateAsync above (every GlAccountsOptions
        // code). The factory no longer mirrors a CoA subset — doing so now collides on the
        // unique (company_id, account_code).

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

}
