using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 8.6 — AR-side WHT. Real Postgres. GL must stay balanced
/// (Dr Bank cash_received + Dr 1180 WHT-Recv = Cr AR Σapplied); a
/// WhtCertificate Direction='R' is recorded; effective-date rate changes
/// don't recalc posted docs.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint86ArWhtTests
{
    private readonly PostgresFixture _fx;
    public Sprint86ArWhtTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // §14 (resolved Sprint 14.5): route through the shared TestIds helper so
    // randomization lives in one place. 6-upper preserves prior code shape.
    private static string Sfx() => TestIds.Suffix()[..6].ToUpperInvariant();

    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    private static async Task<int> SvcWhtTypeId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.WhtTypes.Where(w => w.Code == "SVC" && w.EffectiveTo == null)
            .Select(w => w.WhtTypeId).FirstAsync();
    }

    private static CreateTaxInvoiceRequest TiReq(long cust, int? bu = null) =>
        new(new DateOnly(2026, 5, 16), cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "svc", 1m, 1, "ชิ้น", 10000m, 0m, 1, "VAT7", 0.07m)],
            bu);

    private static async Task<long> PostTi(ServiceProvider sp, long cust, int? bu = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(TiReq(cust, bu), default);
        await svc.PostAsync(id, default);
        return id;
    }

    private static CreateReceiptRequest RcReq(
        long cust, long tiId, decimal applied, decimal wht = 0, int? whtType = null) =>
        new(new DateOnly(2026, 5, 16), cust, PaymentMethod.Transfer, null, null, null,
            "THB", 1m, null, [new ReceiptApplicationInput(tiId, applied)], null,
            wht, whtType, wht > 0 ? $"WHT-{Sfx()}" : null,
            wht > 0 ? new DateOnly(2026, 5, 16) : null);

    [SkippableFact]
    public async Task Receipt_without_wht_unchanged_no_regression()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var ti = await PostTi(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc.CreateDraftAsync(RcReq(cust, ti, 10700m), default);
        var res = await rsvc.PostAsync(rcId, default);

        res.WhtAmount.Should().Be(0m);
        res.CashReceived.Should().Be(10700m);
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == res.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit).And.Be(10700m);
        je.Lines.Should().NotContain(l => l.Description!.Contains("WHT receivable"));
        (await db.WhtCertificates.CountAsync(w => w.ReceiptId == rcId)).Should().Be(0);
    }

    [SkippableFact]
    public async Task Receipt_with_wht_posts_balanced_gl_and_cert_R()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var wt = await SvcWhtTypeId(sp);
        var ti = await PostTi(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        // 10,700 applied; customer withholds 300 (3% of 10,000 service base).
        var rcId = await rsvc.CreateDraftAsync(RcReq(cust, ti, 10700m, 300m, wt), default);
        var res = await rsvc.PostAsync(rcId, default);

        res.WhtAmount.Should().Be(300m);
        res.CashReceived.Should().Be(10400m);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == res.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        (je.TotalDebit - je.TotalCredit).Should().BeLessThan(0.01m);
        je.Lines.Should().Contain(l => l.DebitAmount == 10400m);            // bank
        je.Lines.Should().Contain(l => l.DebitAmount == 300m
            && l.Description!.Contains("WHT receivable"));                  // 1180
        je.Lines.Should().Contain(l => l.CreditAmount == 10700m);           // AR

        var cert = await db.WhtCertificates.FirstAsync(w => w.ReceiptId == rcId);
        cert.Direction.Should().Be("R");
        cert.PaymentVoucherId.Should().BeNull();
        cert.WhtAmount.Should().Be(300m);
        cert.DocNo.Should().StartWith("WHT-");
    }

    [SkippableFact]
    public async Task Wht_exceeding_amount_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var wt = await SvcWhtTypeId(sp);
        var ti = await PostTi(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var act = () => rsvc.CreateDraftAsync(RcReq(cust, ti, 1000m, 2000m, wt), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("rc.wht_exceeds_amount");
    }

    [SkippableFact]
    public async Task Wht_amount_without_type_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var ti = await PostTi(sp, cust);

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var act = () => rsvc.CreateDraftAsync(RcReq(cust, ti, 10700m, 300m, null), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("rc.wht_type_invalid");
    }

    [SkippableFact]
    public async Task WhtType_change_rate_closes_old_and_opens_new()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var code = "EVT" + Sfx();

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtTypeService>();
        var id = await svc.CreateAsync(new CreateWhtTypeRequest(
            code, "อีเวนต์", "Event", "3", "PND53", 0.05m), default);

        (await svc.ResolveAtDateAsync(code, new DateOnly(2026, 5, 31), default))!
            .Rate.Should().Be(0.05m);

        await svc.ChangeRateAsync(id,
            new ChangeWhtRateRequest(0.04m, new DateOnly(2026, 6, 1)), default);

        (await svc.ResolveAtDateAsync(code, new DateOnly(2026, 5, 31), default))!
            .Rate.Should().Be(0.05m, "before the change date keeps the old rate");
        (await svc.ResolveAtDateAsync(code, new DateOnly(2026, 6, 1), default))!
            .Rate.Should().Be(0.04m, "on/after the change date uses the new rate");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var rows = await db.WhtTypes.Where(w => w.Code == code)
            .OrderBy(w => w.EffectiveFrom).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].EffectiveTo.Should().Be(new DateOnly(2026, 5, 31));
        rows[1].EffectiveTo.Should().BeNull();
    }

    [SkippableFact]
    public async Task WhtType_deactivate_excluded_from_active_list()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var code = "TMP" + Sfx();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtTypeService>();
        var id = await svc.CreateAsync(new CreateWhtTypeRequest(
            code, "ชั่วคราว", null, "3", "PND53", 0.03m), default);

        await svc.DeactivateAsync(id, default);

        (await svc.ListAsync(includeInactive: false, default))
            .Any(w => w.WhtTypeId == id).Should().BeFalse();
        (await svc.ListAsync(includeInactive: true, default))
            .Any(w => w.WhtTypeId == id).Should().BeTrue();
    }

    [SkippableFact]
    public async Task Cross_bu_receipt_with_wht_keeps_per_app_bu_and_null_wht_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var wt = await SvcWhtTypeId(sp);

        int buA, buB;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var bsvc = s0.ServiceProvider.GetRequiredService<Accounting.Application.Master.IBusinessUnitService>();
            buA = await bsvc.CreateAsync(new Accounting.Application.Master.CreateBusinessUnitRequest(
                "WBUA" + Sfx(), "A", null, null), default);
            buB = await bsvc.CreateAsync(new Accounting.Application.Master.CreateBusinessUnitRequest(
                "WBUB" + Sfx(), "B", null, null), default);
        }
        var tiA = await PostTi(sp, cust, buA);
        var tiB = await PostTi(sp, cust, buB);

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
            new DateOnly(2026, 5, 16), cust, PaymentMethod.Transfer, null, null, null,
            "THB", 1m, null,
            [new ReceiptApplicationInput(tiA, 10700m), new ReceiptApplicationInput(tiB, 10700m)],
            null, 600m, wt, "WHT-" + Sfx(), new DateOnly(2026, 5, 16)), default);
        var res = await rsvc.PostAsync(rcId, default);

        res.CrossesBusinessUnits.Should().BeTrue();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Receipts.Where(r => r.ReceiptId == rcId)
            .Select(r => r.BusinessUnitId).FirstAsync()).Should().BeNull();

        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == res.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit);
        // Cash + WHT-Recv lines carry no BU; AR-clearing lines carry each TI's BU.
        je.Lines.Should().Contain(l => l.DebitAmount > 0m
            && l.Description!.Contains("WHT receivable") && l.BusinessUnitId == null);
        je.Lines.Should().Contain(l => l.CreditAmount == 10700m && l.BusinessUnitId == buA);
        je.Lines.Should().Contain(l => l.CreditAmount == 10700m && l.BusinessUnitId == buB);
    }
}
