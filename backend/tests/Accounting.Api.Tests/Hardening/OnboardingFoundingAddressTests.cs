using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Onboarding founding-address gap (ม.86/4). A freshly-created company MUST get a
/// <c>master.company_profile</c> row whose granular registered-address boxes (Reg*) are populated
/// from the create request, with the legacy free-text <c>RegisteredAddressLine1</c> composed from
/// the same parts — otherwise every RD form (ภ.พ.30 / ภ.ง.ด.3/53/54 / 50ทวิ) renders blank address
/// boxes for that tenant. Exercises the REAL <see cref="ICompanyService.CreateAsync"/> path (not a
/// fixture shortcut), which is the only place the founding profile is written.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class OnboardingFoundingAddressTests
{
    private readonly PostgresFixture _fx;
    public OnboardingFoundingAddressTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task CreateAsync_writes_full_granular_founding_address_into_company_profile()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var taxId = TestIds.TaxId();
        var nameTh = $"บริษัทผู้ก่อตั้ง {TestIds.Suffix()}";

        int companyId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 1, branchId: 1))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            // Align identity sequences after the explicit-id seeds (same reason as TestCompanyFactory).
            await db.Database.ExecuteSqlRawAsync(
                "SELECT setval(pg_get_serial_sequence('master.companies','company_id'), " +
                "(SELECT COALESCE(MAX(company_id),0)+1 FROM master.companies), false);");

            var svc = s.ServiceProvider.GetRequiredService<ICompanyService>();
            companyId = await svc.CreateAsync(new CreateCompanyRequest(
                TaxId: taxId, NameTh: nameTh, NameEn: "Founder Co., Ltd.",
                LegalEntityType: LegalEntityType.LimitedCompany,
                RegistrationDate: new DateOnly(2024, 1, 15),
                VatRegistered: true, VatRegisterDate: new DateOnly(2024, 2, 1),
                FiscalYearStartMonth: 1,
                // companies tails / shared address parts
                AddressTh: null, SubDistrict: "ทุ่งมหาเมฆ", District: "เขตสาทร",
                Province: "กรุงเทพมหานคร", PostalCode: "10120",
                Phone: "02-111-2222", Email: "founder@example.co.th",
                PaidUpCapital: 1_000_000m, VatRate: 0.07m, Pnd30SubmissionMode: "manual",
                // granular street-level founding-address parts
                RegHouseNo: "99/1", RegMoo: "5", RegSoi: "สุขใจ 3", RegStreet: "สาทรใต้",
                RegBuilding: "อาคารเอ", RegRoomNo: "1203", RegFloor: "12", RegVillage: "บ้านสวน"),
                default);
        }

        // Read the founding profile back with a super-admin tenant (bypasses the company filter).
        var opts = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention()
            .Options;
        await using var rdb = new AccountingDbContext(
            opts, new StubTenant { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = true });

        var prof = await rdb.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CompanyId == companyId);

        prof.Should().NotBeNull("CreateAsync must create the 1:1 company_profile row (ม.86/4 founding identity)");

        // Required hard fields are populated.
        prof!.LegalName.Should().Be(nameTh);
        prof.TaxId.Should().Be(taxId);
        prof.RegistrationNumber.Should().Be(taxId);
        prof.BranchCode.Should().Be("00000");
        prof.VatRegistrationDate.Should().Be(new DateOnly(2024, 2, 1));

        // Granular registered-address boxes — each part in its own field for the RD forms.
        prof.RegHouseNo.Should().Be("99/1");
        prof.RegMoo.Should().Be("5");
        prof.RegSoi.Should().Be("สุขใจ 3");
        prof.RegStreet.Should().Be("สาทรใต้");
        prof.RegBuilding.Should().Be("อาคารเอ");
        prof.RegRoomNo.Should().Be("1203");
        prof.RegFloor.Should().Be("12");
        prof.RegVillage.Should().Be("บ้านสวน");
        prof.RegisteredSubdistrict.Should().Be("ทุ่งมหาเมฆ");
        prof.RegisteredDistrict.Should().Be("เขตสาทร");
        prof.RegisteredProvince.Should().Be("กรุงเทพมหานคร");
        prof.RegisteredPostalCode.Should().Be("10120");

        // Legacy free-text Line1 (Tax Invoice / e-Tax) is composed from the same parts, identical
        // to the ภ.พ.09 hard-edit composer — must contain the house no + the อาคาร/ถ. tokens.
        prof.RegisteredAddressLine1.Should().NotBeNullOrWhiteSpace();
        prof.RegisteredAddressLine1.Should().Contain("99/1");
        prof.RegisteredAddressLine1.Should().Contain("อาคารอาคารเอ");
        prof.RegisteredAddressLine1.Should().Contain("ถ.สาทรใต้");
        prof.RegisteredAddressLine1.Should().Be(ThaiRegisteredAddress.ComposeLine1(
            "99/1", "อาคารเอ", "1203", "12", "บ้านสวน", "5", "สุขใจ 3", "สาทรใต้"));

        // companies.AddressTh mirrors the composed founding line (general display).
        var company = await rdb.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId);
        company!.AddressTh.Should().Be(prof.RegisteredAddressLine1);
    }

    // A freshly onboarded company must get the FULL chart of accounts the GL posting engine
    // resolves (every GlAccountsOptions code) — not just 1180. The prod bug: a receipt post on a
    // real onboarded tenant 422'd with gl.account_missing '1130' (AR) because CreateAsync seeded
    // only 1180, on the (wrong) assumption a SQL demo seed supplied the rest.
    [SkippableFact]
    public async Task CreateAsync_seeds_full_chart_of_accounts_for_gl_posting()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var taxId = TestIds.TaxId();
        int companyId;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 1, branchId: 1))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "SELECT setval(pg_get_serial_sequence('master.companies','company_id'), " +
                "(SELECT COALESCE(MAX(company_id),0)+1 FROM master.companies), false);");

            var svc = s.ServiceProvider.GetRequiredService<ICompanyService>();
            companyId = await svc.CreateAsync(new CreateCompanyRequest(
                TaxId: taxId, NameTh: $"บ.ผังบัญชี {TestIds.Suffix()}", NameEn: null,
                LegalEntityType: LegalEntityType.LimitedCompany,
                RegistrationDate: null, VatRegistered: true, VatRegisterDate: new DateOnly(2024, 1, 1),
                FiscalYearStartMonth: 1,
                AddressTh: null, SubDistrict: "x", District: "y",
                Province: "กรุงเทพมหานคร", PostalCode: "10110", Phone: null, Email: null,
                PaidUpCapital: null, VatRate: 0.07m, Pnd30SubmissionMode: "manual"),
                default);
        }

        var opts = new DbContextOptionsBuilder<AccountingDbContext>()
            .UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention()
            .Options;
        await using var rdb = new AccountingDbContext(
            opts, new StubTenant { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = true });

        var codes = await rdb.ChartOfAccounts.AsNoTracking()
            .Where(a => a.CompanyId == companyId).Select(a => a.AccountCode).ToListAsync();

        // Every GL role the posting engine resolves must exist, or the first journal entry 422s.
        var gl = new GlAccountsOptions();
        var required = new[]
        {
            gl.ArAccount, gl.ApAccount, gl.CashAccount, gl.BankAccount, gl.SalesAccount,
            gl.OutputVatAccount, gl.InputVatAccount, gl.WhtPayableAccount, gl.WhtReceivableAccount,
            gl.SalesReturnAccount, gl.IrrecoverableVatExpenseAccount, gl.SalaryExpenseAccount,
            gl.EmployerSsoExpenseAccount, gl.PitPayableAccount, gl.SsoPayableAccount, gl.NetWagesPayableAccount,
        };
        codes.Should().Contain(required,
            "a freshly onboarded company must carry every GlAccountsOptions code (else GL posting 422s)");
        codes.Should().Contain("1130", "AR is the account the prod receipt-post hit");
    }

    [SkippableFact]
    public async Task CreateAsync_rejects_missing_province_or_postal_via_validator()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var v = new CreateCompanyValidator();

        var noProvince = NewReq() with { Province = null };
        v.Validate(noProvince).IsValid.Should().BeFalse("Province is the founding registered address (ม.86/4)");

        var badPostal = NewReq() with { PostalCode = "123" };
        v.Validate(badPostal).IsValid.Should().BeFalse("PostalCode must be 5 digits");

        var ok = NewReq();
        v.Validate(ok).IsValid.Should().BeTrue("a full valid founding address passes");
    }

    // A real, checksum-valid Thai Tax ID — this test exercises the Province/PostalCode rules,
    // NOT uniqueness, so it must use a value that passes ThaiTaxId.TryParse deterministically
    // (TestIds.TaxId() is random and intentionally fails the checksum → would flake the run).
    private static CreateCompanyRequest NewReq() => new(
        TaxId: "0105556123453", NameTh: "บ.ทดสอบ", NameEn: null,
        LegalEntityType: LegalEntityType.LimitedCompany,
        RegistrationDate: null, VatRegistered: true, VatRegisterDate: new DateOnly(2024, 1, 1),
        FiscalYearStartMonth: 1,
        AddressTh: null, SubDistrict: "x", District: "y", Province: "กรุงเทพมหานคร", PostalCode: "10110",
        Phone: null, Email: null);
}
