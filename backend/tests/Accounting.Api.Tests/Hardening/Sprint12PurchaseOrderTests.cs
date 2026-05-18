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

    [SkippableFact]
    public async Task Approve_by_different_user_allocates_docno_same_user_is_sod()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var creator = Provider(userId: 1);
        var vid = await NewVendor(creator);
        long poId;
        await using (var s = creator.CreateAsyncScope())
            poId = await s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .CreateDraftAsync(Req(vid), default);

        // Same user (creator) → SoD violation.
        await using (var s = creator.CreateAsyncScope())
        {
            var act = () => s.ServiceProvider.GetRequiredService<IPurchaseOrderService>()
                .ApproveAsync(poId, default);
            (await act.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("po.sod_violation");
        }

        // Different user → Approved + doc_no allocated.
        await using var approver = Provider(userId: 2);
        await using (var s = approver.CreateAsyncScope())
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
    public async Task Ck_po_sod_db_check_blocks_self_approval_insert()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // IAuditable interceptor sets CreatedBy = tenant.UserId on insert, so
        // use userId 5 and ApprovedBy 5 → CreatedBy == ApprovedBy → ck_po_sod.
        await using var sp = Provider(userId: 5);
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        db.PurchaseOrders.Add(new PurchaseOrder
        {
            CompanyId = 1, BranchId = 1, Status = PurchaseOrderStatus.Approved,
            DocDate = new(2026, 5, 1), VendorName = "X",
            VendorType = CustomerType.Corporate,
            ApprovedBy = 5,                          // == CreatedBy → violates ck_po_sod
            ApprovedAt = DateTimeOffset.UtcNow,
        });
        var act = () => db.SaveChangesAsync(default);
        (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException!.Message.Should().Contain("ck_po_sod");
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
