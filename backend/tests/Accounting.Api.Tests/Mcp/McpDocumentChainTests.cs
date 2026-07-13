using System.Net.Http;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Accounting.Api.Tests.Mcp;

/// <summary>
/// specs/mcp-document-chain.md D8 — the new sales/purchase document-chain builders + MCP tool
/// wiring. Money-critical JE pins (D3 a/b/c/d) run at the SERVICE layer directly (same
/// convention as InvoiceFlowTests.cs / PurchaseCompletenessTests.cs) — this is the shortest,
/// most precise path to prove the REUSED GlPostingService paths fire correctly through the
/// NEW "from-source" builders. Tool-surface behaviors that only exist at the MCP layer
/// (create_invoice_draft's VAT-mode polymorphism + [mcp.domain_rule] wrapping, tenancy scoping,
/// get_document_status's new types, get_workflow_guide content, DraftCreated shape) run through
/// the real MCP client (McpApiFactory), mirroring McpServerSmokeTests.cs.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class McpDocumentChainTests
{
    private const string ApiKeyHeader = "X-Api-Key";
    private readonly PostgresFixture _fx;
    public McpDocumentChainTests(PostgresFixture fx) => _fx = fx;

    // ── generic helpers ────────────────────────────────────────────────────

    private static async Task<long> AccountIdAsync(ServiceProvider sp, int companyId, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();
    }

    private sealed record JeLine(long AccountId, decimal Debit, decimal Credit);

    private static async Task<(decimal TotalDebit, decimal TotalCredit, List<JeLine> Lines)> JournalAsync(
        ServiceProvider sp, string reference)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.Include(j => j.Lines)
            .FirstAsync(j => j.Reference == reference);
        return (je.TotalDebit, je.TotalCredit,
            je.Lines.Select(l => new JeLine(l.AccountId, l.DebitAmount, l.CreditAmount)).ToList());
    }

    // ── sales-chain helper: Q(accept) → SO(posted), one line ──────────────────

    private static async Task<long> QuotationToPostedSoAsync(
        ServiceProvider sp, long customerId, DateOnly today,
        string productType, decimal unitPrice, decimal taxRate)
    {
        await using var s = sp.CreateAsyncScope();
        var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
            today, today.AddDays(30), customerId, null, "THB", 1m, null, null,
            [new ChainLineInput(null, "รายการทดสอบ", 1m, "ชิ้น", unitPrice, 0m, 1,
                taxRate > 0 ? "VAT7" : "VAT0", taxRate, productType)]), default);
        await qSvc.SendAsync(qId, default);
        await qSvc.AcceptAsync(qId, default);
        var soId = await qSvc.ConvertToSalesOrderAsync(qId, default);
        var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
        await soSvc.PostAsync(soId, default);
        return soId;
    }

    // Builds the DO exactly like the MCP tool's full-qty wrapper (links every SO line).
    private static async Task<long> CreateAndIssueDoFromSoAsync(ServiceProvider sp, long soId, DateOnly today)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var so = await db.SalesOrders.Include(x => x.Lines).FirstAsync(x => x.SalesOrderId == soId);
        var req = new CreateDeliveryOrderRequest(
            today, so.CustomerId, so.BusinessUnitId, false, null, soId,
            so.Lines.OrderBy(l => l.LineNo).Select(l => new DeliveryLineInput(
                l.LineId, l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate, l.ProductType)).ToList());
        var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var doId = await soSvc.CreateDeliveryOrderAsync(soId, req, default);
        var doSvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
        await doSvc.IssueAsync(doId, default);
        return doId;
    }

    private async Task<long> CustomerIdAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CompanyId == companyId)
            .Select(c => c.CustomerId).FirstAsync();
    }

    private static DateOnly Today(ServiceProvider sp)
    {
        using var s = sp.CreateScope();
        return s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
    }

    // ── purchase-chain helpers ─────────────────────────────────────────────

    private static async Task<long> SeedVendorAsync(ServiceProvider sp, int companyId, bool vatRegistered = true)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = companyId, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายทดสอบ MCP chain",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = vatRegistered,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private static async Task<(int CatId, long ExpAcct)> SeedExpenseCategoryAsync(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var cat = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = TestIds.ExpenseCategoryCode(),
            NameTh = "หมวดทดสอบ MCP chain", DefaultExpenseAccountId = expAcct,
            DefaultIsRecoverableVat = true,
        };
        db.ExpenseCategories.Add(cat);
        await db.SaveChangesAsync();
        return (cat.CategoryId, expAcct);
    }

    private static async Task<int> SeedWhtTypeAsync(ServiceProvider sp, int companyId, decimal rate)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var w = new WhtType
        {
            CompanyId = companyId, Code = TestIds.WhtTypeCode(), NameTh = "ค่าบริการทดสอบ MCP chain",
            IncomeTypeCode = "2", FormType = WhtFormType.Pnd53, Rate = rate,
        };
        db.WhtTypes.Add(w);
        await db.SaveChangesAsync();
        return w.WhtTypeId;
    }

    private static async Task<long> CreateAndApprovePoAsync(
        ServiceProvider sp, long vendorId, DateOnly today, decimal netAmount, decimal taxRate)
    {
        await using var s = sp.CreateAsyncScope();
        var poSvc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var poId = await poSvc.CreateDraftAsync(new CreatePurchaseOrderRequest(
            today, null, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(null, "รายการซื้อทดสอบ", 1m, "ชิ้น", netAmount, 0m,
                null, taxRate > 0 ? "VAT7" : "VAT0", taxRate, null)]), default);
        await poSvc.ApproveAsync(poId, default);
        return poId;
    }

    // ── D8 #1 — VAT sales happy path: pins D3(a) (Dr 1120/Dr 1180/Cr 1130; no Cr 4000) ──

    [SkippableFact]
    public async Task Vat_sales_chain_settles_ti_with_customer_wht_pins_D3a_je()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);

        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "GOOD", 100_000m, 0.07m);
        await using (var s = sp.CreateAsyncScope())
        {
            var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            (await soSvc.GetAsync(soId, default))!.DeliveryRequired.Should().BeTrue("a GOOD line requires delivery");
        }
        var doId = await CreateAndIssueDoFromSoAsync(sp, soId, today);

        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await tiSvc.CreateFromDeliveryOrderAsync(doId, default);
            await tiSvc.PostAsync(tiId, default);
        }

        var whtTypeId = await SeedWhtTypeAsync(sp, co.CompanyId, 0.03m);
        string rcDocNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var rcSvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var rcId = await rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
                Applications: [new ReceiptApplicationInput(TaxInvoiceId: tiId, AppliedAmount: 107_000m)],
                WhtLines: [new ReceiptWhtLineInput(whtTypeId, 100_000m)]), default);
            var posted = await rcSvc.PostAsync(rcId, default);
            rcDocNo = posted.DocNo;
        }

        var (bank, whtRecv, ar, sales) = (
            await AccountIdAsync(sp, co.CompanyId, "1120"), await AccountIdAsync(sp, co.CompanyId, "1180"),
            await AccountIdAsync(sp, co.CompanyId, "1130"), await AccountIdAsync(sp, co.CompanyId, "4000"));
        var (totalDebit, totalCredit, lines) = await JournalAsync(sp, rcDocNo);
        totalDebit.Should().Be(totalCredit).And.Be(107_000m);
        lines.Should().Contain(l => l.AccountId == bank && l.Debit == 104_000m);
        lines.Should().Contain(l => l.AccountId == whtRecv && l.Debit == 3_000m);
        lines.Should().Contain(l => l.AccountId == ar && l.Credit == 107_000m);
        lines.Should().NotContain(l => l.AccountId == sales, "revenue was already recognized at TI post — never re-credited");
    }

    // ── D8 #2 — non-VAT sales happy path: pins D3(b) (Dr 1110/Cr 4000); TI creation blocked ──

    [SkippableFact]
    public async Task NonVat_sales_chain_settles_billing_note_pins_D3b_je_and_blocks_tax_invoice()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);

        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "GOOD", 50_000m, 0m);
        var doId = await CreateAndIssueDoFromSoAsync(sp, soId, today);

        long bnId;
        await using (var s = sp.CreateAsyncScope())
        {
            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bnSvc.CreateFromDeliveryOrderAsync(doId, default);
            await bnSvc.IssueAsync(bnId, default);
        }

        // ม.86/4 — a non-VAT company cannot create a Tax Invoice at all (existing chokepoint,
        // reused unchanged by the new CreateFromDeliveryOrderAsync/CreateFromBillingNoteAsync).
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var act = () => tiSvc.CreateFromDeliveryOrderAsync(doId, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("ti.non_vat_blocked");
        }

        string rcDocNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var rcSvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var rcId = await rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
                Applications: [new ReceiptApplicationInput(TaxInvoiceId: null, AppliedAmount: 50_000m, BillingNoteId: bnId)]),
                default);
            var posted = await rcSvc.PostAsync(rcId, default);
            rcDocNo = posted.DocNo;
        }

        var (cash, sales) = (
            await AccountIdAsync(sp, co.CompanyId, "1110"), await AccountIdAsync(sp, co.CompanyId, "4000"));
        var (totalDebit, totalCredit, lines) = await JournalAsync(sp, rcDocNo);
        totalDebit.Should().Be(totalCredit).And.Be(50_000m);
        lines.Should().Contain(l => l.AccountId == cash && l.Debit == 50_000m);
        lines.Should().Contain(l => l.AccountId == sales && l.Credit == 50_000m);
    }

    // ── D8 #3 — service-only SO skips the DO node entirely ────────────────────

    [SkippableFact]
    public async Task Service_only_so_skips_do_and_invoices_directly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);

        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "SERVICE", 20_000m, 0.07m);
        await using var s = sp.CreateAsyncScope();
        var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
        (await soSvc.GetAsync(soId, default))!.DeliveryRequired.Should().BeFalse("all-SERVICE SO needs no delivery");

        var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var tiId = await tiSvc.CreateFromSalesOrderAsync(soId, default);
        tiId.Should().BeGreaterThan(0);
        (await tiSvc.GetDetailAsync(tiId, default))!.Status.Should().Be("Draft");
    }

    // ── D8 #4 — deliveryRequired enforcement blocks a direct SO invoice for goods ──

    [SkippableFact]
    public async Task DeliveryRequired_blocks_direct_so_invoice_for_a_goods_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);

        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "GOOD", 15_000m, 0.07m);
        await using var s = sp.CreateAsyncScope();
        var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var act = () => tiSvc.CreateFromSalesOrderAsync(soId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("so.delivery_required");
    }

    // ── D8 #4 (MCP-surface) — the SAME guard through create_invoice_draft, confirming the
    // [mcp.domain_rule] wrapper + VAT-mode polymorphism actually wire up end-to-end. ──

    [SkippableFact]
    public async Task Mcp_create_invoice_draft_is_polymorphic_and_wraps_the_delivery_required_guard()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var vatCo = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var vatSp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, vatCo.CompanyId, vatCo.BranchId);
        var today = Today(vatSp);

        var goodsSoId = await QuotationToPostedSoAsync(vatSp, vatCo.CustomerId, today, "GOOD", 12_000m, 0.07m);
        var serviceSoId = await QuotationToPostedSoAsync(vatSp, vatCo.CustomerId, today, "SERVICE", 8_000m, 0.07m);

        var key = await MintMcpKeyAsync(vatCo.CompanyId, vatCo.BranchId,
            ["sales.sales_order.manage", "sales.delivery_order.manage", "sales.billing_note.manage"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        // Goods SO, no DO yet → domain_rule, wrapped by the error-surfacing filter.
        var blocked = await client.CallToolAsync("create_invoice_draft",
            new Dictionary<string, object?> { ["salesOrderId"] = goodsSoId, ["deliveryOrderId"] = null });
        blocked.IsError.Should().BeTrue();
        var blockedText = blocked.Content.OfType<TextContentBlock>().Single().Text;
        blockedText.Should().Contain("[mcp.domain_rule]");

        // Service-only SO → polymorphic VAT-co branch succeeds and returns a Tax Invoice draft.
        var ok = await client.CallToolAsync("create_invoice_draft",
            new Dictionary<string, object?> { ["salesOrderId"] = serviceSoId, ["deliveryOrderId"] = null });
        ok.IsError.Should().NotBe(true);
        var okJson = JsonDocument.Parse(ok.Content.OfType<TextContentBlock>().Single().Text);
        okJson.RootElement.GetProperty("id").GetInt64().Should().BeGreaterThan(0);
        okJson.RootElement.GetProperty("approvalLinkMarkdown").GetString().Should().Contain("tax-invoices");
    }

    // ── D8 #5 — standalone receipt (no invoiceId) stays byte-identical — pins D3(d) ──

    [SkippableFact]
    public async Task Mcp_standalone_receipt_unchanged_pins_D3d_je()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var key = await MintMcpKeyAsync(co.CompanyId, co.BranchId, ["sales.receipt.create", "master.product.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        long productId;
        await using (var s = sp.CreateAsyncScope())
        {
            var productSvc = s.ServiceProvider.GetRequiredService<Accounting.Application.Master.IProductService>();
            productId = await productSvc.CreateAsync(new Accounting.Application.Master.CreateProductRequest(
                TestIds.ProductCode(), "สินค้ามาตรฐาน", null, "GOOD", "ชิ้น", 100m,
                null, null, null, null, null, IsSaleable: true), default);
        }

        var request = new
        {
            docDate = today, customerId = co.CustomerId, paymentMethod = "Cash",
            chequeNo = (string?)null, chequeDate = (DateOnly?)null, bankAccountId = (long?)null,
            currencyCode = "THB", exchangeRate = 1m, notes = (string?)null,
            lines = new[] { new { productId, descriptionTh = "เงินสดขายปลีก", quantity = 1m, unitPrice = 300m, amount = 300m, productType = "GOOD" } },
        };
        var result = await client.CallToolAsync("create_receipt_draft",
            new Dictionary<string, object?> { ["request"] = request });
        result.IsError.Should().NotBe(true);
        var id = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("id").GetInt64();

        await using (var s = sp.CreateAsyncScope())
        {
            var rcSvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var posted = await rcSvc.PostAsync(id, default);
            var sales = await AccountIdAsync(sp, co.CompanyId, "4000");
            var (totalDebit, totalCredit, lines) = await JournalAsync(sp, posted.DocNo);
            totalDebit.Should().Be(totalCredit).And.Be(300m);
            lines.Should().Contain(l => l.AccountId == sales && l.Credit == 300m);
        }
    }

    // ── D8 #6 — dedup guards (each rejects a 2nd hop) ─────────────────────────

    [SkippableFact]
    public async Task Dedup_guard_rejects_a_second_so_from_the_same_quotation()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        await using var s = sp.CreateAsyncScope();
        var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
            today, today.AddDays(30), co.CustomerId, null, "THB", 1m, null, null,
            [new ChainLineInput(null, "x", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m, "SERVICE")]), default);
        await qSvc.SendAsync(qId, default);
        await qSvc.AcceptAsync(qId, default);
        await qSvc.ConvertToSalesOrderAsync(qId, default);
        var act = () => qSvc.ConvertToSalesOrderAsync(qId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("quotation.converted");
    }

    [SkippableFact]
    public async Task Dedup_guard_rejects_a_second_delivery_order_for_the_same_so()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "GOOD", 5_000m, 0.07m);
        await CreateAndIssueDoFromSoAsync(sp, soId, today);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var so = await db.SalesOrders.Include(x => x.Lines).FirstAsync(x => x.SalesOrderId == soId);
        var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var act = () => soSvc.CreateDeliveryOrderAsync(soId, new CreateDeliveryOrderRequest(
            today, so.CustomerId, so.BusinessUnitId, false, null, soId,
            so.Lines.Select(l => new DeliveryLineInput(
                l.LineId, l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode, l.TaxRate, l.ProductType)).ToList()),
            default);
        await act.Should().ThrowAsync<DomainException>();   // so.not_posted (auto-closed) or so.do_exists
    }

    [SkippableFact]
    public async Task Dedup_guard_rejects_a_second_invoice_for_the_same_delivery_order()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "GOOD", 7_000m, 0.07m);
        var doId = await CreateAndIssueDoFromSoAsync(sp, soId, today);

        await using var s = sp.CreateAsyncScope();
        var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        await tiSvc.CreateFromDeliveryOrderAsync(doId, default);
        var act = () => tiSvc.CreateFromDeliveryOrderAsync(doId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("do.ti_exists");
    }

    [SkippableFact]
    public async Task Dedup_guard_rejects_a_receipt_on_an_already_paid_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "SERVICE", 4_000m, 0.07m);
        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await tiSvc.CreateFromSalesOrderAsync(soId, default);
            await tiSvc.PostAsync(tiId, default);
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var rcSvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            var rcId = await rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
                Applications: [new ReceiptApplicationInput(TaxInvoiceId: tiId, AppliedAmount: 4_280m)]), default);
            await rcSvc.PostAsync(rcId, default);

            var act = () => rcSvc.CreateDraftAsync(new CreateReceiptRequest(
                today, co.CustomerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
                Applications: [new ReceiptApplicationInput(TaxInvoiceId: tiId, AppliedAmount: 1m)]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rc.overpaid");
        }
    }

    [SkippableFact]
    public async Task Dedup_guard_rejects_a_receipt_on_a_settled_billing_note()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "SERVICE", 6_000m, 0m);
        long bnId;
        await using (var s = sp.CreateAsyncScope())
        {
            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bnSvc.CreateFromSalesOrderAsync(soId, default);
            await bnSvc.IssueAsync(bnId, default);
            await bnSvc.MarkSettledAsync(bnId, default);   // simulate already-fully-collected
        }
        await using var s2 = sp.CreateAsyncScope();
        var rcSvc = s2.ServiceProvider.GetRequiredService<IReceiptService>();
        var act = () => rcSvc.CreateDraftAsync(new CreateReceiptRequest(
            today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
            Applications: [new ReceiptApplicationInput(TaxInvoiceId: null, AppliedAmount: 6_000m, BillingNoteId: bnId)]),
            default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rc.invoice_already_settled");
    }

    [SkippableFact]
    public async Task Dedup_guard_rejects_a_second_vendor_invoice_for_the_same_po()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var vendorId = await SeedVendorAsync(sp, co.CompanyId);
        var (catId, _) = await SeedExpenseCategoryAsync(sp, co.CompanyId);
        var poId = await CreateAndApprovePoAsync(sp, vendorId, today, 10_000m, 0.07m);

        await using var s = sp.CreateAsyncScope();
        var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        await viSvc.CreateFromPurchaseOrderAsync(poId,
            new CreateViFromPoRequest(catId, "VTX-001", today), default);
        var act = () => viSvc.CreateFromPurchaseOrderAsync(poId,
            new CreateViFromPoRequest(catId, "VTX-002", today), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("po.vi_exists");
    }

    [SkippableFact]
    public async Task Dedup_guard_rejects_a_second_payment_voucher_for_the_same_vi()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var vendorId = await SeedVendorAsync(sp, co.CompanyId);
        var (catId, _) = await SeedExpenseCategoryAsync(sp, co.CompanyId);
        var poId = await CreateAndApprovePoAsync(sp, vendorId, today, 8_000m, 0.07m);

        long viId;
        await using (var s = sp.CreateAsyncScope())
        {
            var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viId = await viSvc.CreateFromPurchaseOrderAsync(poId,
                new CreateViFromPoRequest(catId, "VTX-003", today), default);
            await viSvc.PostAsync(viId, default);
        }
        await using var s2 = sp.CreateAsyncScope();
        var pvSvc = s2.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        await pvSvc.CreateFromVendorInvoiceAsync(viId,
            new CreatePvFromViRequest(PaymentMethod.Transfer), default);
        var act = () => pvSvc.CreateFromVendorInvoiceAsync(viId,
            new CreatePvFromViRequest(PaymentMethod.Transfer), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("vi.pv_exists");
    }

    // ── D8 #7 — purchase happy path: pins D3(c) (Dr 2110/Cr 2152/Cr 1120) ─────

    [SkippableFact]
    public async Task Purchase_chain_settles_vi_with_our_wht_pins_D3c_je()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var vendorId = await SeedVendorAsync(sp, co.CompanyId);
        var (catId, _) = await SeedExpenseCategoryAsync(sp, co.CompanyId);
        var whtTypeId = await SeedWhtTypeAsync(sp, co.CompanyId, 0.03m);
        var poId = await CreateAndApprovePoAsync(sp, vendorId, today, 100_000m, 0.07m);

        long viId, pvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            viId = await viSvc.CreateFromPurchaseOrderAsync(poId,
                new CreateViFromPoRequest(catId, "VTX-D3C", today), default);
            var detail = await viSvc.GetDetailAsync(viId, default);
            detail!.Lines.Should().ContainSingle(l => l.Amount == 100_000m && l.VatRate == 0.07m);
            await viSvc.PostAsync(viId, default);
        }
        await using (var s = sp.CreateAsyncScope())
        {
            var pvSvc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
            pvId = await pvSvc.CreateFromVendorInvoiceAsync(viId,
                new CreatePvFromViRequest(PaymentMethod.Transfer, WhtTypeId: whtTypeId, WhtRate: 0.03m), default);
            await pvSvc.ApproveAsync(pvId, default);
            var posted = await pvSvc.PostAsync(pvId, default);

            var (ap, whtPayable, bank) = (
                await AccountIdAsync(sp, co.CompanyId, "2110"), await AccountIdAsync(sp, co.CompanyId, "2152"),
                await AccountIdAsync(sp, co.CompanyId, "1120"));
            var (totalDebit, totalCredit, lines) = await JournalAsync(sp, posted.DocNo);
            totalDebit.Should().Be(totalCredit).And.Be(107_000m);
            lines.Should().Contain(l => l.AccountId == ap && l.Debit == 107_000m);
            lines.Should().Contain(l => l.AccountId == whtPayable && l.Credit == 3_000m);
            lines.Should().Contain(l => l.AccountId == bank && l.Credit == 104_000m);
        }

        await using var s2 = sp.CreateAsyncScope();
        var viSvc2 = s2.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        (await viSvc2.GetDetailAsync(viId, default))!.SettlementStatus.Should().Be("PAID");
    }

    // ── D8 #8 — guard negatives: VI-from-Draft-PO / PV-from-Draft-VI ─────────

    [SkippableFact]
    public async Task Vi_from_po_rejects_a_draft_po()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var vendorId = await SeedVendorAsync(sp, co.CompanyId);
        var (catId, _) = await SeedExpenseCategoryAsync(sp, co.CompanyId);

        await using var s = sp.CreateAsyncScope();
        var poSvc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var poId = await poSvc.CreateDraftAsync(new CreatePurchaseOrderRequest(
            today, null, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(null, "x", 1m, "ชิ้น", 1000m, 0m, null, "VAT7", 0.07m, null)]), default);

        var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var act = () => viSvc.CreateFromPurchaseOrderAsync(poId, new CreateViFromPoRequest(catId, "X", today), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("po.not_approved");
    }

    [SkippableFact]
    public async Task Pv_from_vi_rejects_a_draft_vi()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var vendorId = await SeedVendorAsync(sp, co.CompanyId);
        var (catId, _) = await SeedExpenseCategoryAsync(sp, co.CompanyId);
        var poId = await CreateAndApprovePoAsync(sp, vendorId, today, 3_000m, 0.07m);

        await using var s = sp.CreateAsyncScope();
        var viSvc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var viId = await viSvc.CreateFromPurchaseOrderAsync(poId, new CreateViFromPoRequest(catId, "X2", today), default);
        // VI stays Draft (not posted).
        var pvSvc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var act = () => pvSvc.CreateFromVendorInvoiceAsync(viId, new CreatePvFromViRequest(PaymentMethod.Transfer), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("vi.not_posted");
    }

    // ── §B addition (Ham 2026-07-13) — optional BillingNote hop for VAT companies ──

    [SkippableFact]
    public async Task Billing_note_draft_from_do_and_service_only_so_on_a_vat_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);

        var goodsSoId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "GOOD", 9_000m, 0.07m);
        var doId = await CreateAndIssueDoFromSoAsync(sp, goodsSoId, today);
        var serviceSoId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "SERVICE", 3_000m, 0.07m);

        await using var s = sp.CreateAsyncScope();
        var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnFromDo = await bnSvc.CreateFromDeliveryOrderAsync(doId, default);
        bnFromDo.Should().BeGreaterThan(0);
        var bnFromSo = await bnSvc.CreateFromSalesOrderAsync(serviceSoId, default);
        bnFromSo.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Tax_invoice_draft_from_billing_note_and_dedup_guard()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "SERVICE", 2_000m, 0.07m);

        await using var s = sp.CreateAsyncScope();
        var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnId = await bnSvc.CreateFromSalesOrderAsync(soId, default);
        await bnSvc.IssueAsync(bnId, default);

        var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var tiId = await tiSvc.CreateFromBillingNoteAsync(bnId, default);
        (await tiSvc.GetDetailAsync(tiId, default))!.QuotationId.Should().BeNull();

        var act = () => tiSvc.CreateFromBillingNoteAsync(bnId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bn.ti_exists");
    }

    [SkippableFact]
    public async Task Vat_company_receipt_against_a_billing_note_is_blocked_must_settle_the_ti_instead()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "SERVICE", 1_500m, 0.07m);

        await using var s = sp.CreateAsyncScope();
        var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnId = await bnSvc.CreateFromSalesOrderAsync(soId, default);
        await bnSvc.IssueAsync(bnId, default);

        var rcSvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var act = () => rcSvc.CreateDraftAsync(new CreateReceiptRequest(
            today, co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, null,
            Applications: [new ReceiptApplicationInput(TaxInvoiceId: null, AppliedAmount: 1_605m, BillingNoteId: bnId)]),
            default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rc.vat_co_no_bn_settle");
    }

    // ── D8 #9 — tenancy: get_sales_order/list_sales_orders scope to caller company ──

    [SkippableFact]
    public async Task Get_sales_order_and_list_sales_orders_scope_to_caller_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co1 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var co2 = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp1 = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co1.CompanyId, co1.BranchId);
        var today = Today(sp1);
        var soId = await QuotationToPostedSoAsync(sp1, co1.CustomerId, today, "SERVICE", 1_000m, 0.07m);

        await using var sp2 = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co2.CompanyId, co2.BranchId);
        await using var s2 = sp2.CreateAsyncScope();
        var soSvc2 = s2.ServiceProvider.GetRequiredService<ISalesOrderService>();
        (await soSvc2.GetAsync(soId, default)).Should().BeNull("company 2 must not see company 1's SO");
        (await soSvc2.ListAsync(null, default)).Should().NotContain(x => x.SalesOrderId == soId);

        await using var s1 = sp1.CreateAsyncScope();
        var soSvc1 = s1.ServiceProvider.GetRequiredService<ISalesOrderService>();
        (await soSvc1.GetAsync(soId, default)).Should().NotBeNull();
    }

    // ── D8 #10 — get_document_status resolves the 3 new types ─────────────────

    [SkippableFact]
    public async Task Mcp_get_document_status_resolves_sales_order_delivery_order_billing_note()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);
        var soId = await QuotationToPostedSoAsync(sp, co.CustomerId, today, "GOOD", 2_500m, 0.07m);
        var doId = await CreateAndIssueDoFromSoAsync(sp, soId, today);
        long bnId;
        await using (var s = sp.CreateAsyncScope())
        {
            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bnSvc.CreateFromDeliveryOrderAsync(doId, default);
        }

        var key = await MintMcpKeyAsync(co.CompanyId, co.BranchId, ["sales.tax_invoice.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        var soStatus = await client.CallToolAsync("get_document_status",
            new Dictionary<string, object?> { ["type"] = "sales-order", ["id"] = soId });
        soStatus.IsError.Should().NotBe(true);
        // Full-qty delivery auto-closed the SO the moment the DO was created (D1/D5) — Closed,
        // not Posted, is the correct terminal state here.
        JsonDocument.Parse(soStatus.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("status").GetString().Should().Be("Closed");

        var doStatus = await client.CallToolAsync("get_document_status",
            new Dictionary<string, object?> { ["type"] = "delivery-order", ["id"] = doId });
        JsonDocument.Parse(doStatus.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("status").GetString().Should().Be("Issued");

        var bnStatus = await client.CallToolAsync("get_document_status",
            new Dictionary<string, object?> { ["type"] = "billing-note", ["id"] = bnId });
        JsonDocument.Parse(bnStatus.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("status").GetString().Should().Be("Draft");
    }

    // ── D8 #11 — get_workflow_guide content ────────────────────────────────────

    [SkippableFact]
    public async Task Mcp_get_workflow_guide_matches_company_vat_mode()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var vatCo = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var nonVatCo = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);

        var vatKey = await MintMcpKeyAsync(vatCo.CompanyId, vatCo.BranchId, ["sales.quotation.read"]);
        await using (var factory = new McpApiFactory(_fx.ConnectionString))
        {
            using var http = factory.CreateClient();
            http.DefaultRequestHeaders.Add(ApiKeyHeader, vatKey);
            await using var client = await ConnectAsync(http);
            var result = await client.CallToolAsync("get_workflow_guide", new Dictionary<string, object?>());
            var text = result.Content.OfType<TextContentBlock>().Single().Text;
            text.Should().Contain("ใบกำกับภาษี");
            text.Should().Contain("create_billing_note_draft");   // optional BN step documented
        }

        var nonVatKey = await MintMcpKeyAsync(nonVatCo.CompanyId, nonVatCo.BranchId, ["sales.quotation.read"]);
        await using (var factory = new McpApiFactory(_fx.ConnectionString))
        {
            using var http = factory.CreateClient();
            http.DefaultRequestHeaders.Add(ApiKeyHeader, nonVatKey);
            await using var client = await ConnectAsync(http);
            var result = await client.CallToolAsync("get_workflow_guide", new Dictionary<string, object?>());
            var text = result.Content.OfType<TextContentBlock>().Single().Text;
            text.Should().Contain("ม.86/4");
            // The ม.86/4 warning itself legitimately NAMES "ใบกำกับภาษี" (explaining the company
            // CANNOT issue one) — the precise non-VAT marker is the absence of the VAT guide's
            // step-4 action wording (which only fires when a Tax Invoice is actually produced).
            text.Should().NotContain("ระบบออกเป็นใบกำกับภาษี");
        }
    }

    // ── D8 #12 — every new create tool's DraftCreated carries a non-empty approvalLinkMarkdown ──

    [SkippableFact]
    public async Task New_create_tools_return_nonempty_approval_link_markdown()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today(sp);

        var key = await MintMcpKeyAsync(co.CompanyId, co.BranchId,
            ["sales.quotation.read", "sales.quotation.create", "sales.sales_order.manage",
             "sales.delivery_order.manage", "sales.billing_note.manage", "master.customer.read"]);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add(ApiKeyHeader, key);
        await using var client = await ConnectAsync(http);

        long qId;
        await using (var s = sp.CreateAsyncScope())
        {
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), co.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "x", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m, "SERVICE")]), default);
            await qSvc.SendAsync(qId, default);
            await qSvc.AcceptAsync(qId, default);
        }

        var soResult = await client.CallToolAsync("create_sales_order_draft",
            new Dictionary<string, object?> { ["quotationId"] = qId });
        soResult.IsError.Should().NotBe(true);
        var soJson = JsonDocument.Parse(soResult.Content.OfType<TextContentBlock>().Single().Text).RootElement;
        soJson.GetProperty("approvalLinkMarkdown").GetString().Should().NotBeNullOrWhiteSpace();
        var soId = soJson.GetProperty("id").GetInt64();

        await using (var s = sp.CreateAsyncScope())
        {
            var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            await soSvc.PostAsync(soId, default);
        }

        var bnResult = await client.CallToolAsync("create_billing_note_draft",
            new Dictionary<string, object?> { ["salesOrderId"] = soId, ["deliveryOrderId"] = null });
        bnResult.IsError.Should().NotBe(true);
        JsonDocument.Parse(bnResult.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("approvalLinkMarkdown").GetString().Should().NotBeNullOrWhiteSpace();
    }

    // ── local MCP scaffolding (mirrors McpServerSmokeTests.cs — per-file convention) ──

    private async Task<string> MintMcpKeyAsync(int companyId, int branchId, IReadOnlyList<string> scopes)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(
            TestIds.Name("mcp-doc-chain"), scopes, Kind: ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }

    private static async Task<McpClient> ConnectAsync(HttpClient http) =>
        await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            http,
            loggerFactory: null,
            ownsHttpClient: false));
}
