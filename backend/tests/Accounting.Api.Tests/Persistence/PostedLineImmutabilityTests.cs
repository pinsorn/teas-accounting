using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Api.Tests.Fixtures;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Persistence;

/// <summary>
/// §4.2 / ม.86/4 — DB-level posting immutability on *_lines (cross-validation 2026-06-19, finding B7).
/// Header immutability is on the header tables (020/040/060/570); the *_lines tables had no DB guard,
/// so a raw UPDATE/DELETE of a POSTED document's line monetary values tripped no trigger. SqlScript
/// 580 adds BEFORE UPDATE/DELETE triggers on tax_invoice_lines / vendor_invoice_lines / receipt_lines /
/// journal_lines that block mutation once the parent leaves DRAFT.
///
/// The TI test posts a TI WITH a product-linked line — which exercises the post-time product_code
/// snapshot that PostAsync now flushes while still DRAFT (so the trigger does NOT block legit posting),
/// then proves the posted line is frozen. A DRAFT line is shown to still be mutable.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PostedLineImmutabilityTests
{
    private readonly PostgresFixture _fx;
    public PostedLineImmutabilityTests(PostgresFixture fx) => _fx = fx;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var c = new NpgsqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        return c;
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Create a TI with a product-linked line; post it iff <paramref name="post"/>. Returns its id.
    /// Posting exercises the product_code snapshot flushed while DRAFT (the split in PostAsync).</summary>
    private async Task<long> CreateTiWithProductLineAsync(bool post)
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        await using var scope = sp.CreateAsyncScope();

        var productId = await scope.ServiceProvider.GetRequiredService<IProductService>().CreateAsync(
            new CreateProductRequest(TestIds.ProductCode(), "สินค้า B7", null, "GOOD", "ชิ้น",
                DefaultUnitPrice: 1000m, null, null, null, null, null, IsSaleable: true), default);

        var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new SystemClock().TodayInBangkok(), co.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(productId, null, "สินค้า B7", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
            default);

        if (post) await svc.PostAsync(id, default);   // must NOT be blocked by the line trigger
        return id;
    }

    [SkippableFact]
    public async Task Posting_a_TI_with_a_product_line_is_not_blocked_by_the_line_trigger()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // If the split/allow logic were wrong, PostAsync (which snapshots product_code onto the line)
        // would throw a check_violation. It must succeed.
        var id = await CreateTiWithProductLineAsync(post: true);
        id.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Posted_tax_invoice_line_cannot_be_updated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var id = await CreateTiWithProductLineAsync(post: true);
        await using var c = await OpenAsync();

        var act = async () => await Exec(c,
            $"UPDATE sales.tax_invoice_lines SET line_amount = line_amount + 1 WHERE tax_invoice_id = {id}");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514"); // check_violation raised by trg_ti_lines_immutable
    }

    [SkippableFact]
    public async Task Posted_tax_invoice_line_cannot_be_deleted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var id = await CreateTiWithProductLineAsync(post: true);
        await using var c = await OpenAsync();

        var act = async () => await Exec(c,
            $"DELETE FROM sales.tax_invoice_lines WHERE tax_invoice_id = {id}");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514");
    }

    [SkippableFact]
    public async Task Draft_tax_invoice_line_can_still_be_edited()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var id = await CreateTiWithProductLineAsync(post: false);   // still DRAFT
        await using var c = await OpenAsync();

        var act = async () => await Exec(c,
            $"UPDATE sales.tax_invoice_lines SET line_amount = line_amount + 1 WHERE tax_invoice_id = {id}");
        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task Posted_journal_line_cannot_be_updated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Posting a TI auto-posts a GL journal entry + journal_lines (TaxInvoiceService → _gl.Post…).
        // This is the only behavioural exercise of gl.fn_journal_lines_immutable — it confirms the
        // trigger fires AND that its parent-FK column (journal_id) is correct: a wrong column would
        // raise 42703 (undefined_column), not 23514 (check_violation).
        int companyId;
        {
            var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
            companyId = co.CompanyId;
            await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
            await using var scope = sp.CreateAsyncScope();
            var productId = await scope.ServiceProvider.GetRequiredService<IProductService>().CreateAsync(
                new CreateProductRequest(TestIds.ProductCode(), "สินค้า B7J", null, "GOOD", "ชิ้น",
                    DefaultUnitPrice: 1000m, null, null, null, null, null, IsSaleable: true), default);
            var svc = scope.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                new SystemClock().TodayInBangkok(), co.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(productId, null, "สินค้า B7J", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
            await svc.PostAsync(id, default);
        }

        await using var c = await OpenAsync();
        var act = async () => await Exec(c, $@"
            UPDATE gl.journal_lines SET debit_amount = debit_amount
            WHERE journal_id IN (SELECT journal_id FROM gl.journal_entries
                                 WHERE company_id = {companyId} AND status = 'POSTED')");

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514"); // check_violation from trg_journal_lines_immutable
    }

    [SkippableFact]
    public async Task All_four_line_immutability_triggers_are_installed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var c = await OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            SELECT count(*) FROM pg_trigger WHERE NOT tgisinternal AND tgname IN
              ('trg_ti_lines_immutable','trg_vi_lines_immutable',
               'trg_receipt_lines_immutable','trg_journal_lines_immutable')", c);
        Convert.ToInt32(await cmd.ExecuteScalarAsync()).Should().Be(4);
    }
}
