using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Purchase;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Sys;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// specs/subledgers.md S5/S6(endpoint-RBAC portion) — AR Aging, Customer Statement, Vendor
/// Ledger: reconciliation correctness (the money tests), running-balance math, and endpoint-
/// level 400/403. TestCompanyFactory-isolated (money-math tests) + RbacApiFactory (HTTP-level
/// 400/403), mirroring GeneralLedgerReportTests.cs / GeneralLedgerEndpointTests.cs.
/// All postings dated today (T) or later — never a hardcoded past month (period-close footgun).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SubledgerReportTests
{
    private readonly PostgresFixture _fx;
    public SubledgerReportTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    // ── AR-side helpers ──────────────────────────────────────────────────────────────────

    private static async Task<(long Id, decimal Total)> PostTi(
        ServiceProvider sp, long customerId, decimal unitPrice, DateOnly docDate)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            docDate, customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "line", 1m, 1, "ชิ้น", unitPrice, 0m, 1, "VAT7", 0.07m)]),
            default);
        await svc.PostAsync(id, default);
        var total = await db.TaxInvoices.Where(t => t.TaxInvoiceId == id).Select(t => t.TotalAmount).FirstAsync();
        return (id, total);
    }

    private static async Task<long> PostReceipt(
        ServiceProvider sp, long customerId, long tiId, decimal applied, DateOnly docDate,
        decimal wht = 0m, int? whtTypeId = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var id = await svc.CreateDraftAsync(new CreateReceiptRequest(
            docDate, customerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(tiId, applied)], null,
            wht, whtTypeId, wht > 0 ? "WHT-" + TestIds.Suffix()[..6] : null, wht > 0 ? docDate : null),
            default);
        await svc.PostAsync(id, default);
        return id;
    }

    private static async Task<int> SvcWhtTypeId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.WhtTypes.Where(w => w.Code == "SVC" && w.EffectiveTo == null)
            .Select(w => w.WhtTypeId).FirstAsync();
    }

    // ── AP-side helpers (mirrors Sprint6SettlementTests' Seed/PostVi/CreatePv) ──────────────

    private static async Task<(long VendorId, int CatId, long ExpAcct)> SeedVendor(ServiceProvider sp, int companyId)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var expAcct = await db.ChartOfAccounts
            .Where(a => a.CompanyId == companyId && a.AccountCode == "5200")
            .Select(a => a.AccountId).FirstAsync();
        var v = new Vendor
        {
            CompanyId = companyId, VendorCode = "SL-" + TestIds.Suffix()[..8],
            VendorType = CustomerType.Corporate, NameTh = "ผู้ขายทดสอบ",
            TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
        };
        var c = new ExpenseCategory
        {
            CompanyId = companyId, CategoryCode = "SLC" + TestIds.Suffix()[..6],
            NameTh = "หมวดทดสอบ", DefaultExpenseAccountId = expAcct, DefaultIsRecoverableVat = true,
        };
        db.Vendors.Add(v); db.ExpenseCategories.Add(c);
        await db.SaveChangesAsync();
        return (v.VendorId, c.CategoryId, expAcct);
    }

    private static async Task<(long Id, decimal Total)> PostVi(
        ServiceProvider sp, int companyId, long vendorId, int catId, decimal amount, DateOnly docDate)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IVendorInvoiceService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var id = await svc.CreateDraftAsync(new CreateVendorInvoiceRequest(
            docDate, vendorId, "VT-" + TestIds.Suffix()[..6], docDate, null,
            "THB", 1m, null, [new VendorInvoiceLineInput(catId, null, "vi line", amount, 0.07m)]), default);
        db.SeedViAttachment(id, companyId); await db.SaveChangesAsync();   // VI Post requires an attachment
        await svc.PostAsync(id, default);
        var total = await db.VendorInvoices.Where(v => v.VendorInvoiceId == id).Select(v => v.TotalAmount).FirstAsync();
        return (id, total);
    }

    private static async Task<long> CreateAndPostPv(
        ServiceProvider sp, long vendorId, int catId, long expAcct, decimal amount, long viId, DateOnly docDate)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPaymentVoucherService>();
        var pvId = await svc.CreateDraftAsync(new CreatePaymentVoucherRequest(
            docDate, vendorId, catId, PaymentMethod.Transfer, null, null, null, "THB", 1m,
            "settle", null, [new PaymentVoucherLineInput(expAcct, "pv line", amount, null, 0.07m, true, null, 0m)],
            VendorInvoiceId: viId), default);
        await svc.ApproveAsync(pvId, default);
        await svc.PostAsync(pvId, default);
        return pvId;
    }

    // ── S5 — AR aging buckets ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Ar_aging_buckets_move_with_asOf_customer_filter_isolates()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today;
        var (tiId, total) = await PostTi(sp, co.CustomerId, 1000m, today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();

        async Task AssertBucket(DateOnly asOf, Func<ArAgingRow, decimal> pick)
        {
            var report = await svc.ArAgingAsync(asOf, co.CustomerId, default);
            report.Rows.Should().ContainSingle(r => r.CustomerId == co.CustomerId);
            var row = report.Rows.Single(r => r.CustomerId == co.CustomerId);
            pick(row).Should().Be(total, $"asOf={asOf} should place the full outstanding amount in this bucket");
            (row.Current + row.Bucket31To60 + row.Bucket61To90 + row.BucketOver90).Should().Be(total,
                "the other buckets must be zero — the amount lands in exactly one bucket");
        }

        await AssertBucket(today, r => r.Current);
        await AssertBucket(today.AddDays(45), r => r.Bucket31To60);
        await AssertBucket(today.AddDays(75), r => r.Bucket61To90);
        await AssertBucket(today.AddDays(100), r => r.BucketOver90);

        // customerId filter EXCLUDES: a second customer in the SAME company with its own
        // posted invoice must never leak into a query filtered to the first customer.
        long customer2Id;
        await using (var s2 = sp.CreateAsyncScope())
        {
            var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var customer2 = new Customer
            {
                CompanyId = co.CompanyId, CustomerCode = TestIds.CustomerCode(),
                CustomerType = CustomerType.Corporate, NameTh = "ลูกค้าทดสอบ 2 จำกัด",
                TaxId = "0105556123453", BranchCode = "00000", VatRegistered = true,
                BillingAddress = "99 ถ.ทดสอบ กรุงเทพฯ 10110",
                CreditLimit = 0, PaymentTermDays = 30, IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Customers.Add(customer2);
            await db.SaveChangesAsync();
            customer2Id = customer2.CustomerId;
        }
        await PostTi(sp, customer2Id, 2000m, today);

        var reportFiltered = await svc.ArAgingAsync(today, co.CustomerId, default);
        reportFiltered.Rows.Should().ContainSingle().Which.CustomerId.Should().Be(co.CustomerId);
        reportFiltered.Totals.Total.Should().Be(total,
            "the second customer's invoice must be excluded from both the rows and the totals");
    }

    // ── S5 — reconciliation balanced baseline ───────────────────────────────────────────

    [SkippableFact]
    public async Task Reconciliation_balanced_after_invoice_then_zero_after_full_receipt()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today;
        var (tiId, total) = await PostTi(sp, co.CustomerId, 1000m, today);

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();
            var report = await svc.ArAgingAsync(today, null, default);
            report.Reconciliation.ControlAccountCode.Should().Be("1130");
            report.Reconciliation.ControlAccountBalance.Should().Be(total);
            report.Reconciliation.SubLedgerTotal.Should().Be(total);
            report.Reconciliation.Difference.Should().Be(0m);
            report.Reconciliation.Balanced.Should().BeTrue();
        }

        await PostReceipt(sp, co.CustomerId, tiId, total, today);

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();
            var report = await svc.ArAgingAsync(today, null, default);
            report.Reconciliation.ControlAccountBalance.Should().Be(0m);
            report.Reconciliation.SubLedgerTotal.Should().Be(0m);
            report.Reconciliation.Balanced.Should().BeTrue();
        }
    }

    // ── S5 — WHT tie-out arbiter. Per Fable's ruling 3 (2026-07-08): if this FAILS, the gap
    //    is logged in the Attempt log and this item STOPS — no report-layer patch. ─────────

    [SkippableFact]
    public async Task Wht_tie_out_arbiter_customer_net_ar_zero_after_withheld_receipt()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today;
        var wt = await SvcWhtTypeId(sp);
        // 100 + 7% VAT = 107; customer withholds 3 (arbitrary — within the 107 applied).
        var (tiId, total) = await PostTi(sp, co.CustomerId, 100m, today);
        await PostReceipt(sp, co.CustomerId, tiId, total, today, wht: 3m, whtTypeId: wt);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();
        var statement = await svc.CustomerStatementAsync(co.CustomerId, today, today, default);
        statement.ClosingBalance.Should().Be(0m,
            "the receipt's full AppliedAmount (which already includes the WHT-withheld portion, " +
            "Sprint 8.6) must clear the invoice net-zero — WHT arbiter (spec ruling 3)");

        var report = await svc.ArAgingAsync(today, null, default);
        report.Reconciliation.Balanced.Should().BeTrue(
            "WHT arbiter: AmountPaid/AppliedAmount already include the WHT-cleared portion " +
            "(verified against ReceiptService.PostAsync/GlPostingService.PostReceiptAsync), so " +
            "the subledger must tie to 1130 even when WHT was withheld");
    }

    // ── S5 — unattributable difference is SURFACED, never hidden ───────────────────────

    [SkippableFact]
    public async Task Unattributable_manual_je_to_ar_is_surfaced_not_hidden()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today;

        long arId, cashId;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            arId = await db.ChartOfAccounts.Where(a => a.CompanyId == co.CompanyId && a.AccountCode == "1130")
                .Select(a => a.AccountId).FirstAsync();
            cashId = await db.ChartOfAccounts.Where(a => a.CompanyId == co.CompanyId && a.AccountCode == "1110")
                .Select(a => a.AccountId).FirstAsync();
        }

        const decimal d = 500m;
        long jvId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IJournalService>();
            jvId = await svc.CreateDraftAsync(new CreateJournalRequest(
                today, today, "Manual AR JE — no customer doc (subledger reconciliation test)",
                "REF-MANUAL", "THB", 1m,
                [
                    new JournalLineInput(arId, d, 0m, "dr AR", "dr-ref", null),
                    new JournalLineInput(cashId, 0m, d, "cr cash", "cr-ref", null),
                ]), default);
        }
        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IJournalService>().PostAsync(jvId, default);

        await using var s2 = sp.CreateAsyncScope();
        var subSvc = s2.ServiceProvider.GetRequiredService<ISubledgerReportService>();
        var report = await subSvc.ArAgingAsync(today, null, default);
        report.Reconciliation.ControlAccountBalance.Should().Be(d);
        report.Reconciliation.SubLedgerTotal.Should().Be(0m, "no customer document backs this JE");
        report.Reconciliation.Difference.Should().Be(d,
            "manual JE debiting AR with no customer doc must be surfaced exactly, never hidden/rounded");
        report.Reconciliation.Balanced.Should().BeFalse();
    }

    // ── S5 — customer statement running balance ─────────────────────────────────────────

    [SkippableFact]
    public async Task Customer_statement_running_balance_and_opening_excludes_prior_movements()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today;
        // TaxInvoiceService/ReceiptService ALWAYS re-pin DocDate to server "today" at POST
        // (§10 — never trusts the request's DocDate, even for a same-run "future" value), so
        // two documents posted in one test run cannot land on genuinely different real dates.
        // Range the query around a single shared DocDate instead (mirrors GeneralLedgerReportTests
        // .Opening_balance_reflects_posted_je_before_range_and_rows_exclude_it's "range starts
        // after the posting" trick) — troubles-wiki: "DocDate always server-pinned to today".
        var (tiId, tiTotal) = await PostTi(sp, co.CustomerId, 1000m, today);
        var applied = tiTotal / 2m;   // partial settle so the two lines stay distinguishable
        await PostReceipt(sp, co.CustomerId, tiId, applied, today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();

        var statement = await svc.CustomerStatementAsync(co.CustomerId, today, today.AddDays(10), default);
        statement.OpeningBalance.Should().Be(0m);
        statement.Lines.Should().HaveCount(2);
        // Same DocDate on both — deterministic DocType-rank tie-break (invoice rank 0 < receipt
        // rank 1) must still order the invoice line first, not creation/insertion order.
        statement.Lines[0].DocType.Should().Be("TaxInvoice");
        statement.Lines[0].Debit.Should().Be(tiTotal);
        statement.Lines[0].RunningBalance.Should().Be(tiTotal);
        statement.Lines[1].DocType.Should().Be("Receipt");
        statement.Lines[1].Credit.Should().Be(applied);
        statement.Lines[1].RunningBalance.Should().Be(tiTotal - applied);
        statement.ClosingBalance.Should().Be(tiTotal - applied);

        // Query starting AFTER today — both movements fall into OpeningBalance, not Lines.
        var later = await svc.CustomerStatementAsync(co.CustomerId, today.AddDays(1), today.AddDays(10), default);
        later.OpeningBalance.Should().Be(tiTotal - applied);
        later.Lines.Should().BeEmpty();
    }

    // ── Tier-2 review nit — CN/DN coverage: TaxAdjustmentNote branch in ArMovementsAsync
    //    (CN→Credit, DN→Debit) was never exercised by any S5 test until now. ───────────────

    [SkippableFact]
    public async Task Customer_statement_shows_credit_note_as_credit_and_debit_note_as_debit_and_ar_stays_balanced()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today;   // all postings dated today — closed-period footgun.
        var (tiId, tiTotal) = await PostTi(sp, co.CustomerId, 1000m, today);

        async Task<TaxAdjustmentNotePostedResult> PostNote(
            TaxAdjustmentNoteType type, string reasonCode, decimal subtotal)
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            var noteId = await svc.CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
                type, today, tiId, reasonCode, "subledger CN/DN coverage test",
                subtotal, 0.07m, "THB", 1m, null), default);
            return await svc.PostAsync(noteId, default);
        }

        var cn = await PostNote(TaxAdjustmentNoteType.Credit, nameof(CreditNoteReasonCode.AmountError), 100m);
        var dn = await PostNote(TaxAdjustmentNoteType.Debit, nameof(DebitNoteReasonCode.AdditionalCharge), 50m);

        await using var s2 = sp.CreateAsyncScope();
        var subSvc = s2.ServiceProvider.GetRequiredService<ISubledgerReportService>();
        var statement = await subSvc.CustomerStatementAsync(co.CustomerId, today, today, default);

        statement.Lines.Should().HaveCount(3);
        // Same DocDate on all three — deterministic DocType-rank tie-break: TaxInvoice(0) first,
        // then CN/DN (both rank 2) ordered by SourceId (note id), CN posted before DN.
        statement.Lines[0].DocType.Should().Be("TaxInvoice");
        statement.Lines[0].Debit.Should().Be(tiTotal);
        statement.Lines[0].RunningBalance.Should().Be(tiTotal);

        statement.Lines[1].DocType.Should().Be("CreditNote");
        statement.Lines[1].Credit.Should().Be(cn.TotalAmount, "a Credit Note reverses AR (Credit)");
        statement.Lines[1].Debit.Should().Be(0m);
        statement.Lines[1].RunningBalance.Should().Be(tiTotal - cn.TotalAmount);

        statement.Lines[2].DocType.Should().Be("DebitNote");
        statement.Lines[2].Debit.Should().Be(dn.TotalAmount, "a Debit Note increases AR (Debit)");
        statement.Lines[2].Credit.Should().Be(0m);
        var expectedClosing = tiTotal - cn.TotalAmount + dn.TotalAmount;
        statement.Lines[2].RunningBalance.Should().Be(expectedClosing);
        statement.ClosingBalance.Should().Be(expectedClosing);

        var aging = await subSvc.ArAgingAsync(today, null, default);
        aging.Reconciliation.Difference.Should().Be(0m,
            "CN/DN post balanced GL entries against 1130 — the subledger must still tie out");
        aging.Reconciliation.Balanced.Should().BeTrue();
    }

    // ── S5 — vendor ledger (AP analog, payable-positive orientation) ───────────────────

    [SkippableFact]
    public async Task Vendor_ledger_running_balance_and_ap_reconciliation_balances_after_settle()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        var today = Today;
        var (vendorId, catId, expAcct) = await SeedVendor(sp, co.CompanyId);
        var (viId, viTotal) = await PostVi(sp, co.CompanyId, vendorId, catId, 1000m, today);

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();
            var ledger = await svc.VendorLedgerAsync(vendorId, today, today, default);
            ledger.Lines.Should().ContainSingle();
            ledger.Lines[0].Credit.Should().Be(viTotal, "a Vendor Invoice increases the payable (Credit)");
            ledger.Lines[0].RunningBalance.Should().Be(viTotal, "payable-positive orientation");
            ledger.ClosingBalance.Should().Be(viTotal);
            ledger.Reconciliation.ControlAccountCode.Should().Be("2110");
            ledger.Reconciliation.ControlAccountBalance.Should().Be(viTotal);
            ledger.Reconciliation.Balanced.Should().BeTrue();
        }

        // docDate param is ignored server-side (VendorInvoiceService/PaymentVoucherService §10 —
        // always re-pinned to today at POST, same DocDate-pinning footgun as TaxInvoice/Receipt);
        // the range below still covers it regardless of the value passed here.
        await CreateAndPostPv(sp, vendorId, catId, expAcct, 1000m, viId, today);

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();
            var ledger = await svc.VendorLedgerAsync(vendorId, today, today, default);
            ledger.Lines.Should().HaveCount(2);
            ledger.Lines[1].Debit.Should().Be(viTotal, "a Payment Voucher decreases the payable (Debit)");
            ledger.ClosingBalance.Should().Be(0m);
            ledger.Reconciliation.ControlAccountBalance.Should().Be(0m);
            ledger.Reconciliation.SubLedgerTotal.Should().Be(0m);
            ledger.Reconciliation.Balanced.Should().BeTrue("matched invoice + full settle must tie to 2110");
        }
    }

    // ── S5 — errors: unknown/other-tenant party → 404 ───────────────────────────────────

    [SkippableFact]
    public async Task Unknown_customer_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();

        var act = () => svc.CustomerStatementAsync(999_999_999L, Today, Today, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("customer.not_found");
    }

    [SkippableFact]
    public async Task Unknown_vendor_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();

        var act = () => svc.VendorLedgerAsync(999_999_999L, Today, Today, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("vendor.not_found");
    }

    [SkippableFact]
    public async Task Other_tenant_customer_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var coA = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);
        await using var s = spB.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISubledgerReportService>();

        // coA's customer id, queried under company B's tenant context — EF global filter hides it.
        var act = () => svc.CustomerStatementAsync(coA.CustomerId, Today, Today, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("customer.not_found");
    }

    // ── S5/S6 — endpoint-level 400 (fromDate>toDate) + 403 (no perm), HTTP pipeline ────
    // Mirrors GeneralLedgerEndpointTests.cs exactly (RbacApiFactory, JWT, real routes).

    private static string Token(bool hasPerms) => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "subledger-endpoint-tests", CompanyId: 1, BranchId: 1,
            IsSuperAdmin: hasPerms, Roles: ["test"], Permissions: [])).Token;

    private static HttpRequestMessage Get(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [SkippableFact]
    public async Task Customer_statement_and_vendor_ledger_from_after_to_returns_400()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(hasPerms: true);

        using var csResp = await client.SendAsync(Get(
            "/reports/customer-statement?customerId=1&fromDate=2026-02-01&toDate=2026-01-01", token));
        csResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var vlResp = await client.SendAsync(Get(
            "/reports/vendor-ledger?vendorId=1&fromDate=2026-02-01&toDate=2026-01-01", token));
        vlResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task No_perm_user_gets_403_on_all_three_subledger_routes()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(hasPerms: false);

        using var arResp = await client.SendAsync(Get("/reports/ar-aging", token));
        arResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no sales.tax_invoice.read claim");

        using var csResp = await client.SendAsync(Get(
            "/reports/customer-statement?customerId=1&fromDate=2026-01-01&toDate=2026-01-31", token));
        csResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no sales.tax_invoice.read claim");

        using var vlResp = await client.SendAsync(Get(
            "/reports/vendor-ledger?vendorId=1&fromDate=2026-01-01&toDate=2026-01-31", token));
        vlResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no purchase.vendor_invoice.read claim");

        // specs/ar-aging-csv-export.md — export endpoint must be gated identically to the JSON one.
        using var exportResp = await client.SendAsync(Get("/reports/ar-aging/export", token));
        exportResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no sales.tax_invoice.read claim");
    }

    // ── specs/ar-aging-csv-export.md — CSV export: header row, data-row count vs the JSON
    //    report, UTF-8 BOM (Excel/Thai), CRLF line endings. Mirrors
    //    GeneralLedgerEndpointTests.Csv_export_returns_200_with_bom_and_row_count_matches_report;
    //    row counts checked RELATIVE to the JSON report (shared company 1 data), never absolute. ──

    [SkippableFact]
    public async Task Ar_aging_csv_export_returns_200_with_bom_crlf_and_row_count_matches_report()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(hasPerms: true);

        using var reportResp = await client.SendAsync(Get("/reports/ar-aging", token));
        reportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reportJson = JsonDocument.Parse(await reportResp.Content.ReadAsStringAsync());
        var expectedRows = reportJson.RootElement.GetProperty("rows").GetArrayLength();

        using var csvResp = await client.SendAsync(Get("/reports/ar-aging/export", token));
        csvResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await csvResp.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal([0xEF, 0xBB, 0xBF], "UTF-8 BOM so Thai text opens correctly in Excel");

        // Encoding.UTF8.GetString does NOT strip the BOM it just decoded (only StreamReader's
        // byte-order-mark detection does that) — trim the leading U+FEFF before asserting content.
        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        text.Should().Contain("\r\n", "CRLF line endings, not bare \\n");
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("Customer,TaxId,Current,Bucket31To60,Bucket61To90,BucketOver90,Total");
        // header + N data rows + totals row.
        lines.Should().HaveCount(expectedRows + 2);
    }
}
