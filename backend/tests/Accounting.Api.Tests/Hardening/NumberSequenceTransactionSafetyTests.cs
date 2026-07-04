using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Review 2026-07-04 H8 — <c>NumberSequenceService.NextAsync</c>'s UPSERT auto-commits
/// immediately when there is no ambient transaction (<c>cmd.Transaction = CurrentTransaction
/// ?.GetDbTransaction()</c>). Five paths called <c>NextAsync</c> then <c>SaveChangesAsync</c>
/// with NO enclosing transaction, so a save failure AFTER allocation left a
/// consumed-but-unused number — a permanent gap. Each is now wrapped in one explicit
/// <c>BeginTransactionAsync</c>…<c>CommitAsync</c>, mirroring the pre-existing safe
/// <c>TaxInvoiceService.PostAsync</c> shape.
///
/// Proving-test trick: after the draft document exists, stage a SECOND tracked entity on
/// the SAME (scoped) DbContext — a Customer duplicating an existing (company_id,
/// customer_code) pair, a real unique index (<c>CustomerConfiguration</c>) — WITHOUT
/// saving it. The transition method's own <c>SaveChangesAsync</c> then flushes the poison
/// insert together with its real update in one batch; the unique violation aborts the
/// whole ambient transaction, including the number-sequence UPSERT that ran on it. Each
/// test uses a FRESH <see cref="TestCompanyFactory"/> company so the sequence tuple starts
/// empty — "no gap" is asserted as "zero rows exist for that period", both before and
/// after the failed call.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberSequenceTransactionSafetyTests
{
    private readonly PostgresFixture _fx;
    public NumberSequenceTransactionSafetyTests(PostgresFixture fx) => _fx = fx;

    private static async Task PoisonAsync(AccountingDbContext db, int companyId)
    {
        var dupCode = await db.Customers.Where(c => c.CompanyId == companyId)
            .Select(c => c.CustomerCode).FirstAsync();
        db.Customers.Add(new Customer
        {
            CompanyId = companyId, CustomerCode = dupCode,   // dup (company_id, customer_code) → 23505
            CustomerType = CustomerType.Corporate, NameTh = "Poison",
            TaxId = TestIds.TaxId(), BranchCode = "00000", VatRegistered = true,
            BillingAddress = "x", CreditLimit = 0, PaymentTermDays = 30, IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<int> SeqRowCountAsync(
        AccountingDbContext db, int companyId, int branchId, string prefix, DateOnly docDate) =>
        await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM sys.number_sequences WHERE company_id={0} " +
            "AND branch_id={1} AND prefix_code={2} AND sub_prefix='' AND period_year={3} AND period_month={4}",
            companyId, branchId, prefix, docDate.Year, docDate.Month).FirstAsync();

    [SkippableFact]
    public async Task PurchaseOrder_approve_number_allocation_rolls_back_with_a_failed_save()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        long poId;
        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
            var v = new Vendor
            {
                CompanyId = t.CompanyId, VendorCode = "V-" + TestIds.Suffix(),
                NameTh = "Vendor", VendorType = CustomerType.Corporate, IsForeign = false,
            };
            db.Vendors.Add(v);
            await db.SaveChangesAsync();

            var poSvc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            poId = await poSvc.CreateDraftAsync(new CreatePurchaseOrderRequest(
                today, null, v.VendorId, null, "THB", 1m, null, null,
                [new PurchaseOrderLineInput(null, "po line", 1m, "ชิ้น", 1000m, 0m, null, null, 0m, null)]),
                default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await PoisonAsync(db, t.CompanyId);
            var poSvc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            var act = () => poSvc.ApproveAsync(poId, default);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await SeqRowCountAsync(db, t.CompanyId, t.BranchId, "PO", today)).Should().Be(0,
                "the failed save must roll back the PO number allocation too — no gap");
        }
    }

    [SkippableFact]
    public async Task BillingNote_issue_ivnumber_allocation_rolls_back_with_a_failed_save()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        long bnId;
        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
        {
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bnSvc.CreateDraftAsync(new CreateBillingNoteRequest(
                today, today.AddDays(30), t.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, "bn line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await PoisonAsync(db, t.CompanyId);
            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            var act = () => bnSvc.IssueAsync(bnId, default);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await SeqRowCountAsync(db, t.CompanyId, t.BranchId, "IV", today)).Should().Be(0,
                "the failed save must roll back the IV number allocation too — no gap");
        }
    }

    [SkippableFact]
    public async Task Quotation_send_number_allocation_rolls_back_with_a_failed_save()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        long qId;
        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
        {
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
                today, today.AddDays(30), t.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "qt line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await PoisonAsync(db, t.CompanyId);
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            var act = () => qSvc.SendAsync(qId, default);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await SeqRowCountAsync(db, t.CompanyId, t.BranchId, "QT", today)).Should().Be(0,
                "the failed save must roll back the QT number allocation too — no gap");
        }
    }

    [SkippableFact]
    public async Task SalesOrder_post_number_allocation_rolls_back_with_a_failed_save()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        long soId;
        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
        {
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
            var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            soId = await soSvc.CreateDraftAsync(new CreateSalesOrderRequest(
                today, null, t.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "so line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await PoisonAsync(db, t.CompanyId);
            var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            var act = () => soSvc.PostAsync(soId, default);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await SeqRowCountAsync(db, t.CompanyId, t.BranchId, "SO", today)).Should().Be(0,
                "the failed save must roll back the SO number allocation too — no gap");
        }
    }

    [SkippableFact]
    public async Task DeliveryOrder_issue_number_allocation_rolls_back_with_a_failed_save()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        long doId;
        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
        {
            today = s.ServiceProvider.GetRequiredService<IClock>().TodayInBangkok();
            var doSvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            doId = await doSvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                today, t.CustomerId, null, false, null, null,
                [new DeliveryLineInput(null, null, "do line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await PoisonAsync(db, t.CompanyId);
            var doSvc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            var act = () => doSvc.IssueAsync(doId, default);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            (await SeqRowCountAsync(db, t.CompanyId, t.BranchId, "DO", today)).Should().Be(0,
                "the failed save must roll back the DO number allocation too — no gap");
        }
    }
}
