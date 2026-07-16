using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// WP-A backend fixes (specs/fix-sales-ux-findings-2026-07-16.md), real Postgres.
///  S4     — Quotation/SalesOrder/DeliveryOrder list DTOs now carry BusinessUnitId.
///  S9     — the "Business Unit is required for this company" rule (already enforced on
///           TaxInvoice/Receipt/TaxAdjustmentNote) now also guards Quotation create/update/
///           send, SalesOrder create, and Invoice(BillingNote) create/update.
///  S14    — Invoice (BillingNote) DueDate now derives from the customer's PaymentTermDays
///           on the SO→Invoice chain-create path (was always DueDate == DocDate).
///  S12-BE — Quotation UpdateDraftAsync now writes an activity-log entry.
/// Each scenario uses an isolated TestCompanyFactory company so the BU-required flag flip
/// and the 0-day-term customer never touch shared company 1 data.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SalesUxFixesWpATests
{
    private readonly PostgresFixture _fx;
    public SalesUxFixesWpATests(PostgresFixture fx) => _fx = fx;

    private static ChainLineInput ServiceLine(decimal qty, decimal price) =>
        new(null, "บริการทดสอบ", qty, "ครั้ง", price, 0m, 1, "VAT7", 0.07m, "SERVICE");

    private static async Task<int> NewBu(IServiceProvider sp, string code)
    {
        var svc = sp.GetRequiredService<IBusinessUnitService>();
        return await svc.CreateAsync(new CreateBusinessUnitRequest(code, "หน่วย " + code, code, null), default);
    }

    private static async Task<long> NewCustomerWithTerm(IServiceProvider sp, int companyId, int termDays)
    {
        var db = sp.GetRequiredService<AccountingDbContext>();
        var c = new Customer
        {
            CompanyId = companyId, CustomerCode = "C-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            CustomerType = CustomerType.Corporate, NameTh = "ลูกค้าเทอมทดสอบ",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
            BillingAddress = "99 ถ.ทดสอบ", CreditLimit = 0, PaymentTermDays = termDays, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.CustomerId;
    }

    // ── S4 — list DTOs carry businessUnitId ──────────────────────────────

    [SkippableFact]
    public async Task Quotation_list_item_carries_business_unit_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var buId = await NewBu(scope.ServiceProvider, "REPT");

        var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var id = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, buId, "THB", 1m, null, null, [ServiceLine(1m, 500m)]), default);

        var list = await qsvc.ListAsync(null, default);
        list.Should().Contain(x => x.QuotationId == id && x.BusinessUnitId == buId);
    }

    [SkippableFact]
    public async Task SalesOrder_list_item_carries_business_unit_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var buId = await NewBu(scope.ServiceProvider, "REPT");

        var sosvc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var id = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, c.CustomerId, buId, "THB", 1m, null, null,
            [ServiceLine(1m, 500m)]), default);

        var list = await sosvc.ListAsync(null, default);
        list.Should().Contain(x => x.SalesOrderId == id && x.BusinessUnitId == buId);
    }

    [SkippableFact]
    public async Task DeliveryOrder_list_item_carries_business_unit_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var buId = await NewBu(scope.ServiceProvider, "REPT");

        var dosvc = scope.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
        var id = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId, buId, false, null, null,
            [new DeliveryLineInput(null, null, "สินค้าทดสอบ", 1m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m, "GOOD")]),
            default);

        var list = await dosvc.ListAsync(null, default);
        list.Should().Contain(x => x.DeliveryOrderId == id && x.BusinessUnitId == buId);
    }

    // ── S9 — company-level BU-required enforcement ───────────────────────

    [SkippableFact]
    public async Task Quotation_create_without_bu_rejected_when_company_requires_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .SetCompanyRequiresBuAsync(true, default);

        var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var act = () => qsvc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null, [ServiceLine(1m, 500m)]), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
    }

    // Real state-transition drive (not a seeded target state): the draft is legitimately
    // created with a null BU while the company had NOT yet opted into BU-required (mirrors
    // an MCP agent draft made before the flag flipped) — SendAsync must still catch it.
    [SkippableFact]
    public async Task Quotation_send_of_pre_existing_null_bu_draft_rejected_once_company_requires_bu()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null, [ServiceLine(1m, 500m)]), default);

        await scope.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .SetCompanyRequiresBuAsync(true, default);

        var act = () => qsvc.SendAsync(qId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
    }

    [SkippableFact]
    public async Task SalesOrder_create_without_bu_rejected_when_company_requires_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .SetCompanyRequiresBuAsync(true, default);

        var sosvc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var act = () => sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, c.CustomerId, null, "THB", 1m, null, null,
            [ServiceLine(1m, 500m)]), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
    }

    [SkippableFact]
    public async Task BillingNote_create_without_bu_rejected_when_company_requires_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IBusinessUnitService>()
            .SetCompanyRequiresBuAsync(true, default);

        var bnsvc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var act = () => bnsvc.CreateDraftAsync(new CreateBillingNoteRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, null, null, "THB", 1m, null, null,
            [new BillingLineInput(null, null, "บริการทดสอบ", 1m, "ครั้ง", 500m, 0m, 1, "VAT7", 0.07m, "SERVICE")]),
            default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
    }

    // ── S14 — Invoice DueDate derives from customer credit term ──────────

    [SkippableFact]
    public async Task Invoice_from_sales_order_applies_customer_credit_term_to_due_date()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // TestCompanyFactory's seeded demo customer carries PaymentTermDays = 30.
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var sosvc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, c.CustomerId, null, "THB", 1m, null, null,
            [ServiceLine(1m, 1000m)]), default);
        await sosvc.PostAsync(soId, default);

        var bnsvc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnId = await bnsvc.CreateFromSalesOrderAsync(soId, default);

        var detail = await bnsvc.GetAsync(bnId, default);
        detail.Should().NotBeNull();
        detail!.DueDate.Should().Be(detail.DocDate.AddDays(30), "customer PaymentTermDays = 30");
    }

    [SkippableFact]
    public async Task Invoice_from_sales_order_keeps_due_date_equal_doc_date_when_customer_has_no_term()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();
        var custId = await NewCustomerWithTerm(scope.ServiceProvider, c.CompanyId, termDays: 0);

        var sosvc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, custId, null, "THB", 1m, null, null,
            [ServiceLine(1m, 1000m)]), default);
        await sosvc.PostAsync(soId, default);

        var bnsvc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnId = await bnsvc.CreateFromSalesOrderAsync(soId, default);

        var detail = await bnsvc.GetAsync(bnId, default);
        detail.Should().NotBeNull();
        detail!.DueDate.Should().Be(detail.DocDate, "prior behavior preserved when the customer has no credit term");
    }

    // ── S12-BE — Quotation draft edit now writes an activity entry ───────

    [SkippableFact]
    public async Task Quotation_update_draft_writes_activity_entry()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null, [ServiceLine(1m, 500m)]), default);

        await qsvc.UpdateDraftAsync(qId, new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(45)),
            c.CustomerId, null, "THB", 1m, "แก้ไขแล้ว", null, [ServiceLine(2m, 500m)]), default);

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var updated = await db.ActivityLogs.AsNoTracking()
            .CountAsync(a => a.EntityType == "Quotation" && a.EntityId == qId && a.ActivityType == "Updated");
        updated.Should().Be(1);
    }

    // ── S13b — number-issuing/posting transitions reject a duplicate call ────
    // Prod evidence (S13): the Cloudflare edge occasionally 503s a first attempt while the
    // origin still applies it; the client then retries the SAME request. Each transition
    // below is driven to completion once (the REAL transition, not a seeded target status),
    // then called again — the retry must be rejected with no second doc number and no
    // second activity/JE entry (safe no-op or 409/422, either acceptable per spec).

    [SkippableFact]
    public async Task Quotation_send_called_twice_second_call_rejected_no_duplicate_number()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null, [ServiceLine(1m, 500m)]), default);
        await qsvc.SendAsync(qId, default);
        var docNo1 = (await qsvc.GetAsync(qId, default))!.DocNo;
        docNo1.Should().NotBeNullOrEmpty();

        var act = () => qsvc.SendAsync(qId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("quotation.bad_status");

        (await qsvc.GetAsync(qId, default))!.DocNo.Should().Be(docNo1, "the retry must not consume a second number");
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.ActivityLogs.AsNoTracking().CountAsync(
            a => a.EntityType == "Quotation" && a.EntityId == qId && a.ActivityType == "Sent")).Should().Be(1);
    }

    [SkippableFact]
    public async Task Quotation_accept_called_twice_second_call_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var qsvc = scope.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qsvc.CreateDraftAsync(new CreateQuotationRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, "THB", 1m, null, null, [ServiceLine(1m, 500m)]), default);
        await qsvc.SendAsync(qId, default);
        await qsvc.AcceptAsync(qId, default);

        var act = () => qsvc.AcceptAsync(qId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("quotation.bad_status");

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.ActivityLogs.AsNoTracking().CountAsync(
            a => a.EntityType == "Quotation" && a.EntityId == qId && a.ActivityType == "Accepted")).Should().Be(1);
    }

    [SkippableFact]
    public async Task SalesOrder_post_called_twice_second_call_rejected_no_duplicate_number()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var sosvc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, c.CustomerId, null, "THB", 1m, null, null,
            [ServiceLine(1m, 500m)]), default);
        await sosvc.PostAsync(soId, default);
        var docNo1 = (await sosvc.GetAsync(soId, default))!.DocNo;
        docNo1.Should().NotBeNullOrEmpty();

        var act = () => sosvc.PostAsync(soId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("so.bad_status");

        (await sosvc.GetAsync(soId, default))!.DocNo.Should().Be(docNo1, "the retry must not consume a second number");
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.ActivityLogs.AsNoTracking().CountAsync(
            a => a.EntityType == "SalesOrder" && a.EntityId == soId && a.ActivityType == "Posted")).Should().Be(1);
    }

    [SkippableFact]
    public async Task BillingNote_issue_called_twice_second_call_rejected_no_duplicate_number()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var bnsvc = scope.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var bnId = await bnsvc.CreateDraftAsync(new CreateBillingNoteRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            c.CustomerId, null, null, null, "THB", 1m, null, null,
            [new BillingLineInput(null, null, "บริการทดสอบ", 1m, "ครั้ง", 500m, 0m, 1, "VAT7", 0.07m, "SERVICE")]),
            default);
        await bnsvc.IssueAsync(bnId, default);
        var docNo1 = (await bnsvc.GetAsync(bnId, default))!.DocNo;
        docNo1.Should().NotBeNullOrEmpty();

        var act = () => bnsvc.IssueAsync(bnId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("billing_note.bad_status");

        (await bnsvc.GetAsync(bnId, default))!.DocNo.Should().Be(docNo1, "the retry must not consume a second number");
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.ActivityLogs.AsNoTracking().CountAsync(
            a => a.EntityType == "BillingNote" && a.EntityId == bnId && a.ActivityType == "Issued")).Should().Be(1);
    }

    // Receipt.PostAsync is the one transition whose number allocation happens BEFORE its
    // Draft-status guard (Receipt.MarkPosted) instead of before it like the other four —
    // this test proves that ordering is still safe: the whole transition (incl. the number
    // allocation, which runs on the SAME ambient DB transaction per NumberSequenceService)
    // rolls back atomically when MarkPosted rejects the retry, so no number is stranded and
    // no second JE is posted.
    [SkippableFact]
    public async Task Receipt_post_called_twice_second_call_rejected_no_duplicate_number_or_je()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var rcsvc = scope.ServiceProvider.GetRequiredService<IReceiptService>();
        var rcId = await rcsvc.CreateDraftAsync(new CreateReceiptRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), c.CustomerId, PaymentMethod.Transfer,
            null, null, null, "THB", 1m, null, [],
            Lines: [new ReceiptLineInput("สินค้าทดสอบ", 1m, 1000m, 1000m)]), default);

        var posted1 = await rcsvc.PostAsync(rcId, default);
        posted1.DocNo.Should().NotBeNullOrEmpty();

        var act = () => rcsvc.PostAsync(rcId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rc.not_draft");

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.Receipts.AsNoTracking().Where(r => r.ReceiptId == rcId)
            .Select(r => r.DocNo).FirstAsync()).Should().Be(posted1.DocNo, "the retry must not consume a second number");
        (await db.JournalEntries.AsNoTracking().CountAsync(j => j.Reference == posted1.DocNo))
            .Should().Be(1, "the failed retry must not post a second JE");
    }

    // ── S15-BE — SalesOrder draft update (Quotation-parity: §10 Option B DocDate) ────

    [SkippableFact]
    public async Task SalesOrder_update_draft_persists_and_preserves_doc_date()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var sosvc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var docDate1 = DateOnly.FromDateTime(DateTime.UtcNow);
        var soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            docDate1, null, c.CustomerId, null, "THB", 1m, "เดิม", null, [ServiceLine(1m, 500m)]), default);

        var docDate2 = docDate1.AddDays(1);
        await sosvc.UpdateDraftAsync(soId, new CreateSalesOrderRequest(
            docDate2, null, c.CustomerId, null, "THB", 1m, "แก้ไขแล้ว", null, [ServiceLine(3m, 500m)]), default);

        var detail = await sosvc.GetAsync(soId, default);
        detail.Should().NotBeNull();
        detail!.DocDate.Should().Be(docDate2,
            "the update persists whatever DocDate the request carries (§10 Option B, Quotation-parity — never re-pinned)");
        detail.TotalAmount.Should().Be(1500m * 1.07m, "the edited line quantity (3 x 500) is recomputed with VAT");

        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.ActivityLogs.AsNoTracking().CountAsync(
            a => a.EntityType == "SalesOrder" && a.EntityId == soId && a.ActivityType == "Updated")).Should().Be(1);
    }

    [SkippableFact]
    public async Task SalesOrder_update_rejected_when_not_draft()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, c.CompanyId, c.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var sosvc = scope.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var soId = await sosvc.CreateDraftAsync(new CreateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, c.CustomerId, null, "THB", 1m, null, null,
            [ServiceLine(1m, 500m)]), default);
        await sosvc.PostAsync(soId, default);

        var act = () => sosvc.UpdateDraftAsync(soId, new CreateSalesOrderRequest(
            DateOnly.FromDateTime(DateTime.UtcNow), null, c.CustomerId, null, "THB", 1m, "ควรถูกปฏิเสธ", null,
            [ServiceLine(1m, 500m)]), default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("so.cannot_edit_after_post");
    }
}
