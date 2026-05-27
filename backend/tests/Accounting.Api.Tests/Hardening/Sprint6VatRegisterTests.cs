using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Purchase;
using Accounting.Application.Reports;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Audit;
using Accounting.Infrastructure.Ledger;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Purchase;
using Accounting.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint-6B: input-VAT register sources from VendorInvoice.vat_claim_period
/// (ม.82/4), not PV / doc_date.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint6VatRegisterTests
{
    private readonly PostgresFixture _fx;
    public Sprint6VatRegisterTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider() =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<IClock, SystemClock>()
            .AddSingleton(Options.Create(new GlAccountsOptions()))
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .AddDbContext<AccountingDbContext>(o =>
                o.UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention())
            .AddScoped<INumberSequenceService, NumberSequenceService>()
            .AddScoped<IGlPostingService, GlPostingService>()
            .AddScoped<IPeriodCloseService, PeriodCloseService>()
            .AddScoped<IActivityRecorder, ActivityRecorder>()
            .AddScoped<IVendorInvoiceService, VendorInvoiceService>()
            .AddScoped<IVatReportService, VatReportService>()
            .BuildServiceProvider();

    private async Task<(long vendorId, int recCat, int nonRecCat)> Seed(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var exp = await db.ChartOfAccounts
            .Where(a => a.CompanyId == 1 && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = "VR-" + Guid.NewGuid().ToString("N")[..8],
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขาย VR",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var rec = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = "VRR" + Guid.NewGuid().ToString("N")[..6],
            NameTh = "rec", DefaultExpenseAccountId = exp, DefaultIsRecoverableVat = true,
        };
        var non = new ExpenseCategory
        {
            CompanyId = 1, CategoryCode = "VRN" + Guid.NewGuid().ToString("N")[..6],
            NameTh = "nonrec", DefaultExpenseAccountId = exp, DefaultIsRecoverableVat = false,
        };
        db.Vendors.Add(v); db.ExpenseCategories.AddRange(rec, non);
        await db.SaveChangesAsync();
        return (v.VendorId, rec.CategoryId, non.CategoryId);
    }

    private async Task<string> PostVi(
        ServiceProvider sp, long vendorId, int catId, decimal amount, decimal vatRate,
        DateOnly docDate, DateOnly vendorTiDate, int? claim, bool post = true)
    {
        var no = "VR-" + Guid.NewGuid().ToString("N")[..8];
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            DocDate: docDate, VendorId: vendorId, VendorTaxInvoiceNo: no,
            VendorTaxInvoiceDate: vendorTiDate, VatClaimPeriod: claim,
            CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Lines: [new VendorInvoiceLineInput(catId, null, "l", amount, vatRate)]), default);
        if (post) await svc.PostAsync(id, default);
        return no;
    }

    [SkippableFact]
    public async Task Register_filters_by_vat_claim_period_two_periods()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, recCat, _) = await Seed(sp);

        var noMar = await PostVi(sp, vendorId, recCat, 1000m, 0.07m,
            new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 1), 202603);
        var noApr = await PostVi(sp, vendorId, recCat, 2000m, 0.07m,
            new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 1), 202604);

        await using var s = sp.CreateAsyncScope();
        var rpt = s.ServiceProvider.GetRequiredService<IVatReportService>();

        var mar = await rpt.GetRegisterAsync(2026, 3, default);
        mar.Purchase.Select(p => p.DocNo).Should().Contain(noMar).And.NotContain(noApr);
        var apr = await rpt.GetRegisterAsync(2026, 4, default);
        apr.Purchase.Select(p => p.DocNo).Should().Contain(noApr).And.NotContain(noMar);
    }

    [SkippableFact]
    public async Task NonRecoverable_only_vi_excluded_from_input_register()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, recCat, nonRecCat) = await Seed(sp);

        var rec = await PostVi(sp, vendorId, recCat, 1000m, 0.07m,
            new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 1), 202606);
        var non = await PostVi(sp, vendorId, nonRecCat, 1000m, 0.07m,
            new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 2), 202606);

        await using var s = sp.CreateAsyncScope();
        var rpt = s.ServiceProvider.GetRequiredService<IVatReportService>();
        var reg = await rpt.GetRegisterAsync(2026, 6, default);

        reg.Purchase.Select(p => p.DocNo).Should().Contain(rec).And.NotContain(non,
            "a VI with no recoverable VAT carries no claimable input VAT");
        reg.Purchase.Where(p => p.DocNo == rec).Sum(p => p.RecoverableVat).Should().Be(70m);
    }

    [SkippableFact]
    public async Task Draft_vi_excluded_from_register()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, recCat, _) = await Seed(sp);

        var posted = await PostVi(sp, vendorId, recCat, 1000m, 0.07m,
            new DateOnly(2026, 7, 5), new DateOnly(2026, 7, 1), 202607);
        var draft = await PostVi(sp, vendorId, recCat, 9000m, 0.07m,
            new DateOnly(2026, 7, 6), new DateOnly(2026, 7, 2), 202607, post: false);

        await using var s = sp.CreateAsyncScope();
        var rpt = s.ServiceProvider.GetRequiredService<IVatReportService>();
        var reg = await rpt.GetRegisterAsync(2026, 7, default);

        reg.Purchase.Select(p => p.DocNo).Should().Contain(posted).And.NotContain(draft);
    }

    [SkippableFact]
    public async Task Register_keys_off_claim_period_not_doc_date()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var (vendorId, recCat, _) = await Seed(sp);

        // Vendor TI Jan 2026 → default claim 202601; recorded (doc_date) Apr 2026.
        var no = await PostVi(sp, vendorId, recCat, 1000m, 0.07m,
            docDate: new DateOnly(2026, 4, 20),
            vendorTiDate: new DateOnly(2026, 1, 15), claim: null);

        await using var s = sp.CreateAsyncScope();
        var rpt = s.ServiceProvider.GetRequiredService<IVatReportService>();

        (await rpt.GetRegisterAsync(2026, 1, default)).Purchase
            .Select(p => p.DocNo).Should().Contain(no, "claim period 202601 drives the register");
        (await rpt.GetRegisterAsync(2026, 4, default)).Purchase
            .Select(p => p.DocNo).Should().NotContain(no, "doc_date month must NOT drive it");
    }
}
