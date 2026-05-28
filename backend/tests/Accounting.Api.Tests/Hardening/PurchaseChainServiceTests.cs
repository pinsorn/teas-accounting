using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Purchase;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Purchase;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// F (Question-Backend36) — IPurchaseChainService walks both directions from any anchor.
/// We seed a PO + a VI that points at it directly via the DbContext (the chain resolver
/// is read-only, so the production validation paths aren't exercised), then probe each
/// anchor and assert the DTO surfaces both ends. Adding PV/WHT to this fixture would
/// require running the full PV approve-and-post pipeline with attachments + SoD — that
/// chain is already covered by purchase-chain.spec.ts on the e2e side.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PurchaseChainServiceTests
{
    private readonly PostgresFixture _fx;
    public PurchaseChainServiceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider() =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContext>(new StubTenant
            {
                CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false,
            })
            .AddDbContext<AccountingDbContext>(o =>
                o.UseNpgsql(_fx.ConnectionString).UseSnakeCaseNamingConvention())
            .AddScoped<IPurchaseChainService, PurchaseChainService>()
            .BuildServiceProvider();

    [SkippableFact]
    public async Task Chain_resolves_both_directions_from_PO_or_VI_anchor()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var sp = Provider();
        long poId; long viId; string poDocNo; string viDocNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            // Suffix keeps doc_no UNIQUE across re-runs on the shared teas_test DB.
            var sfx = Guid.NewGuid().ToString("N")[..8];
            poDocNo = $"PO-CHAIN-{sfx}";
            viDocNo = $"VI-CHAIN-{sfx}";

            var po = new PurchaseOrder
            {
                CompanyId = 1, BranchId = 1,
                DocNo = poDocNo, DocDate = new DateOnly(2026, 5, 16),
                Status = PurchaseOrderStatus.Approved,
                VendorId = 1, VendorName = "ผู้ขายทดสอบ chain",
                VendorType = CustomerType.Corporate,
                TotalAmount = 1500m, SubtotalAmount = 1500m,
            };
            db.PurchaseOrders.Add(po);
            await db.SaveChangesAsync();
            poId = po.PurchaseOrderId;

            var vi = new VendorInvoice
            {
                CompanyId = 1, BranchId = 1,
                DocNo = viDocNo, DocDate = new DateOnly(2026, 5, 16),
                VendorTaxInvoiceNo = $"VTI-{sfx}",
                VendorTaxInvoiceDate = new DateOnly(2026, 5, 10),
                VatClaimPeriod = 202605,
                VendorId = 1, VendorName = "ผู้ขายทดสอบ chain",
                VendorType = CustomerType.Corporate,
                PurchaseOrderId = poId,
                Status = DocumentStatus.Posted,
                TotalAmount = 1500m,
            };
            db.VendorInvoices.Add(vi);
            await db.SaveChangesAsync();
            viId = vi.VendorInvoiceId;
        }

        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPurchaseChainService>();

        // Anchor at the PO — walk DOWN should pick up the VI.
        var fromPo = await svc.GetAsync("purchase-order", poId, default);
        fromPo.Should().NotBeNull();
        fromPo!.PurchaseOrder.Should().NotBeNull();
        fromPo.PurchaseOrder!.DocNo.Should().Be(poDocNo);
        fromPo.VendorInvoices.Should().ContainSingle(v => v.Id == viId && v.DocNo == viDocNo);
        fromPo.PaymentVouchers.Should().BeEmpty();
        fromPo.WhtCertificates.Should().BeEmpty();

        // Anchor at the VI — walk UP should pick up the PO; the VI itself stays in the list.
        var fromVi = await svc.GetAsync("vendor-invoice", viId, default);
        fromVi.Should().NotBeNull();
        fromVi!.PurchaseOrder.Should().NotBeNull();
        fromVi.PurchaseOrder!.Id.Should().Be(poId);
        fromVi.VendorInvoices.Should().ContainSingle(v => v.Id == viId);

        // Unknown anchor → null (not 500).
        (await svc.GetAsync("nope", 1, default)).Should().BeNull();

        // Missing id → null (tenant existence check).
        (await svc.GetAsync("purchase-order", 99_999_999L, default)).Should().BeNull();
    }
}
