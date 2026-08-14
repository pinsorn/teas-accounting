using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Application.Master;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Ledger;

/// <summary>
/// Manual journal vouchers (specs/manual-jv-and-coa-management.md §2/§4). Covers INV-1
/// (balance, server-side), INV-2 (money invariant — the money-critical test), INV-3
/// (immutability + no mutation routes), INV-4 (period/fiscal-year/future-date gates), and
/// INV-5 (postable-account gate, incl. the cross-tenant security test).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ManualJournalTests
{
    private readonly PostgresFixture _fx;
    public ManualJournalTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp)> NewCompanyAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        return (co, TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId));
    }

    private static async Task<long> AccountId(AccountingDbContext db, string code) =>
        await db.ChartOfAccounts.Where(a => a.AccountCode == code).Select(a => a.AccountId).FirstAsync();

    // ── INV-1 — a journal balances (server-side guarantee, GlPostingService/JournalEntry) ──

    [SkippableFact]
    public async Task Unbalanced_manual_jv_is_refused_and_writes_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var desc = "Unbalanced-" + Guid.NewGuid().ToString("N")[..8];
            var req = new CreateManualJournalRequest(Today, desc, null,
            [
                new ManualJournalLineInput(bank, 100.00m, 0m, null, null),
                new ManualJournalLineInput(loan, 0m, 99.99m, null, null),
            ]);

            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
            var act = () => svc.CreateAndPostManualAsync(req, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("gl.unbalanced");

            (await db.JournalEntries.AnyAsync(j => j.Description == desc)).Should().BeFalse(
                "a refusal that leaves a row is the real failure mode");
        }
    }

    [Fact]
    public void Three_decimal_amount_is_refused_by_the_validator()
    {
        // The columns are numeric(19,4) — without the 2dp rule, 100.005/100.005 would
        // round-trip and balance on invisible satang. Pure validator unit test (no DB).
        var req = new CreateManualJournalRequest(Today, "3dp test", null,
        [
            new ManualJournalLineInput(1, 100.005m, 0m, null, null),
            new ManualJournalLineInput(2, 0m, 100.005m, null, null),
        ]);
        var result = new CreateManualJournalValidator().Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("2 decimal"));
    }

    // ── INV-2 — the money invariant (the load-bearing test) ─────────────────────────────

    [SkippableFact]
    public async Task Director_loan_jv_ties_out()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var reports = scope.ServiceProvider.GetRequiredService<IFinancialReportService>();
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");

            var tbBefore = await reports.TrialBalanceAsync(Today, includeInactive: true, default);
            var plBefore = await reports.ProfitLossAsync(
                new DateOnly(Today.Year, 1, 1), Today, null, includeUnspecified: true, default);

            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
            const decimal amount = 100_000.00m;
            var desc = "Director loan " + Guid.NewGuid().ToString("N")[..8];
            var posted = await svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, desc, null,
                [
                    new ManualJournalLineInput(bank, amount, 0m, "Bank in", null),
                    new ManualJournalLineInput(loan, 0m, amount, "Director loan", null),
                ]), default);
            posted.DocNo.Should().NotBeNullOrWhiteSpace();

            var tbAfter = await reports.TrialBalanceAsync(Today, includeInactive: true, default);
            var plAfter = await reports.ProfitLossAsync(
                new DateOnly(Today.Year, 1, 1), Today, null, includeUnspecified: true, default);

            var before = tbBefore.Rows.ToDictionary(r => r.AccountCode);
            var after = tbAfter.Rows.ToDictionary(r => r.AccountCode);

            foreach (var code in after.Keys)
            {
                var b = before.GetValueOrDefault(code);
                var a = after[code];
                var dDebit = a.Debit - (b?.Debit ?? 0m);
                var dCredit = a.Credit - (b?.Credit ?? 0m);

                if (code == "1120")
                {
                    dDebit.Should().Be(amount, "(a) Δ1120 must be +100,000.00 exactly, on the debit side");
                    dCredit.Should().Be(0m);
                }
                else if (code == "2190")
                {
                    dCredit.Should().Be(amount, "(b) Δ2190 must be +100,000.00 exactly, on the credit side");
                    dDebit.Should().Be(0m);
                }
                else
                {
                    (dDebit - dCredit).Should().Be(0m,
                        $"(c) every OTHER account's net delta must be zero — {code} moved");
                }
            }

            tbAfter.Totals.Balanced.Should().BeTrue("(d) the trial balance must still foot");
            tbAfter.Totals.Debit.Should().Be(tbAfter.Totals.Credit);

            // (e) — the invariant that catches the classic error: a director loan is a
            // LIABILITY, never income. P&L net income over the period must be unchanged,
            // because 1120/2190 are Asset/Liability accounts that never appear in the P&L.
            plAfter.Totals.NetProfit.Should().Be(plBefore.Totals.NetProfit,
                "a director loan must never be recognized as income");
        }
    }

    // ── INV-3 — immutability + no mutation routes ───────────────────────────────────────

    [SkippableFact]
    public async Task Posted_manual_jv_cannot_be_mutated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await NewCompanyAsync();
        long journalId;
        await using (sp)
        {
            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
                var bank = await AccountId(db, "1120");
                var loan = await AccountId(db, "2190");
                var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
                var posted = await svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                    Today, "Immutability test " + Guid.NewGuid().ToString("N")[..8], null,
                    [
                        new ManualJournalLineInput(bank, 500m, 0m, null, null),
                        new ManualJournalLineInput(loan, 0m, 500m, null, null),
                    ]), default);
                journalId = posted.JournalId;
            }

            await using var conn = new NpgsqlConnection(_fx.ConnectionString);
            await conn.OpenAsync();

            async Task<PostgresException> ExpectViolationAsync(string sql)
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                var act = () => cmd.ExecuteNonQueryAsync();
                return (await act.Should().ThrowAsync<PostgresException>()).Which;
            }

            (await ExpectViolationAsync(
                $"UPDATE gl.journal_entries SET total_debit = total_debit + 1 WHERE journal_id = {journalId}"))
                .SqlState.Should().Be("23514");
            (await ExpectViolationAsync(
                $"DELETE FROM gl.journal_entries WHERE journal_id = {journalId}"))
                .SqlState.Should().Be("23514");
        }
    }

    [SkippableFact]
    public async Task No_journal_mutation_routes_exist()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        var source = factory.Services.GetRequiredService<EndpointDataSource>();

        var mutating = source.Endpoints.OfType<RouteEndpoint>()
            .Where(ep => (ep.RoutePattern.RawText ?? "").TrimStart('/').StartsWith("journals", StringComparison.OrdinalIgnoreCase))
            .SelectMany(ep => (ep.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(m => (Method: m, Route: ep.RoutePattern.RawText)))
            .Where(x => x.Method is "PUT" or "DELETE")
            .ToList();

        mutating.Should().BeEmpty("a posted manual JV must never be editable or deletable via any route");
    }

    // ── INV-4 — period / fiscal-year / future-date gates (EnsureOpenAsync, reused) ──────

    [SkippableFact]
    public async Task Jv_dated_tomorrow_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
            var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today.AddDays(1), "Future date", null,
                [
                    new ManualJournalLineInput(bank, 1m, 0m, null, null),
                    new ManualJournalLineInput(loan, 0m, 1m, null, null),
                ]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.future_date");
        }
    }

    [SkippableFact]
    public async Task Jv_into_closed_month_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        {
            await using (var scope = sp.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IPeriodCloseService>()
                    .CloseAsync(Today.Year, Today.Month, "close for test", default);

            await using var scope2 = sp.CreateAsyncScope();
            var db = scope2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scope2.ServiceProvider.GetRequiredService<IJournalService>();
            var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, "Closed month", null,
                [
                    new ManualJournalLineInput(bank, 1m, 0m, null, null),
                    new ManualJournalLineInput(loan, 0m, 1m, null, null),
                ]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("period.closed");
        }
    }

    [SkippableFact]
    public async Task Jv_into_a_reopened_month_succeeds()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        {
            await using (var scope = sp.CreateAsyncScope())
            {
                var periods = scope.ServiceProvider.GetRequiredService<IPeriodCloseService>();
                await periods.CloseAsync(Today.Year, Today.Month, "close for test", default);
                await periods.ReopenAsync(Today.Year, Today.Month, "reopen for test", default);
            }

            await using var scope2 = sp.CreateAsyncScope();
            var db = scope2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scope2.ServiceProvider.GetRequiredService<IJournalService>();
            var posted = await svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, "Reopened month", null,
                [
                    new ManualJournalLineInput(bank, 1m, 0m, null, null),
                    new ManualJournalLineInput(loan, 0m, 1m, null, null),
                ]), default);
            posted.DocNo.Should().NotBeNullOrWhiteSpace(
                "the gate is the only thing blocking back-dating, and back-dating is legitimately reachable");
        }
    }

    [SkippableFact]
    public async Task Jv_into_closed_fiscal_year_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await NewCompanyAsync();
        await using (sp)
        {
            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
                db.FiscalYearCloses.Add(new FiscalYearClose
                {
                    CompanyId = co.CompanyId, FiscalYear = Today.Year,
                    FiscalStartDate = new DateOnly(Today.Year, 1, 1),
                    FiscalEndDate = new DateOnly(Today.Year, 12, 31),
                    ClosedAt = DateTimeOffset.UtcNow, ClosedBy = 1,
                });
                await db.SaveChangesAsync();
            }

            await using var scope2 = sp.CreateAsyncScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db2, "1120");
            var loan = await AccountId(db2, "2190");
            var svc = scope2.ServiceProvider.GetRequiredService<IJournalService>();
            var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, "FY closed", null,
                [
                    new ManualJournalLineInput(bank, 1m, 0m, null, null),
                    new ManualJournalLineInput(loan, 0m, 1m, null, null),
                ]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.year_closed");
        }
    }

    // ── INV-5 — only postable accounts accept postings (the security test) ──────────────

    [SkippableFact]
    public async Task Jv_to_header_account_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var headerId = await coa.CreateAsync(new CreateAccountRequest(
                "9800", "หัวข้อทดสอบ", "Test Header", AccountType.Asset, null, true, NormalBalance.Debit), default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
            var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, "Header account", null,
                [
                    new ManualJournalLineInput(headerId, 1m, 0m, null, null),
                    new ManualJournalLineInput(bank, 0m, 1m, null, null),
                ]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.account_is_header");
        }
    }

    [SkippableFact]
    public async Task Jv_to_inactive_account_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var accountId = await coa.CreateAsync(new CreateAccountRequest(
                "9801", "บัญชีปิดใช้งาน", "Inactive account", AccountType.Asset, null, false, NormalBalance.Debit), default);
            await coa.UpdateAsync(accountId,
                new UpdateAccountRequest("บัญชีปิดใช้งาน", "Inactive account", false, false), default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
            var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, "Inactive account", null,
                [
                    new ManualJournalLineInput(accountId, 1m, 0m, null, null),
                    new ManualJournalLineInput(bank, 0m, 1m, null, null),
                ]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.account_inactive");
        }
    }

    /// <summary>THE security test (spec INV-5) — journal_lines has no company_id and no FK to
    /// chart_of_accounts, so RLS cannot stop a forged/foreign account id. Server-side validation
    /// in JournalService IS the only control.</summary>
    [SkippableFact]
    public async Task Jv_to_another_companys_account_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (coA, spA) = await NewCompanyAsync();
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);

        long foreignAccountId;
        await using (var scopeB = spB.CreateAsyncScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<AccountingDbContext>();
            foreignAccountId = await AccountId(dbB, "1120");   // company B's REAL bank account
        }

        await using (spA)
        {
            var desc = "Cross-tenant attempt " + Guid.NewGuid().ToString("N")[..8];
            await using (var scopeA = spA.CreateAsyncScope())
            {
                var dbA = scopeA.ServiceProvider.GetRequiredService<AccountingDbContext>();
                var ownBank = await AccountId(dbA, "1130"); // company A's own AR account (any valid postable)
                var svc = scopeA.ServiceProvider.GetRequiredService<IJournalService>();

                var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                    Today, desc, null,
                    [
                        new ManualJournalLineInput(foreignAccountId, 1m, 0m, null, null),
                        new ManualJournalLineInput(ownBank, 0m, 1m, null, null),
                    ]), default);
                (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.account_not_found");
            }

            // Zero journal rows in BOTH companies — not a 500, not a partial post.
            await using (var scopeA = spA.CreateAsyncScope())
            {
                var dbA = scopeA.ServiceProvider.GetRequiredService<AccountingDbContext>();
                (await dbA.JournalEntries.AnyAsync(j => j.Description == desc)).Should().BeFalse();
            }
            await using (var scopeB = spB.CreateAsyncScope())
            {
                var dbB = scopeB.ServiceProvider.GetRequiredService<AccountingDbContext>();
                (await dbB.JournalEntries.AnyAsync(j => j.Description == desc)).Should().BeFalse();
            }
        }
    }

    // ── Tier-2 F1 — per-line BusinessUnitId is a client-supplied FK; RLS cannot stop a
    // forged/foreign id (same reasoning as the account gate above — a Postgres FK check runs
    // as the table owner and bypasses RLS). Mirrors Jv_to_another_companys_account_is_refused. ──

    [SkippableFact]
    public async Task Jv_with_another_companys_business_unit_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, spA) = await NewCompanyAsync();
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);

        int foreignBuId;
        await using (var scopeB = spB.CreateAsyncScope())
        {
            var buSvc = scopeB.ServiceProvider.GetRequiredService<IBusinessUnitService>();
            foreignBuId = await buSvc.CreateAsync(
                new CreateBusinessUnitRequest("FBU", "หน่วยบริษัท B", "Foreign BU", null), default);
        }

        await using (spA)
        {
            var desc = "Cross-tenant BU attempt " + Guid.NewGuid().ToString("N")[..8];
            await using var scopeA = spA.CreateAsyncScope();
            var db = scopeA.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scopeA.ServiceProvider.GetRequiredService<IJournalService>();

            // Company B's REAL, live business-unit id — small sequential ints, easy to guess —
            // stamped onto a line posted from company A.
            var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, desc, null,
                [
                    new ManualJournalLineInput(bank, 1m, 0m, null, foreignBuId),
                    new ManualJournalLineInput(loan, 0m, 1m, null, null),
                ]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.invalid");

            (await db.JournalEntries.AnyAsync(j => j.Description == desc)).Should().BeFalse(
                "a refusal that leaves a row is the real failure mode — a forged BU must persist nothing");
        }
    }

    // ── Tier-2 F1b — Company.RequiresBusinessUnit must be enforced on Revenue/Expense lines;
    // a balance-sheet line (asset/liability/equity) stays exempt — BU only concerns P&L
    // reporting, and a director-loan entry (bank + a liability) has no meaningful BU. ──

    [SkippableFact]
    public async Task Jv_to_expense_account_without_bu_is_refused_when_company_requires_it()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IBusinessUnitService>()
                .SetCompanyRequiresBuAsync(true, default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");   // Asset — exempt, needs no BU
            var rent = await AccountId(db, "5100");   // Expense — must carry a BU on this company
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();

            var act = () => svc.CreateAndPostManualAsync(new CreateManualJournalRequest(
                Today, "Rent without BU", null,
                [
                    new ManualJournalLineInput(rent, 1m, 0m, null, null),
                    new ManualJournalLineInput(bank, 0m, 1m, null, null),
                ]), default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("bu.required");
        }
    }

    // ── PostAsync gate (Tier-2 R2b, mcp-manual-journal.md §10) ──────────────────────────────
    // The draft-then-post lifecycle (CreateDraftAsync + PostAsync) previously had ZERO
    // account/period/fiscal-year validation anywhere: CreateDraftAsync doesn't check (§1),
    // and PostAsync used to just allocate a doc number and mark posted. These gates are
    // EXTRACTED from CreateAndPostManualAsync's own checks (above) so both paths share one
    // implementation, not a duplicate. Drafts are created directly via CreateDraftAsync
    // (bypassing the MCP tool's own gate) so PostAsync's OWN defense-in-depth is what's
    // actually under test — matching how a REST /journals/ POST caller (no account
    // validation there either) could otherwise persist a bad draft.

    [SkippableFact]
    public async Task Post_of_draft_with_header_account_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var headerId = await coa.CreateAsync(new CreateAccountRequest(
                "9810", "หัวข้อทดสอบ post", "Test Header (post)", AccountType.Asset, null, true, NormalBalance.Debit), default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();

            var draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "Header account (post gate)", null, "THB", 1m,
                [
                    new JournalLineInput(headerId, 1m, 0m, null, null, null),
                    new JournalLineInput(bank, 0m, 1m, null, null, null),
                ]), default);

            var act = () => svc.PostAsync(draftId, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.account_is_header");
        }
    }

    [SkippableFact]
    public async Task Post_of_draft_with_inactive_account_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountService>();
            var accountId = await coa.CreateAsync(new CreateAccountRequest(
                "9811", "บัญชีปิดใช้งาน post", "Inactive (post)", AccountType.Asset, null, false, NormalBalance.Debit), default);
            await coa.UpdateAsync(accountId,
                new UpdateAccountRequest("บัญชีปิดใช้งาน post", "Inactive (post)", false, false), default);

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();

            var draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "Inactive account (post gate)", null, "THB", 1m,
                [
                    new JournalLineInput(accountId, 1m, 0m, null, null, null),
                    new JournalLineInput(bank, 0m, 1m, null, null, null),
                ]), default);

            var act = () => svc.PostAsync(draftId, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.account_inactive");
        }
    }

    [SkippableFact]
    public async Task Post_of_draft_with_foreign_account_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, spA) = await NewCompanyAsync();
        var coB = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);

        long foreignAccountId;
        await using (var scopeB = spB.CreateAsyncScope())
        {
            var dbB = scopeB.ServiceProvider.GetRequiredService<AccountingDbContext>();
            foreignAccountId = await AccountId(dbB, "1130");
        }

        await using (spA)
        await using (var scopeA = spA.CreateAsyncScope())
        {
            var dbA = scopeA.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var ownBank = await AccountId(dbA, "1120");
            var svc = scopeA.ServiceProvider.GetRequiredService<IJournalService>();

            // CreateDraftAsync performs NO account validation (§1) — a foreign id sails
            // straight into a persisted draft; PostAsync must be the one to catch it.
            var draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "Foreign account (post gate)", null, "THB", 1m,
                [
                    new JournalLineInput(foreignAccountId, 1m, 0m, null, null, null),
                    new JournalLineInput(ownBank, 0m, 1m, null, null, null),
                ]), default);

            var act = () => svc.PostAsync(draftId, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.account_not_found");
        }
    }

    [SkippableFact]
    public async Task Post_of_draft_into_closed_period_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        {
            // Close the period FIRST — PeriodCloseService.CloseAsync itself refuses to close a
            // period with outstanding DRAFT documents in it, so the only way to get a draft
            // sitting inside an already-closed period is to close first, then draft (CreateDraftAsync
            // performs no period check at all, so it succeeds regardless — §1).
            await using (var scope = sp.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IPeriodCloseService>()
                    .CloseAsync(Today.Year, Today.Month, "close for post-gate test", default);

            long draftId;
            await using (var scope = sp.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
                var bank = await AccountId(db, "1120");
                var loan = await AccountId(db, "2190");
                var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
                draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                    Today, Today, "Closed period (post gate)", null, "THB", 1m,
                    [
                        new JournalLineInput(bank, 1m, 0m, null, null, null),
                        new JournalLineInput(loan, 0m, 1m, null, null, null),
                    ]), default);
            }

            await using (var scope = sp.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
                var act = () => svc.PostAsync(draftId, default);
                (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("period.closed");
            }
        }
    }

    // ── R1/C1 (WP-3) — the posting-seam precision guard (JournalEntry.MarkPosted) ───────────
    // §3.1: THB is a 2-decimal currency. The seam REJECTS, never rounds. T12/T13.

    [Fact]
    public void T12a_draft_request_with_3dp_line_is_refused_by_the_validator()
    {
        // Mirrors Three_decimal_amount_is_refused_by_the_validator above, but for the DRAFT
        // path's CreateJournalValidator (JournalDtos.cs:85-108) — the one MCP/REST /journals
        // POST uses, which today has NO precision rule at all (unlike CreateManualJournalValidator).
        var req = new CreateJournalRequest(Today, Today, "3dp draft test", null, "THB", 1m,
        [
            new JournalLineInput(1, 100.005m, 0m, null, null, null),
            new JournalLineInput(2, 0m, 100.005m, null, null, null),
        ]);
        var result = new CreateJournalValidator().Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("2 decimal"));
    }

    [SkippableFact]
    public async Task T12b_posting_a_pre_seeded_4dp_draft_is_refused_and_stays_draft()
    {
        // CreateDraftAsync performs no precision validation (mirrors the existing
        // account/period gates above) — so a 4dp draft can be seeded directly, exactly
        // like Post_of_draft_with_header_account_is_refused seeds a bad account. MarkPosted
        // (the seam every posting path shares) must be the one to catch it.
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();

            var draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "4dp draft (post gate)", null, "THB", 1m,
                [
                    new JournalLineInput(bank, 100.005m, 0m, null, null, null),
                    new JournalLineInput(loan, 0m, 100.005m, null, null, null),
                ]), default);

            var act = () => svc.PostAsync(draftId, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.precision");

            var entry = await db.JournalEntries.AsNoTracking().SingleAsync(j => j.JournalId == draftId);
            entry.Status.Should().Be(DocumentStatus.Draft, "a refused post must not mark the entry Posted");
            entry.DocNo.Should().BeNull("no JV number may be consumed by a refused post");
        }
    }

    [SkippableFact]
    public async Task T13_4dp_lines_that_net_balanced_are_rejected_in_full_no_row_no_number()
    {
        // C4 F1's live repro: 33.3333 / 33.3333 / 33.3334 nets to 100.0000 == Cr 100.00 at the
        // HEADER (IsBalanced passes), but no individual line is clean at 2dp. Rounding each line
        // to 2dp would give 33.33x3=99.99 != 100.00 — the seam must reject in full, not round,
        // not plug. I14, I15.
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();

            var draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "33.3333 split (post gate)", null, "THB", 1m,
                [
                    new JournalLineInput(bank, 33.3333m, 0m, null, null, null),
                    new JournalLineInput(bank, 33.3333m, 0m, null, null, null),
                    new JournalLineInput(bank, 33.3334m, 0m, null, null, null),
                    new JournalLineInput(loan, 0m, 100.00m, null, null, null),
                ]), default);

            var act = () => svc.PostAsync(draftId, default);
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("je.precision");

            var entry = await db.JournalEntries.AsNoTracking().SingleAsync(j => j.JournalId == draftId);
            entry.Status.Should().Be(DocumentStatus.Draft);
            entry.DocNo.Should().BeNull("not rounded, not plugged — the entry is refused in full");
        }
    }

    [SkippableFact]
    public async Task Post_of_valid_draft_still_succeeds_unchanged_happy_path()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using (sp)
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();

            var draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "Happy path (post gate)", null, "THB", 1m,
                [
                    new JournalLineInput(bank, 1m, 0m, null, null, null),
                    new JournalLineInput(loan, 0m, 1m, null, null, null),
                ]), default);

            var posted = await svc.PostAsync(draftId, default);
            posted.DocNo.Should().NotBeNullOrWhiteSpace("a valid draft must still post cleanly after R2b's new gates");
        }
    }

    // ── R3/H2 — concurrent double-post race ─────────────────────────────────────────────
    // Two callers race /journals/{id}/post on the SAME draft. JournalEntry.Version is never
    // incremented by MarkPosted (unlike PaymentVoucher post-WP-B), so EF's own optimistic-
    // concurrency WHERE clause never diverges between the two writers — what actually stops
    // the loser from corrupting the winner's already-committed row is
    // gl.trg_je_immutable (020_journal_immutability.sql), which RAISEs a raw 23514
    // check_violation when the loser's UPDATE tries to change doc_no/status on a row that is
    // now POSTED. Unwrapped, that surfaces as a raw DbUpdateException/PostgresException — a
    // 500. PostAsync must translate it into a clean 409 without disturbing the winner.

    [SkippableFact]
    public async Task Concurrent_double_post_race_posts_exactly_once_and_the_loser_gets_a_clean_409()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (_, sp) = await NewCompanyAsync();
        await using var spDisposable = sp;

        long draftId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var bank = await AccountId(db, "1120");
            var loan = await AccountId(db, "2190");
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
            draftId = await svc.CreateDraftAsync(new CreateJournalRequest(
                Today, Today, "Race " + Guid.NewGuid().ToString("N")[..8], null, "THB", 1m,
                [
                    new JournalLineInput(bank, 1m, 0m, null, null, null),
                    new JournalLineInput(loan, 0m, 1m, null, null, null),
                ]), default);
        }

        async Task<(bool Ok, Exception? Ex)> TryPost()
        {
            await using var scope = sp.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IJournalService>();
            try { await svc.PostAsync(draftId, default); return (true, null); }
            catch (Exception ex) { return (false, ex); }
        }

        var results = await Task.WhenAll(Task.Run(TryPost), Task.Run(TryPost));
        results.Count(r => r.Ok).Should().Be(1, "a double-post race must post exactly once — never two winners");
        var loserEx = results.Single(r => !r.Ok).Ex!;
        loserEx.Should().BeOfType<DomainException>(
            "the loser must surface a clean domain error, not a raw unmapped DB exception (500)");
        // Mirrors ExpenseClaimServiceTests.Double_pay_race_posts_exactly_one_journal_entry — the
        // loser hits EITHER the immutability-race translation (its fresh-load happened WHILE the
        // winner's write was in flight) OR the ordinary je.not_draft status guard (its fresh-load
        // already observed the winner's committed Posted row). Both are a correct rejection;
        // which one fires is a timing accident, not something the caller controls.
        ((DomainException)loserEx).Code.Should().BeOneOf("je.locked_mismatch", "je.not_draft");

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var entry = await db2.JournalEntries.AsNoTracking().SingleAsync(j => j.JournalId == draftId);
        entry.Status.Should().Be(DocumentStatus.Posted, "the winner must win — undisturbed by the loser");
        entry.DocNo.Should().NotBeNullOrWhiteSpace();
    }
}
