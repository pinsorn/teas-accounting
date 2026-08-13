using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// specs/fix-breakit-r2-compliance.md WP-7 (Feature C) — T19-T21. R2/WP-7 (2026-08-12) deletes
/// the manual "customer has paid" endpoint (<c>BillingNoteService.MarkSettledAsync</c>); a posted
/// Receipt is now the ONLY way a BillingNote reaches Settled (I7), and the deletion must change no
/// existing row (I8).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BillingNoteSettlementDeletionTests
{
    private readonly PostgresFixture _fx;
    public BillingNoteSettlementDeletionTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    private Task<TestCompanyFactory.SeededCompany> NonVatCompanyAsync() =>
        TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);

    private static async Task<long> AccountId(ServiceProvider sp, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.ChartOfAccounts.Where(a => a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();
    }

    // Mirrors NonVatArAccrualTests.CreateAndIssueBnAsync — a Draft Invoice, one line, Issued.
    private static async Task<(long BnId, decimal Total)> CreateAndIssueBnAsync(
        ServiceProvider sp, long customerId, decimal unitPrice)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        var id = await svc.CreateDraftAsync(new CreateBillingNoteRequest(
            Today, Today.AddDays(30), customerId, null, null, null, "THB", 1m, null, null,
            [new BillingLineInput(null, null, "line", 1m, "ชิ้น", unitPrice, 0m, 1, "VAT7", 0.07m)]),
            default);
        await svc.IssueAsync(id, default);
        var detail = await svc.GetAsync(id, default);
        return (id, detail!.TotalAmount);
    }

    private static async Task<ReceiptPostedResult> PostReceiptForBnAsync(
        ServiceProvider sp, long customerId, long bnId, decimal appliedAmount)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IReceiptService>();
        var id = await svc.CreateDraftAsync(new CreateReceiptRequest(
            Today, customerId, PaymentMethod.Transfer, null, null, null, "THB", 1m, null,
            [new ReceiptApplicationInput(null, appliedAmount, null, bnId)]), default);
        return await svc.PostAsync(id, default);
    }

    // ── T19 (RED first) — the route itself is gone, proven against a REAL, live Issued
    // BillingNote under the caller's own tenant (never id=1 / a fixed company — a 404
    // for a missing or misscoped entity would be indistinguishable from "route absent"
    // and prove nothing). Pre-deletion, this exact request against this exact row
    // succeeds (204, the mutation actually fires); post-deletion it 404s because no
    // handler is mapped at all — verified empirically via `git stash` of the 3 deleted
    // surfaces, see the attempt log for the RED transcript. Super-admin token (mirrors
    // CompanyVatFlagHttpTests) bypasses ONLY the permission-policy check — CompanyId/
    // BranchId are still the seeded tenant's, so BillingNoteService.LoadAsync's
    // `Where(x => x.CompanyId == tenant.CompanyId)` genuinely resolves the row. ──────

    private static string TenantToken(int companyId, int branchId) => new JwtTokenIssuer(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer, Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey, AccessTokenMinutes = 60,
    })).Issue(new TokenClaims(
        UserId: 990_996, Username: "wp7-super", CompanyId: companyId, BranchId: branchId,
        IsSuperAdmin: true, Roles: ["SUPER_ADMIN"], Permissions: [])).Token;

    [SkippableFact]
    public async Task T19_mark_settled_route_no_longer_exists()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        long bnId;
        await using (var sp = Provider(c.CompanyId, c.BranchId))
            (bnId, _) = await CreateAndIssueBnAsync(sp, c.CustomerId, 999m);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/billing-notes/{bnId}/mark-settled");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TenantToken(c.CompanyId, c.BranchId));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed },
            $"R2/WP-7 deleted BillingNoteEndpoints.cs' mark-settled mapping entirely — BN {bnId} is a " +
            "REAL, live Issued row under the caller's own tenant, so this response is about the ROUTE, " +
            "never a missing/misscoped entity");
    }

    // ── T20 — the ONLY remaining path to Settled: a posted receipt covering the total
    // (I7). Drives the real transition (create → issue → receipt → post), never seeds
    // the target status. Also pins the R1/C6 tie-back: AR's net movement across the
    // accrual + settlement JEs is exactly zero, Dr = Cr on the settling JE. ──────────

    [SkippableFact]
    public async Task T20_posted_receipt_settles_bn_ar_net_movement_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var ar = await AccountId(sp, "1130");

        var (bnId, total) = await CreateAndIssueBnAsync(sp, c.CustomerId, 4_800m);
        var res = await PostReceiptForBnAsync(sp, c.CustomerId, bnId, total);

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bn = await db.BillingNotes.AsNoTracking().FirstAsync(b => b.BillingNoteId == bnId);
        bn.Status.Should().Be(BillingNoteStatus.Settled,
            "I7 — a posted receipt covering the total is the ONLY way to Settled now");
        bn.SettledAt.Should().NotBeNull();

        var issueJeId = bn.JournalEntryId!.Value;
        var receiptJe = await db.JournalEntries.AsNoTracking().SingleAsync(j => j.Reference == res.DocNo);
        receiptJe.TotalDebit.Should().Be(receiptJe.TotalCredit, "Dr = Cr on the settling JE");

        var arNet = await db.JournalLines
            .Where(l => (l.JournalId == issueJeId || l.JournalId == receiptJe.JournalId) && l.AccountId == ar)
            .SumAsync(l => l.DebitAmount - l.CreditAmount);
        arNet.Should().Be(0m, "R1/C6 tie-back — AR accrued at Issue is fully cleared by the settling receipt");
    }

    // ── T21 — deleting the mutator changes no existing row (I8). A BN that reached
    // Settled through the real receipt path (never seeded) keeps its Status/SettledAt/
    // JournalEntryId identical across independent re-reads — nothing in the surviving
    // code touches those columns now that MarkSettledAsync (the only OTHER writer of
    // Status=Settled/SettledAt) no longer exists. ──────────────────────────────────

    [SkippableFact]
    public async Task T21_settled_bn_fields_unchanged_across_rereads()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await NonVatCompanyAsync();
        await using var sp = Provider(c.CompanyId, c.BranchId);

        var (bnId, total) = await CreateAndIssueBnAsync(sp, c.CustomerId, 2_200m);
        await PostReceiptForBnAsync(sp, c.CustomerId, bnId, total);

        BillingNoteDetail before;
        await using (var s1 = sp.CreateAsyncScope())
            before = (await s1.ServiceProvider.GetRequiredService<IBillingNoteService>().GetAsync(bnId, default))!;
        before.Status.Should().Be("Settled");
        before.JournalEntryId.Should().NotBeNull();

        // Fresh scope, second independent read — nothing between the two reads should
        // have touched Status/SettledAt/JournalEntryId.
        await using var s2 = sp.CreateAsyncScope();
        var after = (await s2.ServiceProvider.GetRequiredService<IBillingNoteService>().GetAsync(bnId, default))!;
        after.Status.Should().Be(before.Status);
        after.JournalEntryId.Should().Be(before.JournalEntryId);

        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var bnRow = await db.BillingNotes.AsNoTracking().FirstAsync(b => b.BillingNoteId == bnId);
        bnRow.SettledAt.Should().NotBeNull();
    }
}
