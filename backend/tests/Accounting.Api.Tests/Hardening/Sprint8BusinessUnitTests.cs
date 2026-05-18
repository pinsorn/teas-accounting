using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint-8: Business Unit as the first wired GL dimension. Real Postgres.
/// Covers: company opt-in enforcement (service layer), invalid/inactive BU rejection,
/// duplicate-code guard, soft-deactivate, GL snapshot integrity onto every journal_line,
/// single-BU vs cross-BU receipt resolution, BU list filter + include_unspecified,
/// and posted-TI BU immutability (trigger).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint8BusinessUnitTests
{
    private readonly PostgresFixture _fx;
    public Sprint8BusinessUnitTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        return services
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string NewCode(string prefix) =>
        (prefix + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();

    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    private static async Task<int> CreateBu(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBusinessUnitService>();
        return await svc.CreateAsync(new CreateBusinessUnitRequest(code, "หน่วย " + code, code, null), default);
    }

    private static CreateTaxInvoiceRequest TiReq(long customerId, int? buId) =>
        new(DocDate: new DateOnly(2026, 5, 16), CustomerId: customerId,
            IsTaxInclusive: false, CurrencyCode: "THB", ExchangeRate: 1m,
            Notes: null, PaymentTerms: null, DueDate: null,
            Lines: [new TaxInvoiceLineInput(null, null, "สินค้า", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
            BusinessUnitId: buId);

    private static async Task<(long id, string docNo)> PostTi(
        ServiceProvider sp, long customerId, int? buId)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(TiReq(customerId, buId), default);
        var res = await svc.PostAsync(id, default);
        return (id, res.DocNo);
    }

    private static async Task SetRequiresBu(ServiceProvider sp, bool value)
    {
        await using var s = sp.CreateAsyncScope();
        await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .SetCompanyRequiresBuAsync(value, default);
    }

    [SkippableFact]
    public async Task Flag_off_allows_ti_without_bu()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);

        var (id, docNo) = await PostTi(sp, cust, buId: null);

        docNo.Should().NotBeNullOrEmpty();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.TaxInvoices.Where(t => t.TaxInvoiceId == id)
            .Select(t => t.BusinessUnitId).FirstAsync()).Should().BeNull();
    }

    [SkippableFact]
    public async Task Flag_on_requires_bu_on_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        try
        {
            await SetRequiresBu(sp, true);

            await using (var s = sp.CreateAsyncScope())
            {
                var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
                var act = () => svc.CreateDraftAsync(TiReq(cust, buId: null), default);
                (await act.Should().ThrowAsync<DomainException>())
                    .Which.Code.Should().Be("bu.required");
            }

            var buId = await CreateBu(sp, NewCode("ECOM"));
            var (_, docNo) = await PostTi(sp, cust, buId);
            docNo.Should().NotBeNullOrEmpty();
        }
        finally
        {
            await SetRequiresBu(sp, false); // shared company 1 — restore default
        }
    }

    [SkippableFact]
    public async Task Inactive_bu_rejected_on_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var buId = await CreateBu(sp, NewCode("LAB"));

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
                .DeactivateAsync(buId, default);

        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var act = () => svc.CreateDraftAsync(TiReq(cust, buId), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("bu.invalid");
    }

    [SkippableFact]
    public async Task Duplicate_bu_code_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var code = NewCode("REPT");
        await CreateBu(sp, code);

        var act = () => CreateBu(sp, code);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("bu.duplicate");
    }

    [SkippableFact]
    public async Task Deactivate_is_soft_and_keeps_historical_reference()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var buId = await CreateBu(sp, NewCode("ECOM"));
        var (tiId, _) = await PostTi(sp, cust, buId);

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
                .DeactivateAsync(buId, default);

        await using var s2 = sp.CreateAsyncScope();
        var svc = s2.ServiceProvider.GetRequiredService<IBusinessUnitService>();
        var detail = await svc.GetAsync(buId, default);
        detail.Should().NotBeNull();
        detail!.IsActive.Should().BeFalse();
        (await svc.ListAsync(includeInactive: false, default))
            .Any(b => b.BusinessUnitId == buId).Should().BeFalse();
        (await svc.ListAsync(includeInactive: true, default))
            .Any(b => b.BusinessUnitId == buId).Should().BeTrue();

        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.TaxInvoices.Where(t => t.TaxInvoiceId == tiId)
            .Select(t => t.BusinessUnitId).FirstAsync()).Should().Be(buId);
    }

    [SkippableFact]
    public async Task Posted_ti_snapshots_bu_onto_every_journal_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var buId = await CreateBu(sp, NewCode("ECOM"));
        var (_, docNo) = await PostTi(sp, cust, buId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == docNo);
        je.Lines.Should().NotBeEmpty();
        je.Lines.Should().OnlyContain(l => l.BusinessUnitId == buId);
    }

    [SkippableFact]
    public async Task Single_bu_receipt_inherits_shared_bu()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var buId = await CreateBu(sp, NewCode("LAB"));
        var (tiId, _) = await PostTi(sp, cust, buId);

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
            DocDate: new DateOnly(2026, 5, 16), CustomerId: cust,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Applications: [new ReceiptApplicationInput(tiId, 1070m)],
            BusinessUnitId: null), default);
        var res = await rsvc.PostAsync(rcId, default);

        res.CrossesBusinessUnits.Should().BeFalse();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Receipts.Where(r => r.ReceiptId == rcId)
            .Select(r => r.BusinessUnitId).FirstAsync()).Should().Be(buId);
    }

    [SkippableFact]
    public async Task Cross_bu_receipt_header_null_and_per_line_bu()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var buA = await CreateBu(sp, NewCode("ECOM"));
        var buB = await CreateBu(sp, NewCode("LAB"));
        var (tiA, _) = await PostTi(sp, cust, buA);
        var (tiB, _) = await PostTi(sp, cust, buB);

        await using var s = sp.CreateAsyncScope();
        var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
            DocDate: new DateOnly(2026, 5, 16), CustomerId: cust,
            PaymentMethod: PaymentMethod.Transfer, ChequeNo: null, ChequeDate: null,
            BankAccountId: null, CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
            Applications:
            [
                new ReceiptApplicationInput(tiA, 1070m),
                new ReceiptApplicationInput(tiB, 1070m),
            ],
            BusinessUnitId: null), default);
        var res = await rsvc.PostAsync(rcId, default);

        res.CrossesBusinessUnits.Should().BeTrue();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Receipts.Where(r => r.ReceiptId == rcId)
            .Select(r => r.BusinessUnitId).FirstAsync()).Should().BeNull("cross-BU header is unspecified");

        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == res.DocNo);
        // Cash/bank line is fungible → BU NULL; AR-clearing lines carry each TI's BU.
        je.Lines.Should().Contain(l => l.CreditAmount == 0m && l.BusinessUnitId == null);
        je.Lines.Should().Contain(l => l.CreditAmount == 1070m && l.BusinessUnitId == buA);
        je.Lines.Should().Contain(l => l.CreditAmount == 1070m && l.BusinessUnitId == buB);
    }

    [SkippableFact]
    public async Task Ti_list_filters_by_bu_and_include_unspecified()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var buId = await CreateBu(sp, NewCode("REPT"));
        var (withBu, _) = await PostTi(sp, cust, buId);
        var (noBu, _) = await PostTi(sp, cust, buId: null);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();

        var filtered = await svc.ListAsync(new TaxInvoiceListQuery(
            null, null, null, null, null, 100, buId, false), default);
        filtered.Items.Should().Contain(i => i.TaxInvoiceId == withBu);
        filtered.Items.Should().NotContain(i => i.TaxInvoiceId == noBu);

        var withUnspec = await svc.ListAsync(new TaxInvoiceListQuery(
            null, null, null, null, null, 100, buId, true), default);
        withUnspec.Items.Should().Contain(i => i.TaxInvoiceId == withBu);
        withUnspec.Items.Should().Contain(i => i.TaxInvoiceId == noBu);
    }

    [SkippableFact]
    public async Task Posted_ti_bu_is_immutable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var buId = await CreateBu(sp, NewCode("ECOM"));
        var (tiId, _) = await PostTi(sp, cust, buId);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "UPDATE sales.tax_invoices SET business_unit_id = NULL WHERE tax_invoice_id = {0}",
            tiId);
        await act.Should().ThrowAsync<Exception>("the immutability trigger blocks BU changes on a posted TI");

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db2.TaxInvoices.Where(t => t.TaxInvoiceId == tiId)
            .Select(t => t.BusinessUnitId).FirstAsync()).Should().Be(buId);
    }
}
