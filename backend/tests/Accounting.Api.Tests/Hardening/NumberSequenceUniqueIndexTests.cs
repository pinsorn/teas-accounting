using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Domain.ValueObjects;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// H1 (specs/fix-duplicate-tax-doc-numbers.md) WP-4 — the DDL half. WP-1/2/3 stopped NEW cross-branch
/// duplicates by collapsing the allocator to one company-wide counter; WP-4 makes the database refuse
/// one too, on the seven tables whose unique index used to be (company_id, branch_id, doc_no) (§1.2).
/// Gated on the prod probe (§6) returning zero duplicate rows — done 2026-08-14, PROGRESS-r3-release.md
/// "DUPLICATE CLEANUP DONE".
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberSequenceUniqueIndexTests
{
    private readonly PostgresFixture _fx;
    public NumberSequenceUniqueIndexTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today(IServiceProvider sp) =>
        sp.GetRequiredService<IClock>().TodayInBangkok();

    private static async Task<long> CreateTiDraftAsync(ServiceProvider sp, long customerId, DateOnly today)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        return await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            today, customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "ขาย", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
    }

    private static async Task<string> CreateAndPostTiAsync(ServiceProvider sp, long customerId, DateOnly today)
    {
        var tiId = await CreateTiDraftAsync(sp, customerId, today);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var result = await svc.PostAsync(tiId, default);
        return result.DocNo;
    }

    /// <summary>T2. Purpose: prove the CONSTRAINT, not just the allocator — WP-1/WP-2/WP-3 make new
    /// duplicates unlikely by giving both channels one shared counter, but nothing before WP-4 stops
    /// the database from silently accepting a duplicate `doc_no` on any of the seven branch-scoped
    /// tables if two allocations still ever land on the same number (e.g. a hand-crafted row, or a
    /// future bug in the allocator). Chain: insert a `tax_adjustment_notes` row by hand, then insert a
    /// SECOND row duplicating its `doc_no` under a DIFFERENT `branch_id` — expect 23505. Pre-WP-4 (old
    /// index (company_id, branch_id, doc_no)) this insert would have SUCCEEDED, silently accepting the
    /// duplicate — that is exactly the live prod defect (co2 Repttown, §6-RESULTS). I1.</summary>
    [SkippableFact]
    public async Task Db_refuses_a_second_row_with_the_same_doc_no_under_another_branch()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
            today = Today(s.ServiceProvider);

        // A real posted TI to satisfy tax_adjustment_notes' FK on OriginalTaxInvoiceId — the note
        // itself is inserted by hand below, deliberately bypassing the allocator/service so the test
        // pins the DATABASE constraint, not application-layer behaviour.
        long originalTiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            originalTiId = await CreateTiDraftAsync(sp, t.CustomerId, today);
            await svc.PostAsync(originalTiId, default);
        }

        const string dupDocNo = "08-2026-CN-9001";

        TaxAdjustmentNote MakeNote(int branchId) => new()
        {
            CompanyId = t.CompanyId, BranchId = branchId,
            DocNo = dupDocNo, PrefixCode = "CN", NoteType = TaxAdjustmentNoteType.Credit,
            DocDate = today, TaxPointDate = today,
            OriginalTaxInvoiceId = originalTiId, Reason = "T2 — duplicate doc_no probe",
            CustomerId = t.CustomerId, CustomerName = "T2 customer", CustomerAddress = "T2 address",
            SubtotalAmount = 100m, TaxAmount = 7m, TotalAmount = 107m, TotalAmountThb = 107m,
            TaxRate = 0.07m,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        // First row: branch 0 (the web-UI shape). Must succeed — nothing wrong with it alone.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.TaxAdjustmentNotes.Add(MakeNote(branchId: 0));
            await db.SaveChangesAsync();
        }

        // Second row: SAME company, SAME doc_no, a DIFFERENT branch_id (t.BranchId is the company's
        // real, non-zero HQ branch — the exact shape of the co2 collision, API-key/MCP vs web UI).
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            db.TaxAdjustmentNotes.Add(MakeNote(branchId: t.BranchId));
            var act = async () => await db.SaveChangesAsync();

            var ex = await act.Should().ThrowAsync<DbUpdateException>(
                "the unique index on (company_id, doc_no) must refuse a cross-branch duplicate, " +
                "not silently accept it (the live co2 defect)");
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be("23505");
        }
    }

    /// <summary>T7. Purpose: the regression that matters most — <c>NumberedDocumentWriter.
    /// IsDocNoCollision</c> matches the Postgres constraint NAME for the substring "doc_no" (F6). WP-4
    /// renamed sales.tax_invoices' unique index from ix_tax_invoices_company_id_branch_id_doc_no to
    /// ix_tax_invoices_company_id_doc_no — if that rename had EVER dropped the substring, this
    /// self-healing retry would silently stop firing and every future counter drift on the seven
    /// WP-4 tables would become a raw 500 instead of a transparent retry. Chain: post real tax
    /// invoices to occupy TI doc_no 1..5 for real, drag the TI counter back below the true max (the
    /// exact prod drift shape, see NumberSequenceAmbientTxRetryTests), then drive the REAL PostAsync
    /// again and assert it succeeds by climbing past every collision on the RENAMED index — no raw
    /// DbUpdateException escapes. I5.</summary>
    [SkippableFact]
    public async Task Drifted_bucket_still_self_heals_after_the_index_rename()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);

        DateOnly today;
        await using (var s = sp.CreateAsyncScope())
            today = Today(s.ServiceProvider);

        // Occupy TI doc_no 1..max for REAL, through the actual (renamed) unique index — no hand-seeded
        // rows, so this only proves anything if the real allocator + real index actually agree.
        const int max = 5;
        for (var i = 0; i < max; i++)
            await CreateAndPostTiAsync(sp, t.CustomerId, today);

        // Drag the TI counter back BELOW the true max — WP-4's exact drift shape (mirrors
        // NumberSequenceAmbientTxRetryTests' JV drift), but on the table whose unique index THIS work
        // package renamed, not on gl.journal_entries (already company-wide pre-H1, §1.2).
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var rowsUpdated = await db.Database.ExecuteSqlRawAsync(
                "UPDATE sys.number_sequences SET current_value=1 WHERE company_id={0} AND branch_id=0 " +
                "AND prefix_code='TI' AND sub_prefix='' AND period_year={1} AND period_month={2}",
                t.CompanyId, today.Year, today.Month);
            rowsUpdated.Should().Be(1,
                "the drift-setup UPDATE must hit exactly the TI counter row for this company/period — " +
                "if the WHERE ever matches zero rows the counter silently stays at 5, the next post " +
                "allocates 6 naturally, and the Sequence.Should().Be(max + 1) assertion below passes " +
                "identically with the self-heal retry never exercised");
        }

        // The REAL ambient-tx entry point. If the renamed index ever lost the `doc_no` substring,
        // IsDocNoCollision stops matching and this raises a raw DbUpdateException instead of healing —
        // letting it propagate (no try/catch here) is the assertion.
        var tiId = await CreateTiDraftAsync(sp, t.CustomerId, today);
        string docNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var result = await svc.PostAsync(tiId, default);
            docNo = result.DocNo;
        }

        docNo.Should().NotBeNullOrEmpty();
        DocumentNumber.Parse(docNo).Sequence.Should().Be(max + 1,
            "the post must climb PAST the drift to the true max+1, healed by the retry on the " +
            "renamed index, not collide-and-500");

        // Cheap direct assertion: every one of the seven WP-4 indexes still contains "doc_no" in its
        // actual Postgres name — the exact substring NumberedDocumentWriter.IsDocNoCollision matches.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var names = await db.Database.SqlQueryRaw<string>("""
                SELECT indexname AS "Value" FROM pg_indexes
                WHERE schemaname IN ('sales','purchase','expense','fixedasset')
                  AND indexname IN (
                    'ix_tax_invoices_company_id_doc_no', 'ix_tax_adjustment_notes_company_id_doc_no',
                    'ix_receipts_company_id_doc_no', 'ix_vendor_invoices_company_id_doc_no',
                    'ix_payment_vouchers_company_id_doc_no', 'ix_expense_claims_company_id_doc_no',
                    'ix_fixed_assets_company_id_doc_no')
                """).ToListAsync();
            names.Should().HaveCount(7, "all seven WP-4 indexes must exist under their expected name");
            names.Should().OnlyContain(n => n.Contains("doc_no"),
                "every WP-4 index name must keep the `doc_no` substring — NumberedDocumentWriter's " +
                "retry matches on it (F6); losing it silently kills the self-heal for all seven tables");
        }
    }
}
