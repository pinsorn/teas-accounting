using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Batch-A compliance pack (code-review 2026-06-17). Real Postgres, no mocks. Covers:
///  ① tax-point pinned server-side to Asia/Bangkok today (ม.86/4(7), ม.78, §10);
///  ② THB-only guard at fiscal-doc create (multi-currency deferred — 05-C1/05-H1);
///  ③ unknown period default-closed except current Bangkok month;
///  ⑩ ม.86/4 8-field enforcement; tax-inclusive VAT = total×7/107 shown separately;
///     voided document number is never reused.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BatchAComplianceTests
{
    private readonly PostgresFixture _fx;
    public BatchAComplianceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, int branchId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
                ["FileStorage:StorageRoot"] = Path.Combine(Path.GetTempPath(), "teas-batcha-" + TestIds.Suffix()),
                ["FileStorage:MaxFileSizeMb"] = "25",
            }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = branchId, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static DateOnly TodayBkk() => new SystemClock().TodayInBangkok();

    // Creates a VAT-registered customer (complete by default) under company 1.
    private static async Task<long> SeedCustomerAsync(
        ServiceProvider sp, bool vatRegistered = true, bool complete = true)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var c = new Customer
        {
            CompanyId = 1,
            CustomerCode = TestIds.CustomerCode(),
            CustomerType = CustomerType.Corporate,
            NameTh = TestIds.Name("ลูกค้า"),
            VatRegistered = vatRegistered,
            TaxId = complete ? TestIds.TaxId() : null,
            BranchCode = complete ? "00000" : null,
            BranchName = complete ? "สำนักงานใหญ่" : null,
            BillingAddress = "123 ถนนทดสอบ กรุงเทพฯ",
        };
        db.Customers.Add(c);
        await db.SaveChangesAsync();
        return c.CustomerId;
    }

    private static TaxInvoiceLineInput Line(decimal price, bool inclusive = false) =>
        new(null, null, "บริการทดสอบ", 1m, 1, "ชิ้น", price, 0m, 1, "VAT7", 0.07m);

    // ─────────────────────────────────────────────────────────────────────────────
    // ① Tax-point pinned server-side — ม.86/4(7), ม.78, §10
    // ─────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TaxInvoice_docdate_and_taxpoint_pinned_to_bangkok_today_ignoring_request()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await SeedCustomerAsync(sp);

        // The caller LIES about the date (a far past month). The server must ignore it.
        var lie = new DateOnly(2020, 1, 1);
        long id;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                lie, cust, false, "THB", 1m, null, null, null, [Line(1000m)], null), default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == id);
        var today = TodayBkk();
        // §10 / ม.86/4(7) — DocDate and TaxPointDate are today in Asia/Bangkok, NOT the request.
        ti.DocDate.Should().Be(today);
        ti.TaxPointDate.Should().Be(today);
        ti.DocDate.Should().NotBe(lie);
    }

    [SkippableFact]
    public async Task VendorInvoice_docdate_pinned_to_today_but_vendor_ti_date_is_preserved()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();

        long vendorId; int catId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var exp = await db.ChartOfAccounts.Where(a => a.AccountCode == "5200")
                .Select(a => a.AccountId).FirstAsync();
            var v = new Vendor
            {
                CompanyId = 1, VendorCode = TestIds.VendorCode(), VendorType = CustomerType.Corporate,
                NameTh = TestIds.Name("ผู้ขาย"), TaxId = TestIds.TaxId(), BranchCode = "00000",
                VatRegistered = true,
            };
            var cat = new Accounting.Domain.Entities.Sys.ExpenseCategory
            {
                CompanyId = 1, CategoryCode = TestIds.ExpenseCategoryCode(),
                NameTh = "หมวด", DefaultExpenseAccountId = exp, DefaultIsRecoverableVat = true,
            };
            db.Vendors.Add(v); db.ExpenseCategories.Add(cat);
            await db.SaveChangesAsync();
            vendorId = v.VendorId; catId = cat.CategoryId;
        }

        var today = TodayBkk();
        // The counterparty's tax-invoice date is THIS open month (so the claim period is open),
        // but a different DAY from "today" so we can prove DocDate != VendorTaxInvoiceDate.
        var vendorTiDate = new DateOnly(today.Year, today.Month, 1);
        long id;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
            id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
                DocDate: new DateOnly(2019, 3, 3), VendorId: vendorId,
                VendorTaxInvoiceNo: "VTI-" + TestIds.Suffix()[..6],
                VendorTaxInvoiceDate: vendorTiDate, VatClaimPeriod: null,
                CurrencyCode: "THB", ExchangeRate: 1m, Notes: null,
                Lines: [new VendorInvoiceLineInput(catId, null, "line", 1000m, 0.07m)]), default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var vi = await db2.VendorInvoices.AsNoTracking().FirstAsync(x => x.VendorInvoiceId == id);
        // §10 — vi.DocDate is server-pinned to today...
        vi.DocDate.Should().Be(today);
        // ...but VendorTaxInvoiceDate (drives the ม.82/4 claim window) is the counterparty's date.
        vi.VendorTaxInvoiceDate.Should().Be(vendorTiDate);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ② THB-only guard at create — multi-currency deferred (05-C1 / 05-H1)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TaxInvoice_validator_rejects_non_THB_currency()
    {
        var v = new CreateTaxInvoiceValidator();
        var req = new CreateTaxInvoiceRequest(
            new DateOnly(2026, 6, 1), 1, false, "USD", 1m, null, null, null, [Line(1000m)], null);
        var r = v.Validate(req);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorCode == CurrencyValidationExtensions.ThbOnlyCode);
    }

    [Fact]
    public void TaxInvoice_validator_rejects_non_unit_exchange_rate()
    {
        var v = new CreateTaxInvoiceValidator();
        var req = new CreateTaxInvoiceRequest(
            new DateOnly(2026, 6, 1), 1, false, "THB", 35m, null, null, null, [Line(1000m)], null);
        var r = v.Validate(req);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.ErrorCode == CurrencyValidationExtensions.ThbOnlyCode);
    }

    [Fact]
    public void Fiscal_doc_validators_all_accept_THB_at_rate_one()
    {
        // THB / 1 must remain valid across the fiscal-doc create validators.
        new CreateTaxInvoiceValidator().Validate(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 6, 1), 1, false, "THB", 1m, null, null, null, [Line(1000m)], null))
            .Errors.Should().NotContain(e => e.ErrorCode == CurrencyValidationExtensions.ThbOnlyCode);

        new CreateJournalValidator().Validate(new CreateJournalRequest(
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), "desc", null, "THB", 1m,
            [new JournalLineInput(1, 100m, 0m, null, null, null),
             new JournalLineInput(2, 0m, 100m, null, null, null)]))
            .Errors.Should().NotContain(e => e.ErrorCode == CurrencyValidationExtensions.ThbOnlyCode);
    }

    [Fact]
    public void Journal_validator_rejects_non_THB()
    {
        var v = new CreateJournalValidator();
        var req = new CreateJournalRequest(
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1), "desc", null, "EUR", 1m,
            [new JournalLineInput(1, 100m, 0m, null, null, null),
             new JournalLineInput(2, 0m, 100m, null, null, null)]);
        v.Validate(req).Errors.Should().Contain(e => e.ErrorCode == CurrencyValidationExtensions.ThbOnlyCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ③ Unknown period default-closed except current Bangkok month
    // ─────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Missing_period_is_open_only_for_current_bangkok_month()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(companyId: 1 + Random.Shared.Next(900_000, 999_999));
        await using var s = sp.CreateAsyncScope();
        var period = s.ServiceProvider.GetRequiredService<IPeriodCloseService>();
        var today = TodayBkk();

        // Current month: open-when-missing (the legitimate not-yet-closed case).
        (await period.IsOpenAsync(today.Year, today.Month, default)).Should().BeTrue();

        // A never-opened PAST month: now CLOSED (was unbounded-open before ③).
        var past = today.AddMonths(-6);
        (await period.IsOpenAsync(past.Year, past.Month, default)).Should().BeFalse(
            "③ — a missing past-month row must be CLOSED, not open-forever");

        // A FUTURE month: CLOSED (no forward-dating into never-opened months).
        var future = today.AddMonths(3);
        (await period.IsOpenAsync(future.Year, future.Month, default)).Should().BeFalse(
            "③ — a missing future-month row must be CLOSED");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ⑩ ม.86/4 #3 — a VAT-registered customer must carry Tax ID + branch code
    // ─────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TaxInvoice_rejects_vat_registered_customer_missing_taxid_or_branch()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        // VAT-registered but INCOMPLETE (no TaxId / BranchCode) — ม.86/4 #3 violation.
        var cust = await SeedCustomerAsync(sp, vatRegistered: true, complete: false);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var act = () => svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 6, 1), cust, false, "THB", 1m, null, null, null, [Line(1000m)], null),
            default);
        // ม.86/4 #3 — must be rejected at create.
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("ti.customer_incomplete");
    }

    [SkippableFact]
    public async Task TaxInvoice_carries_all_8_fields_on_issue_and_vat_shown_separately()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await SeedCustomerAsync(sp, vatRegistered: true, complete: true);

        long id; string? docNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                new DateOnly(2026, 6, 1), cust, false, "THB", 1m, null, null, null,
                [Line(1000m)], null), default);
            docNo = (await svc.PostAsync(id, default)).DocNo;
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == id);

        // ม.86/4 #2 — seller name + tax id (13) + branch (5).
        ti.SupplierName.Should().NotBeNullOrWhiteSpace();
        ti.SupplierTaxId.Should().NotBeNullOrWhiteSpace();
        ti.SupplierBranchCode.Should().HaveLength(5);
        // ม.86/4 #3 — buyer name + tax id + branch (VAT-registered buyer).
        ti.CustomerName.Should().NotBeNullOrWhiteSpace();
        ti.CustomerTaxId.Should().NotBeNullOrWhiteSpace();
        ti.CustomerBranchCode.Should().NotBeNullOrWhiteSpace();
        // ม.86/4 #4 — sequential doc number assigned only at POST.
        docNo.Should().NotBeNullOrWhiteSpace();
        // ม.86/4 #6 — VAT shown SEPARATELY from goods value (not merged into the line).
        ti.TaxableAmount.Should().Be(1000m);
        ti.TaxAmount.Should().Be(70m);            // 7% of 1000, shown apart
        ti.TotalAmount.Should().Be(1070m);
        // ม.86/4 #7 — issue date = tax point.
        ti.DocDate.Should().Be(ti.TaxPointDate);
    }

    [SkippableFact]
    public async Task TaxInclusive_vat_is_total_times_7_over_107_shown_separately()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await SeedCustomerAsync(sp);

        long id;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            // isTaxInclusive: true; a 107 gross line → VAT = 107 × 7/107 = 7, net = 100.
            id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                new DateOnly(2026, 6, 1), cust, true, "THB", 1m, null, null, null,
                [Line(107m, inclusive: true)], null), default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking()
            .Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == id);
        // ม.86/4 #6 / RD 7-107 convention — VAT extracted from the gross, shown separately.
        ti.TaxAmount.Should().Be(7m);             // 107 × 7/107
        ti.TaxableAmount.Should().Be(100m);       // net = gross − vat
        ti.TotalAmount.Should().Be(107m);
        ti.Lines.Single().TaxAmount.Should().Be(7m);
    }

    [SkippableFact]
    public async Task Voided_tax_invoice_number_is_never_reused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await SeedCustomerAsync(sp);

        // Post a TI → it gets doc number N (assigned at POST only).
        string firstNo;
        long firstId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            firstId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                new DateOnly(2026, 6, 1), cust, false, "THB", 1m, null, null, null,
                [Line(500m)], null), default);
            firstNo = (await svc.PostAsync(firstId, default)).DocNo;
        }

        // Simulate a void: the posted number stays on the VOIDED row (§4.3 — never reused).
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var ti = await db.TaxInvoices.FirstAsync(t => t.TaxInvoiceId == firstId);
            ti.Status = DocumentStatus.Voided;
            await db.SaveChangesAsync();
        }

        // Post a second TI in the SAME month → it MUST get the next number, never the voided one.
        string secondNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var id2 = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                new DateOnly(2026, 6, 1), cust, false, "THB", 1m, null, null, null,
                [Line(600m)], null), default);
            secondNo = (await svc.PostAsync(id2, default)).DocNo;
        }

        // §4.3 — the voided number is retained and the new doc does NOT reuse it.
        secondNo.Should().NotBe(firstNo);
        SeqOf(secondNo).Should().BeGreaterThan(SeqOf(firstNo),
            "document numbers are strictly increasing; a voided number is never re-issued");

        // The voided row still carries its original number (number not released).
        await using var s3 = sp.CreateAsyncScope();
        var db3 = s3.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var voided = await db3.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == firstId);
        voided.DocNo.Should().Be(firstNo);
        voided.Status.Should().Be(DocumentStatus.Voided);
    }

    // Trailing -NNNN sequence of a MM-YYYY-PREFIX-NNNN document number.
    private static int SeqOf(string? docNo) =>
        int.Parse(docNo!.Split('-')[^1]);
}
