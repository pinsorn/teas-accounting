using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Review 2026-07-04 H6 — <see cref="IReceiptService.SuggestWhtBaseAsync"/> queried
/// <c>TaxInvoiceLines</c>/<c>BillingNoteLines</c> directly on caller-supplied ids with no
/// tenant scope (those line tables carry no <c>company_id</c>, so no EF filter/RLS
/// protects them — isolation only exists via the parent document). A caller in tenant A
/// could pass tenant B's TaxInvoiceId/BillingNoteId and receive B's line
/// <c>DescriptionTh</c> back in the WHT suggestion. Fixed by joining the tenant-filtered
/// parent set (<c>TaxInvoices</c> / <c>BillingNotes</c>), mirroring the safe
/// <c>TaxFilings/SalesCategorizer.cs:41</c> pattern.
///
/// This is NOT an RLS test: the leak's root cause is a missing LINQ join against an
/// EF-filtered (<see cref="Accounting.Domain.Common.ITenantOwned"/>) set, not a Postgres
/// RLS gap — the fix is enforced by the query itself regardless of DB role, so the normal
/// (superuser) test connection proves it correctly (cf. <see cref="TenantIsolationTests"/>).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class H6WhtSuggestTenantLeakTests
{
    private readonly PostgresFixture _fx;
    public H6WhtSuggestTenantLeakTests(PostgresFixture fx) => _fx = fx;

    private const string TiLeakMarker = "H6-LEAK-TI-SECRET";
    private const string BnLeakMarker = "H6-LEAK-BN-SECRET";

    [SkippableFact]
    public async Task Suggest_wht_base_returns_no_lines_for_another_tenants_tax_invoice()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long bTiId;
        await using (var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, b.CompanyId, b.BranchId))
        await using (var s = spB.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            bTiId = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                DateOnly.FromDateTime(DateTime.Now), b.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, TiLeakMarker, 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, a.CompanyId, a.BranchId);
        await using var sA = spA.CreateAsyncScope();
        var rcSvc = sA.ServiceProvider.GetRequiredService<IReceiptService>();

        var result = await rcSvc.SuggestWhtBaseAsync(
            [new ReceiptApplicationInput(bTiId, 100m)], a.CustomerId, default);

        (result.Lines ?? []).Should().BeEmpty(
            "tenant A must not see any line rows for tenant B's Tax Invoice");
        (result.Lines ?? []).Should().NotContain(
            l => l.Description == TiLeakMarker,
            "tenant B's line DescriptionTh must never leak to tenant A (H6)");
    }

    [SkippableFact]
    public async Task Suggest_wht_base_returns_no_lines_for_another_tenants_billing_note()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long bBnId;
        await using (var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, b.CompanyId, b.BranchId))
        await using (var s = spB.CreateAsyncScope())
        {
            var bnSvc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            var today = DateOnly.FromDateTime(DateTime.Now);
            bBnId = await bnSvc.CreateDraftAsync(new CreateBillingNoteRequest(
                today, today.AddDays(30), b.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, BnLeakMarker, 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, a.CompanyId, a.BranchId);
        await using var sA = spA.CreateAsyncScope();
        var rcSvc = sA.ServiceProvider.GetRequiredService<IReceiptService>();

        var result = await rcSvc.SuggestWhtBaseAsync(
            [new ReceiptApplicationInput(null, 100m, null, bBnId)], a.CustomerId, default);

        (result.Lines ?? []).Should().BeEmpty(
            "tenant A must not see any line rows for tenant B's Billing Note");
        (result.Lines ?? []).Should().NotContain(
            l => l.Description == BnLeakMarker,
            "tenant B's line DescriptionTh must never leak to tenant A (H6)");
    }
}
