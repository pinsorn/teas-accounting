using Accounting.Api.Tests.Fixtures;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

[Collection(nameof(PostgresCollection))]
public class TenantIsolationTests
{
    private readonly PostgresFixture _fx;
    public TenantIsolationTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Customer_from_company_A_is_invisible_to_company_B()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Randomized per run so the suite re-runs cleanly on a re-used DB
        // (Answer-Backend1 §3.2 / Report-Backend1 §2.9 idempotency fix).
        var companyA = Random.Shared.Next(500_000, 699_999);
        var companyB = companyA + 1;
        var custCode = "ISO-" + Guid.NewGuid().ToString("N")[..12];
        // tax_id has a UNIQUE index — derive distinct 13-digit ids per run for re-run safety.
        var taxA = companyA.ToString().PadLeft(13, '0');
        var taxB = companyB.ToString().PadLeft(13, '0');

        var spA = _fx.BuildServiceProvider(new StubTenant { CompanyId = companyA, UserId = 1 });
        var spB = _fx.BuildServiceProvider(new StubTenant { CompanyId = companyB, UserId = 1 });

        await using (var sA = spA.CreateAsyncScope())
        {
            var db = sA.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.Companies.Add(new Company { CompanyId = companyA, TaxId = taxA, NameTh = "ABC", LegalEntityType = LegalEntityType.LimitedCompany });
            db.Companies.Add(new Company { CompanyId = companyB, TaxId = taxB, NameTh = "XYZ", LegalEntityType = LegalEntityType.LimitedCompany });
            await db.SaveChangesAsync();

            db.Customers.Add(new Customer { CompanyId = companyA, CustomerCode = custCode, CustomerType = CustomerType.Corporate, NameTh = "Cust-A" });
            await db.SaveChangesAsync();
        }

        await using var sB = spB.CreateAsyncScope();
        var dbB = sB.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var visible = await dbB.Customers.AnyAsync(c => c.CustomerCode == custCode);
        visible.Should().BeFalse("EF global query filter must hide cross-tenant rows");
    }
}
