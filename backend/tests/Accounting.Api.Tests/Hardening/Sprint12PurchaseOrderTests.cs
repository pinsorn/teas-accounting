using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 12 — PO approve/SoD (service + ck_po_sod DB CHECK), cancel,
/// Outstanding aging, cross-tenant isolation.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint12PurchaseOrderTests
{
    private readonly PostgresFixture _fx;
    public Sprint12PurchaseOrderTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    private static string Sfx() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task<long> NewVendor(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = "V-" + Sfx(), NameTh = "ผู้ขาย " + Sfx(),
            VendorType = CustomerType.Corporate, IsForeign = false,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync(default);
        return v.VendorId;
    }

    private static CreatePurchaseOrderRequest Req(long vendorId, DateOnly? expected = null) =>
        new(new DateOnly(2026, 5, 1), expected, vendorId, null, "THB", 1m, null, null,
            [new PurchaseOrderLineInput(null, "สินค้า", 10m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m, null)]);

    // §4.3 (agy review 2026-06-19) — Approve re-pins DocDate to today, when the doc-no is
    // allocated, so a draft created in a prior month doesn't keep a DocDate whose month ≠ the
    // doc-no period bucket.
    [SkippableFact]
    public async Task Approve_repins_docdate_and_buckets_docno_to_today()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider(userId: 1);
        var vid = await NewVendor(sp);
        long poId;
        await using (var s = sp.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vid), default);

        // Simulate a stale draft created last month (Draft = mutable).
        var stale = new SystemClock().TodayInBangkok().AddMonths(-1);
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var po = await db.PurchaseOrders.FirstAsync(p => p.PurchaseOrderId == poId);
            po.DocDate = stale;
            await db.SaveChangesAsync(default);
        }

        await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>().ApproveAsync(poId, default);

        await using var rs = sp.CreateAsyncScope();
        var rdb = rs.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var today = new SystemClock().TodayInBangkok();
        var approved = await rdb.PurchaseOrders.AsNoTracking().FirstAsync(p => p.PurchaseOrderId == poId);
        approved.DocDate.Should().Be(today, "Approve re-pins DocDate to today (§4.3)");
        approved.DocNo!.Should().StartWith($"{today.Month:D2}-{today.Year:D4}-",
            "the PO number must be bucketed on the approval-date period");
    }

    [SkippableFact]
    public async Task Approve_allocates_docno_creator_may_approve_permission_based()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        long poId;
        await using (var s = creator.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vid), default);

        // cont.77 — SoD relaxed: the creator (same user) may approve their own PO,
        // gated by the purchase_order.approve permission. doc_no allocated on approve.
        await using (var s = creator.CreateAsyncScope())
        {
            var r = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .ApproveAsync(poId, default);
            r.DocNo.Should().NotBeNullOrEmpty();
            var d = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .GetDetailAsync(poId, default);
            d!.Status.Should().Be("Approved");
        }
    }

    [SkippableFact]
    public async Task Self_approval_row_is_allowed_after_sod_check_dropped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // cont.77 — ck_po_sod dropped: a row with approved_by == created_by now persists
        // (approval is permission-based; the creator may approve their own PO).
        await using var sp = Provider(userId: 5);
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var po = new PurchaseOrder
        {
            CompanyId = 1, BranchId = 1, Status = PurchaseOrderStatus.Approved,
            DocDate = new(2026, 5, 1), VendorName = "X",
            VendorType = CustomerType.Corporate,
            ApprovedBy = 5,                          // == CreatedBy (set by interceptor)
            ApprovedAt = DateTimeOffset.UtcNow,
        };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(default);          // no longer throws
        po.PurchaseOrderId.Should().BeGreaterThan(0);
    }

    // BE-4b (2026-06-19) — §10 pin-to-today: the caller cannot back-date a PO. Req() supplies a
    // back-dated DocDate (2026-05-01); the service must overwrite it with today in Asia/Bangkok.
    [SkippableFact]
    public async Task Create_pins_docdate_to_today_ignoring_caller_value()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var poId = await svc.CreateDraftAsync(Req(vid), default);   // Req back-dates to 2026-05-01

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var po = await db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.PurchaseOrderId == poId);
        po.DocDate.Should().Be(new SystemClock().TodayInBangkok(),
            "§10 — DocDate is server-today; a back-dated request value must be ignored");
    }

    [SkippableFact]
    public async Task Cancel_stores_reason()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
        var poId = await svc.CreateDraftAsync(Req(vid), default);
        await svc.CancelAsync(poId, "ยกเลิกโครงการ", default);
        var d = await svc.GetDetailAsync(poId, default);
        d!.Status.Should().Be("Cancelled");
        d.CancellationReason.Should().Be("ยกเลิกโครงการ");
    }

    [SkippableFact]
    public async Task Outstanding_report_buckets_an_overdue_approved_po()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);
        var asOf = new DateOnly(2026, 6, 1);
        long poId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var db = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var po = new PurchaseOrder
            {
                CompanyId = 1, BranchId = 1, Status = PurchaseOrderStatus.Approved,
                DocDate = new(2026, 5, 1), DocNo = "PO-T-" + Sfx(),
                ExpectedDeliveryDate = asOf.AddDays(-10),   // 10 days overdue → "8-14"
                VendorId = vid, VendorName = "V", VendorType = CustomerType.Corporate,
                TotalAmount = 50000m, CreatedBy = 1, ApprovedBy = 2,
                ApprovedAt = DateTimeOffset.UtcNow,
            };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync(default);
            poId = po.PurchaseOrderId;
        }
        await using var s = sp.CreateAsyncScope();
        var rep = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
            .OutstandingAsync(asOf, vid, overdueOnly: false, default);
        var row = rep.Rows.Single(r => r.PoId == poId);
        row.DaysOverdue.Should().Be(10);
        row.AgingBucket.Should().Be("8-14");
        row.Remaining.Should().Be(50000m);
    }

    [SkippableFact]
    public async Task Cross_tenant_cannot_read_other_tenants_po()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp1 = Provider(companyId: 1);
        var vid = await NewVendor(sp1);
        long poId;
        await using (var s = sp1.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vid), default);

        await using var sp2 = Provider(companyId: 2, userId: 99);
        await using var s2 = sp2.CreateAsyncScope();
        (await s2.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
            .GetDetailAsync(poId, default)).Should().BeNull();
    }
}
