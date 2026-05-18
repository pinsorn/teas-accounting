using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 9 Part C — ภ.ง.ด.3/53/54 routing by payee type + ภ.พ.36
/// reverse-charge auto-JV (Dr 1170 / Cr 2151, net 0) + immutable history.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint9WhtComplianceTests
{
    private readonly PostgresFixture _fx;
    public Sprint9WhtComplianceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    // PostgresFixture persists inserted rows across runs; finalize is immutable
    // per (form, period). Use a unique far-future period so re-runs never collide.
    private static int RandPeriod()
    {
        var r = Random.Shared;
        return (3000 + r.Next(0, 6000)) * 100 + r.Next(1, 13);
    }
    private static DateOnly PeriodDate(int period) =>
        new(period / 100, period % 100, 10);

    private static async Task AddCert(
        ServiceProvider sp, DateOnly date, CustomerType payee, WhtFormType form,
        decimal income, decimal rate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.WhtCertificates.Add(new WhtCertificate
        {
            CompanyId = 1, BranchId = 1, DocNo = "WT-" + Sfx(), CertDate = date,
            Direction = "P", PayerTaxId = "0105500000001", PayerBranchCode = "00000",
            PayerName = "TEAS Co", PayerAddress = "BKK",
            PayeeTaxId = "1234567890123", PayeeName = "Payee " + Sfx(),
            PayeeAddress = "BKK", PayeeType = payee, FormType = form,
            IncomeTypeCode = "3", IncomeAmount = income, WhtRate = rate,
            WhtAmount = decimal.Round(income * rate, 2),
            Status = DocumentStatus.Posted, IssuedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(default);
    }

    [SkippableFact]
    public async Task Pnd3_53_54_route_by_payee_type_and_form()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var d = new DateOnly(2026, 8, 12);
        await AddCert(sp, d, CustomerType.Individual, WhtFormType.Pnd3,  10000m, 0.03m);
        await AddCert(sp, d, CustomerType.Corporate,  WhtFormType.Pnd53, 20000m, 0.03m);
        await AddCert(sp, d, CustomerType.Corporate,  WhtFormType.Pnd54, 30000m, 0.15m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();

        var p3  = await svc.GeneratePnd3Async(202608, TaxFilingMode.Preview, default);
        var p53 = await svc.GeneratePnd53Async(202608, TaxFilingMode.Preview, default);
        var p54 = await svc.GeneratePnd54Async(202608, TaxFilingMode.Preview, default);

        p3.Rows.Should().NotBeEmpty();
        p3.Totals.Income.Should().BeGreaterThanOrEqualTo(10000m);
        p53.Totals.Income.Should().BeGreaterThanOrEqualTo(20000m);
        p54.Totals.Wht.Should().BeGreaterThanOrEqualTo(4500m);  // 30000 * 0.15
        // ภ.ง.ด.3 must exclude the Pnd54 foreign cert (its 4500 WHT row).
        p3.Rows.Should().NotContain(r => r.WhtAmount == 4500m);
        p53.Rows.Should().NotContain(r => r.WhtAmount == 4500m);
        p54.Rows.Should().Contain(r => r.WhtAmount == 4500m);
        p3.FilingDueDate.Day.Should().Be(7);
    }

    [SkippableFact]
    public async Task Pnd36_finalize_posts_balanced_reverse_charge_jv()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();
        var dd = PeriodDate(period);

        long vendorId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var ven = new Vendor
            {
                CompanyId = 1, VendorCode = "V-" + Sfx(), NameTh = "AWS",
                IsForeign = true, CountryCode = "US",
                VatRegistered = true,  // ck_vendors_foreign_vatreg: foreign ⇒ VAT-registered
            };
            db.Vendors.Add(ven);
            await db.SaveChangesAsync(default);
            vendorId = ven.VendorId;

            db.VendorInvoices.Add(new VendorInvoice
            {
                CompanyId = 1, BranchId = 1, DocNo = "VI-" + Sfx(),
                DocDate = dd,
                VendorTaxInvoiceNo = "AWS-" + Sfx(),
                VendorTaxInvoiceDate = dd,
                VatClaimPeriod = period, VendorId = vendorId, VendorName = "AWS",
                VendorType = CustomerType.Corporate,
                CurrencyCode = "THB", ExchangeRate = 1m,
                SubtotalAmount = 10000m, VatAmount = 0m,
                NonRecoverableVatAmount = 0m, TotalAmount = 10000m,
                TotalAmountThb = 10000m, HasInputVat = false,
                RequiresPnd36ReverseCharge = true,
                SettledAmount = 0m, SettlementStatus = "UNPAID",
                Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(default);
        }

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        var f = await svc.GeneratePnd36Async(period, TaxFilingMode.Finalize, default);

        f.Status.Should().BeOneOf("Finalized", "Submitted");
        f.TotalVat.Should().Be(700m);                 // 10000 * 0.07
        f.ReverseChargeJournalId.Should().NotBeNull();

        var db2 = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var jl = await (
            from l in db2.JournalLines.AsNoTracking()
            join a in db2.ChartOfAccounts.AsNoTracking() on l.AccountId equals a.AccountId
            where l.JournalId == f.ReverseChargeJournalId
            select new { a.AccountCode, l.DebitAmount, l.CreditAmount })
            .ToListAsync(default);

        jl.Sum(x => x.DebitAmount).Should().Be(jl.Sum(x => x.CreditAmount), "JV must balance");
        jl.Single(x => x.AccountCode == "1170").DebitAmount.Should().Be(700m);
        jl.Single(x => x.AccountCode == "2151").CreditAmount.Should().Be(700m);

        (await db2.TaxFilings.AnyAsync(x => x.FormType == "PND36" && x.Period == period))
            .Should().BeTrue();

        var act = () => svc.GeneratePnd36Async(period, TaxFilingMode.Finalize, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("tax_filing.already_finalized");
    }

    [SkippableFact]
    public async Task Tax_filing_history_lists_finalized_records()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();
        await AddCert(sp, PeriodDate(period),
            CustomerType.Individual, WhtFormType.Pnd3, 5000m, 0.03m);

        await using var s = sp.CreateAsyncScope();
        var wht = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        await wht.GeneratePnd3Async(period, TaxFilingMode.Finalize, default);

        var tax = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
        var hist = await tax.ListAsync(default);
        hist.Should().Contain(h => h.FormType == "PND3" && h.Period == period
            && (h.Status == "Finalized" || h.Status == "Submitted"));
    }
}
