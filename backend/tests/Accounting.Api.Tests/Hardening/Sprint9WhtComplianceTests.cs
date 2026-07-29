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

    // N3 (Tier-2 round 2) — mirrors the internal TaxFilingPeriod.MonthRange (not accessible
    // cross-project) so T6's baseline query can match the generators' own month-wide scope.
    private static (DateOnly from, DateOnly to) MonthRange(int period)
    {
        var (y, m) = (period / 100, period % 100);
        return (new DateOnly(y, m, 1), new DateOnly(y, m, DateTime.DaysInMonth(y, m)));
    }

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

    // T5/T6/T7 (specs/pnd2-filing.md, I2/I3) — B1's GeneratePnd2Async, added alongside the three
    // existing generators, must keep the four-way partition exact: no omission, no double count,
    // and totals tie to the underlying certs. Uses RandPeriod() (§1.6.6) to dodge shared-teas_test
    // residue from other tests' Pnd3/Pnd53/Pnd54 fixtures at the same fixed period.
    [SkippableFact]
    public async Task Pnd2_generator_partitions_exactly_with_no_double_count_or_omission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var period = RandPeriod();
        var d = PeriodDate(period);
        await AddCert(sp, d, CustomerType.Individual, WhtFormType.Pnd2,  1000m, 0.15m);  // 150
        await AddCert(sp, d, CustomerType.Individual, WhtFormType.Pnd3, 10000m, 0.03m);  // 300
        await AddCert(sp, d, CustomerType.Corporate,  WhtFormType.Pnd53, 20000m, 0.03m); // 600
        await AddCert(sp, d, CustomerType.Corporate,  WhtFormType.Pnd54, 30000m, 0.15m); // 4500

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var p2  = await svc.GeneratePnd2Async(period, TaxFilingMode.Preview, default);
        var p3  = await svc.GeneratePnd3Async(period, TaxFilingMode.Preview, default);
        var p53 = await svc.GeneratePnd53Async(period, TaxFilingMode.Preview, default);
        var p54 = await svc.GeneratePnd54Async(period, TaxFilingMode.Preview, default);

        // T5 — no double count: the Pnd2 cert appears ONLY on ภ.ง.ด.2.
        p2.Rows.Should().ContainSingle(r => r.WhtAmount == 150m);
        p3.Rows.Should().NotContain(r => r.WhtAmount == 150m);
        p53.Rows.Should().NotContain(r => r.WhtAmount == 150m);
        p54.Rows.Should().NotContain(r => r.WhtAmount == 150m);

        // T6 — no omission: the four generators' counts sum to the total posted count in the
        // period (same tenant scope as the generators — no IgnoreQueryFilters). N3 (Tier-2
        // round 2) — baseline scoped to the full MonthRange, matching what the generators
        // themselves query (was a single day, `d`), so same-month residue from other tests
        // can never silently understate the baseline relative to what the generators return.
        var (monthFrom, monthTo) = MonthRange(period);
        var totalPosted = await db.WhtCertificates.CountAsync(w =>
            w.Direction == "P" && w.Status == DocumentStatus.Posted
            && w.CertDate >= monthFrom && w.CertDate <= monthTo);
        (p2.Rows.Count + p3.Rows.Count + p53.Rows.Count + p54.Rows.Count)
            .Should().Be(totalPosted);

        // T7 — totals tie to the underlying certificates exactly.
        p2.Totals.Income.Should().Be(1000m);
        p2.Totals.Wht.Should().Be(150m);
    }

    // N1 (Tier-2 round 2, specs/pnd2-filing.md §10) — IRdEfilingClient has no SubmitPnd2Async
    // arm. Before the fix, finalizing ภ.ง.ด.2 under a company with Pnd30SubmissionMode="auto"
    // silently faked a submitted filing: TaxFilingStore.SubmitAsync's unknown-form default
    // returned Submitted:false, but FinalizeAsync never checked res.Submitted before recording
    // Status="Submitted"/SubmittedAt=now/RdAckRef="" — immutable forever. ภ.ง.ด.2 must ALWAYS
    // take the manual finalize path regardless of the company's submission mode. Uses a FRESH
    // company (TestCompanyFactory) — company 1's row must never be flipped (shared teas_test).
    [SkippableFact]
    public async Task Pnd2_finalize_always_manual_even_under_company_auto_mode()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var company = await TestCompanyFactory.CreateAsync(
            _fx.ConnectionString, vatRegistered: true, pnd30SubmissionMode: "auto");
        await using var sp = TestCompanyFactory.BuildProvider(
            _fx.ConnectionString, company.CompanyId, company.BranchId);
        var period = RandPeriod();

        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.WhtCertificates.Add(new WhtCertificate
            {
                CompanyId = company.CompanyId, BranchId = company.BranchId,
                DocNo = "WT-" + Sfx(), CertDate = PeriodDate(period),
                Direction = "P", PayerTaxId = "0105500000001", PayerBranchCode = "00000",
                PayerName = "TEAS Co", PayerAddress = "BKK",
                PayeeTaxId = "1234567890123", PayeeName = "กรรมการทดสอบ",
                PayeeAddress = "BKK", PayeeType = CustomerType.Individual, FormType = WhtFormType.Pnd2,
                IncomeTypeCode = "4", Pnd2IncomeCode = "2",
                IncomeAmount = 1000m, WhtRate = 0.15m, WhtAmount = 150m,
                Status = DocumentStatus.Posted, IssuedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        await svc.GeneratePnd2Async(period, TaxFilingMode.Finalize, default);

        var db2 = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db2.TaxFilings.SingleAsync(f => f.FormType == "PND2" && f.Period == period);
        row.Status.Should().Be("Finalized",
            "ภ.ง.ด.2 has no RD auto-submit client yet — must always take the manual finalize " +
            "path regardless of the company's Pnd30SubmissionMode");
        row.SubmittedAt.Should().BeNull();
        row.RdAckRef.Should().BeNull();
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
