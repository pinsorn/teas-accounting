using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

[Collection(nameof(PostgresCollection))]
public sealed class BillingNoteGenerateLinesO2bTests
{
    private readonly PostgresFixture _fx;
    public BillingNoteGenerateLinesO2bTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(TestCompanyFactory.SeededCompany c) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);

    private static CreateBillingNoteRequest Request(
        long customerId, long[]? taxInvoiceIds, IReadOnlyList<BillingLineInput>? lines = null) =>
        new(new DateOnly(2026, 7, 26), new DateOnly(2026, 8, 26), customerId, null, null,
            taxInvoiceIds, "THB", 1m, null, null, lines ?? []);

    private static BillingLineInput ManualLine(string description = "manual override") =>
        new(null, null, description, 1m, "job", 100m, 0m, 0, "VAT7", 0.07m, "SERVICE");

    private static async Task<long> SeedTaxInvoiceAsync(
        ServiceProvider sp, TestCompanyFactory.SeededCompany c,
        decimal subtotal, decimal vat, decimal total, DateOnly? docDate = null,
        int taxCodeId = 7, string? taxCode = null, decimal? taxRate = null)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var suffix = TestIds.Suffix();
        var ti = new TaxInvoice
        {
            CompanyId = c.CompanyId,
            BranchId = c.BranchId,
            DocNo = $"TI-O2B-{suffix}",
            DocDate = docDate ?? new DateOnly(2026, 7, 20),
            TaxPointDate = docDate ?? new DateOnly(2026, 7, 20),
            SupplierTaxId = TestIds.TaxId(),
            SupplierBranchCode = "00000",
            SupplierBranchName = "HQ",
            SupplierName = c.NameTh,
            SupplierAddress = "test supplier address",
            CustomerId = c.CustomerId,
            CustomerTaxId = "0105556123453",
            CustomerName = "test customer",
            CustomerAddress = "test customer address",
            CustomerVatRegistered = vat != 0m,
            SubtotalAmount = subtotal,
            TaxableAmount = subtotal,
            TaxAmount = vat,
            TotalAmount = total,
            TotalAmountThb = total,
            Status = DocumentStatus.Posted,
        };
        ti.Lines.Add(new TaxInvoiceLine
        {
            LineNo = 1,
            DescriptionTh = "รายการต้นทาง",
            Quantity = 1m,
            UomText = "รายการ",
            UnitPrice = subtotal,
            LineAmount = subtotal,
            TaxCodeId = taxCodeId,
            TaxCode = taxCode ?? (vat == 0m ? "VAT0" : "VAT7"),
            TaxRate = taxRate ?? (vat == 0m ? 0m : 0.07m),
            TaxAmount = vat,
            TotalAmount = total,
        });
        db.TaxInvoices.Add(ti);
        await db.SaveChangesAsync();
        return ti.TaxInvoiceId;
    }

    private static async Task<BillingNote> LoadAsync(ServiceProvider sp, long id)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.BillingNotes.AsNoTracking()
            .Include(b => b.Lines).Include(b => b.TaxInvoiceLinks)
            .SingleAsync(b => b.BillingNoteId == id);
    }

    [SkippableFact]
    public async Task Update_with_two_linked_tax_invoices_and_no_manual_lines_generates_two_lines_and_exact_sums()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(c);
        var ti1 = await SeedTaxInvoiceAsync(sp, c, 1000.11m, 70.01m, 1070.12m);
        var ti2 = await SeedTaxInvoiceAsync(sp, c, 234.56m, 16.42m, 250.98m);
        long bnId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await svc.CreateDraftAsync(Request(c.CustomerId, null, [ManualLine("initial")] ), default);
            await svc.UpdateDraftAsync(bnId, Request(c.CustomerId, [ti1, ti2]), default);
        }

        var bn = await LoadAsync(sp, bnId);
        bn.TaxInvoiceLinks.Select(x => x.TaxInvoiceId).Should().BeEquivalentTo([ti1, ti2]);
        bn.Lines.Should().HaveCount(2).And.OnlyContain(l => l.TaxInvoiceId == ti1 || l.TaxInvoiceId == ti2);
        bn.SubtotalAmount.Should().Be(1234.67m);
        bn.VatAmount.Should().Be(86.43m);
        bn.TotalAmount.Should().Be(1321.10m);
        bn.Lines.Sum(l => l.LineAmount).Should().Be(bn.SubtotalAmount);
        bn.Lines.Sum(l => l.TaxAmount).Should().Be(bn.VatAmount);
        bn.Lines.Sum(l => l.TotalAmount).Should().Be(bn.TotalAmount);
        var generated = bn.Lines.Single(l => l.TaxInvoiceId == ti1);
        generated.DescriptionTh.Should().StartWith("ใบกำกับภาษี TI-O2B-")
            .And.EndWith("ลงวันที่ 20/07/2026");
        generated.UomText.Should().Be("ฉบับ");
        generated.TaxCodeId.Should().Be(7);
        generated.TaxCode.Should().Be("VAT7");
        generated.TaxRate.Should().Be(0.07m);
    }

    [SkippableFact]
    public async Task Create_with_links_and_manual_lines_preserves_only_the_manual_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(c);
        var ti = await SeedTaxInvoiceAsync(sp, c, 500m, 35m, 535m);
        long bnId;
        await using (var scope = sp.CreateAsyncScope())
            bnId = await scope.ServiceProvider.GetRequiredService<IBillingNoteService>()
                .CreateDraftAsync(Request(c.CustomerId, [ti], [ManualLine()]), default);

        var bn = await LoadAsync(sp, bnId);
        bn.TaxInvoiceLinks.Should().ContainSingle();
        bn.Lines.Should().ContainSingle(l => l.DescriptionTh == "manual override" && l.TaxInvoiceId == null);
        bn.SubtotalAmount.Should().Be(100m);
        bn.VatAmount.Should().Be(7m);
        bn.TotalAmount.Should().Be(107m);
    }

    [SkippableFact]
    public async Task Create_for_non_vat_company_copies_zero_vat_and_total_still_ties()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = Provider(c);
        var ti1 = await SeedTaxInvoiceAsync(sp, c, 400.25m, 0m, 400.25m);
        var ti2 = await SeedTaxInvoiceAsync(sp, c, 99.75m, 0m, 99.75m);
        long bnId;
        await using (var scope = sp.CreateAsyncScope())
            bnId = await scope.ServiceProvider.GetRequiredService<IBillingNoteService>()
                .CreateDraftAsync(Request(c.CustomerId, [ti1, ti2]), default);

        var bn = await LoadAsync(sp, bnId);
        bn.VatAmount.Should().Be(0m);
        bn.SubtotalAmount.Should().Be(500m);
        bn.TotalAmount.Should().Be(500m);
        bn.Lines.Sum(l => l.TotalAmount).Should().Be(bn.TotalAmount);
    }

    [SkippableFact]
    public async Task Requested_other_tenant_tax_invoice_is_skipped_from_links_lines_and_totals()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spA = Provider(a);
        await using var spB = Provider(b);
        var ownTi = await SeedTaxInvoiceAsync(spA, a, 200m, 14m, 214m);
        var otherTi = await SeedTaxInvoiceAsync(spB, b, 600m, 42m, 642m);
        long bnId;
        await using (var scope = spA.CreateAsyncScope())
            bnId = await scope.ServiceProvider.GetRequiredService<IBillingNoteService>()
                .CreateDraftAsync(Request(a.CustomerId, [ownTi, otherTi]), default);

        var bn = await LoadAsync(spA, bnId);
        bn.TaxInvoiceLinks.Should().ContainSingle(l => l.TaxInvoiceId == ownTi);
        bn.Lines.Should().ContainSingle(l => l.TaxInvoiceId == ownTi);
        bn.Lines.Should().NotContain(l => l.TaxInvoiceId == otherTi);
        bn.SubtotalAmount.Should().Be(200m);
        bn.VatAmount.Should().Be(14m);
        bn.TotalAmount.Should().Be(214m);
    }

    [SkippableFact]
    public async Task Issued_note_rejects_update_that_would_generate_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(c);
        var ti = await SeedTaxInvoiceAsync(sp, c, 100m, 7m, 107m);
        long bnId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await svc.CreateDraftAsync(Request(c.CustomerId, null, [ManualLine()]), default);
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bn = await db.BillingNotes.SingleAsync(x => x.BillingNoteId == bnId);
            bn.Status = BillingNoteStatus.Issued;
            await db.SaveChangesAsync();
        }

        await using var editScope = sp.CreateAsyncScope();
        var editSvc = editScope.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var act = () => editSvc.UpdateDraftAsync(bnId, Request(c.CustomerId, [ti]), default);
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("billing_note.cannot_edit_after_issue");
    }

    [SkippableFact]
    public async Task Create_without_linked_tax_invoices_keeps_existing_manual_line_behavior()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = Provider(c);
        long bnId;
        await using (var scope = sp.CreateAsyncScope())
            bnId = await scope.ServiceProvider.GetRequiredService<IBillingNoteService>()
                .CreateDraftAsync(Request(c.CustomerId, null, [ManualLine("no links")]), default);

        var bn = await LoadAsync(sp, bnId);
        bn.TaxInvoiceLinks.Should().BeEmpty();
        bn.Lines.Should().ContainSingle(l => l.DescriptionTh == "no links" && l.TaxInvoiceId == null);
        bn.SubtotalAmount.Should().Be(100m);
        bn.VatAmount.Should().Be(7m);
        bn.TotalAmount.Should().Be(107m);
    }
}