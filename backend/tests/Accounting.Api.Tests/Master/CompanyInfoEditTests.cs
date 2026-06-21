using System.Linq;
using System.Threading.Tasks;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Master;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Audit;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Master;

/// <summary>
/// Company-info edit (super-admin) — corrects the founding legal identity + VAT/tax config + address
/// (the previously-deferred /hard path). Verifies it rewrites BOTH master.companies and
/// company_profiles, audits the change (§4.6 tax_config_change + §4.8 CompanyInfoChanged), and
/// rejects an invalid tax id / branch code. §4.2 is preserved out-of-band: posted tax invoices
/// snapshot the supplier identity, so this only affects future documents (not asserted here).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompanyInfoEditTests(PostgresFixture fx)
{
    // A 13-digit Thai tax id with a valid mod-11 checksum (TestIds.TaxId() is random/unchecked;
    // the service validates the checksum so the test must supply a real one).
    private static string ValidTaxId()
    {
        var d = new int[13];
        d[0] = 1;
        for (var i = 1; i < 12; i++) d[i] = Random.Shared.Next(0, 10);
        var sum = 0;
        for (var i = 0; i < 12; i++) sum += d[i] * (13 - i);
        d[12] = (11 - (sum % 11)) % 10;
        return string.Concat(d);
    }

    private static UpdateCompanyInfoRequest ValidReq(string taxId) => new(
        LegalName: "บริษัท แก้ชื่อใหม่ จำกัด", NameEn: "Renamed Co Ltd", TaxId: taxId,
        RegistrationNumber: null, LegalEntityType: LegalEntityType.LimitedPartnership, BranchCode: "00001",
        VatRegistered: true, VatRate: 0.07m, Pnd30SubmissionMode: "auto",
        VatRegisterDate: new DateOnly(2021, 1, 1),
        Building: null, RoomNo: null, Floor: null, Village: null, HouseNo: "123", Moo: null, Soi: null,
        Street: "ถ.ใหม่", Subdistrict: "แขวงใหม่", District: "เขตใหม่", Province: "เชียงใหม่", PostalCode: "50000");

    [SkippableFact]
    public async Task UpdateCompanyInfo_rewrites_company_and_profile_and_audits()
    {
        Skip.If(fx.SkipReason is not null, fx.SkipReason);
        var seeded = await TestCompanyFactory.CreateAsync(fx.ConnectionString, vatRegistered: true);
        var newTaxId = ValidTaxId();

        await using (var sp = TestCompanyFactory.BuildProvider(fx.ConnectionString, seeded.CompanyId, branchId: 1))
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ICompanyProfileService>();
            await svc.UpdateCompanyInfoAsync(ValidReq(newTaxId), default);
        }

        await using (var sp = TestCompanyFactory.BuildProvider(fx.ConnectionString, seeded.CompanyId, branchId: 1))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

            var company = await db.Companies.IgnoreQueryFilters().FirstAsync(c => c.CompanyId == seeded.CompanyId);
            company.NameTh.Should().Be("บริษัท แก้ชื่อใหม่ จำกัด");
            company.TaxId.Should().Be(newTaxId);
            company.LegalEntityType.Should().Be(LegalEntityType.LimitedPartnership);
            company.Pnd30SubmissionMode.Should().Be("auto");

            var profile = await db.CompanyProfiles.IgnoreQueryFilters().FirstAsync(p => p.CompanyId == seeded.CompanyId);
            profile.LegalName.Should().Be("บริษัท แก้ชื่อใหม่ จำกัด");
            profile.TaxId.Should().Be(newTaxId);
            profile.BranchCode.Should().Be("00001");
            profile.RegistrationNumber.Should().Be(newTaxId, "registration number defaults to the tax id when blank");
            profile.RegisteredProvince.Should().Be("เชียงใหม่");

            var actions = await db.Set<ActivityLog>().IgnoreQueryFilters()
                .Where(a => a.CompanyId == seeded.CompanyId)
                .Select(a => a.ActivityType).ToListAsync();
            actions.Should().Contain("tax_config_change", "§4.6 — a VAT/tax-config change must be audited");
            actions.Should().Contain("CompanyInfoChanged", "§4.8 — a founding-identity change must be audited");
        }
    }

    [SkippableFact]
    public async Task UpdateCompanyInfo_rejects_invalid_taxid_and_branch()
    {
        Skip.If(fx.SkipReason is not null, fx.SkipReason);
        var seeded = await TestCompanyFactory.CreateAsync(fx.ConnectionString, vatRegistered: true);

        await using var sp = TestCompanyFactory.BuildProvider(fx.ConnectionString, seeded.CompanyId, branchId: 1);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ICompanyProfileService>();

        // Bad tax id (fails the 13-digit checksum).
        var badTax = () => svc.UpdateCompanyInfoAsync(ValidReq(ValidTaxId()) with { TaxId = "1234567890123" }, default);
        await badTax.Should().ThrowAsync<DomainException>();

        // Bad branch code (not 5 digits) — valid tax id so validation reaches the branch check.
        var badBranch = () => svc.UpdateCompanyInfoAsync(ValidReq(ValidTaxId()) with { BranchCode = "1" }, default);
        await badBranch.Should().ThrowAsync<DomainException>();
    }
}
