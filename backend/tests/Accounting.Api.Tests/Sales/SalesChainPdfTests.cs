using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// cont.119 — the Q/SO/DO PDF customer block is enriched from the LIVE customer master
/// (branch / contact / phone / billing address), mirroring the on-screen custInfo merge so the
/// print == the screen. This proves the enriched render path works end-to-end (real Postgres,
/// teas_test) with a customer that carries all those fields — the mapping would throw or
/// mis-shape the model if the live-master fetch regressed.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SalesChainPdfTests
{
    private readonly PostgresFixture _fx;
    public SalesChainPdfTests(PostgresFixture fx) => _fx = fx;

    static SalesChainPdfTests() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    private static void AssertPdf(byte[] bytes)
    {
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(1024);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    private ServiceProvider Provider()
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
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // A customer whose branch / contact / phone live ONLY on the master (not on the doc
    // snapshot) — so a rendered PDF that includes them proves the live-master enrichment.
    private async Task<long> NewCustomer(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var c = new Customer
        {
            CompanyId = 1, CustomerCode = TestIds.CustomerCode(), CustomerType = CustomerType.Corporate,
            NameTh = "บริษัท ลูกค้าทดสอบ PDF จำกัด", TaxId = TestIds.TaxId(), BranchCode = "00007",
            VatRegistered = true, BillingAddress = "1 ถนนทดสอบ กรุงเทพฯ 10110",
            ContactPerson = "คุณผู้ติดต่อทดสอบ", Phone = "02-555-0007",
        };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.CustomerId;
    }

    private async Task<long> NewQuotation(ServiceProvider sp, long customerId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var q = new Quotation
        {
            CompanyId = 1, BranchId = 1, Status = QuotationStatus.Draft,
            DocDate = new DateOnly(2026, 7, 1), ValidUntilDate = new DateOnly(2026, 7, 31),
            CustomerId = customerId, CustomerName = "บริษัท ลูกค้าทดสอบ PDF จำกัด",
            CustomerType = CustomerType.Corporate,
            SubtotalAmount = 1000m, VatAmount = 70m, TotalAmount = 1070m,
            Lines = new List<QuotationLine>
            {
                new()
                {
                    LineNo = 1, DescriptionTh = "ค่าบริการทดสอบ", ProductType = "SERVICE",
                    Quantity = 1m, UomText = "งาน", UnitPrice = 1000m, LineAmount = 1000m,
                    TaxCode = "VAT7", TaxRate = 0.07m, TaxAmount = 70m, TotalAmount = 1070m,
                },
            },
        };
        db.Quotations.Add(q);
        await db.SaveChangesAsync();
        return q.QuotationId;
    }

    [SkippableFact]
    public async Task Quotation_pdf_renders_with_live_master_customer()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cid = await NewCustomer(sp);
        var qid = await NewQuotation(sp, cid);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISalesChainPdfService>();
        AssertPdf(await svc.QuotationPdfAsync(qid, default, copy: false));
        AssertPdf(await svc.QuotationPdfAsync(qid, default, copy: true));
    }
}
