using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Identity;
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
/// specs/fix-review-n-findings-2026-08-17.md — N2: at most one POSTED Tax Invoice per
/// Quotation (ม.86/4). Only POSTED TIs block (N2.1 — a TI has no delete/cancel/void path, so
/// an all-status guard would let one abandoned draft trap the quotation forever). Covers all
/// three write channels: CreateFromQuotationAsync/plain-create (G1), draft re-link (G2), post
/// (G3).
///
/// Race behaviour (two concurrent posts hitting the 23505 from the new unique index) is NOT
/// unit-tested here — it needs two connections racing inside one test and is flaky on a shared
/// teas_test. It is covered by (a) the constraint-name-scoped catch being unit-reachable via
/// the code path review, and (b) the §N2.5 deploy probe proving the unique index exists.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class QuotationSingleInvoiceTests
{
    private readonly PostgresFixture _fx;
    public QuotationSingleInvoiceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    private static TaxInvoiceLineInput PlainLine() =>
        new(null, null, "line", 1m, 1, "ครั้ง", 1000m, 0m, null, "VAT7", 0.07m);

    private static async Task<long> CreateAcceptedQuotationAsync(ServiceProvider sp, long customerId)
    {
        await using var s = sp.CreateAsyncScope();
        var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
            Today(), Today().AddDays(30), customerId, null, "THB", 1m, null, null,
            [new ChainLineInput(null, "line 1", 1m, "ชิ้น", 1000m, 0m, null, "VAT7", 0.07m)]), default);
        await qSvc.SendAsync(qId, default);
        await qSvc.AcceptAsync(qId, default);
        return qId;
    }

    // ── T-N2 — service-level ──────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Second_tax_invoice_from_an_invoiced_quotation_is_refused()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var qId = await CreateAcceptedQuotationAsync(sp, c.CustomerId);

        string firstDocNo;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var tiId = await tiSvc.CreateFromQuotationAsync(qId, default);
            var posted = await tiSvc.PostAsync(tiId, default);
            firstDocNo = posted.DocNo;
        }

        await using var s2 = sp.CreateAsyncScope();
        var tiSvc2 = s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        Func<Task> act = () => tiSvc2.CreateFromQuotationAsync(qId, default);

        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("quotation.already_invoiced");
        ex.Which.Message.Should().Contain(firstDocNo, "the refusal must name the blocking document");
    }

    [SkippableFact]
    public async Task Second_tax_invoice_from_a_quotation_with_only_a_draft_is_allowed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var qId = await CreateAcceptedQuotationAsync(sp, c.CustomerId);

        await using var s = sp.CreateAsyncScope();
        var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var tiId1 = await tiSvc.CreateFromQuotationAsync(qId, default);   // left Draft, deliberately
        var tiId2 = await tiSvc.CreateFromQuotationAsync(qId, default);

        // The anti-trap test: if this fails, someone made the guard count drafts and
        // re-created the no-exit state a draft TI cannot be deleted from (§N2.1).
        tiId2.Should().NotBe(tiId1);
    }

    [SkippableFact]
    public async Task A_draft_cannot_be_posted_once_a_sibling_was_posted()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var qId = await CreateAcceptedQuotationAsync(sp, c.CustomerId);

        long draftAId, draftBId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            draftAId = await tiSvc.CreateFromQuotationAsync(qId, default);
            draftBId = await tiSvc.CreateFromQuotationAsync(qId, default);
        }

        TaxInvoicePostedResult postedA;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            postedA = await tiSvc.PostAsync(draftAId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var tiSvc2 = s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        Func<Task> act = () => tiSvc2.PostAsync(draftBId, default);
        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("quotation.already_invoiced");

        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var tiA = await db.TaxInvoices.AsNoTracking().FirstAsync(x => x.TaxInvoiceId == draftAId);
        tiA.Status.Should().Be(DocumentStatus.Posted);
        tiA.DocNo.Should().Be(postedA.DocNo, "A stays Posted with its DocNo intact");
    }

    [SkippableFact]
    public async Task Plain_create_with_a_quotation_id_is_guarded_too()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var qId = await CreateAcceptedQuotationAsync(sp, c.CustomerId);

        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var tiId = await tiSvc.CreateFromQuotationAsync(qId, default);
            await tiSvc.PostAsync(tiId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var tiSvc2 = s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        // Covers C2 — the channel the original review missed.
        Func<Task> act = () => tiSvc2.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today(), c.CustomerId, false, "THB", 1m, null, null, null,
            [PlainLine()], null, QuotationId: qId), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("quotation.already_invoiced");
    }

    [SkippableFact]
    public async Task Update_draft_cannot_relink_to_an_invoiced_quotation()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var q1Id = await CreateAcceptedQuotationAsync(sp, c.CustomerId);

        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var q1TiId = await tiSvc.CreateFromQuotationAsync(q1Id, default);
            await tiSvc.PostAsync(q1TiId, default);
        }

        long standaloneDraftId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            standaloneDraftId = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [PlainLine()], null), default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var tiSvc2 = s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        // Covers C3 — MCP update_tax_invoice_draft re-linking a draft.
        Func<Task> act = () => tiSvc2.UpdateDraftAsync(standaloneDraftId, new CreateTaxInvoiceRequest(
            Today(), c.CustomerId, false, "THB", 1m, null, null, null,
            [PlainLine()], null, QuotationId: q1Id), default);

        (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("quotation.already_invoiced");
    }

    [SkippableFact]
    public async Task Update_draft_can_re_save_its_own_quotation_link()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var qId = await CreateAcceptedQuotationAsync(sp, c.CustomerId);

        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await tiSvc.CreateFromQuotationAsync(qId, default);   // left Draft
        }

        await using var s2 = sp.CreateAsyncScope();
        var tiSvc2 = s2.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        Func<Task> act = () => tiSvc2.UpdateDraftAsync(tiId, new CreateTaxInvoiceRequest(
            Today(), c.CustomerId, false, "THB", 1m, null, null, null,
            [PlainLine()], null, QuotationId: qId), default);

        // Proves the excludeTaxInvoiceId argument works — an ordinary draft edit is not bricked.
        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task Tax_invoice_with_no_quotation_is_never_blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        async Task<long> CreateAndPostAsync()
        {
            await using var s = sp.CreateAsyncScope();
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            var tiId = await tiSvc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [PlainLine()], null), default);
            await tiSvc.PostAsync(tiId, default);
            return tiId;
        }

        // The partial index's "quotation_id IS NOT NULL" arm — two null-quotation TIs, both posted.
        var first = await CreateAndPostAsync();
        var second = await CreateAndPostAsync();

        first.Should().NotBe(second);
    }

    // ── API-level 409 mapping ────────────────────────────────────────────────────

    private async Task<long> CreateUserAsync(int companyId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('sys.users','user_id'), " +
            "(SELECT COALESCE(MAX(user_id),0)+1 FROM sys.users), false);");
        var user = new User
        {
            Username = "u-" + TestIds.Suffix(), Email = TestIds.Email(), PasswordHash = "x",
            FullName = TestIds.Name(), IsActive = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    private static string Token(long userId, int companyId, IEnumerable<string> perms) =>
        new JwtTokenIssuer(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer, Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey, AccessTokenMinutes = 60,
        })).Issue(new TokenClaims(
            UserId: userId, Username: $"qsi-{userId}", CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: false, Roles: [], Permissions: perms.ToList())).Token;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    [SkippableFact]
    public async Task Error_code_maps_to_409()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var qId = await CreateAcceptedQuotationAsync(sp, c.CustomerId);
        var userId = await CreateUserAsync(c.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, c.CompanyId,
            ["sales.quotation.manage", "sales.tax_invoice.create", "sales.tax_invoice.post"]);

        using var first = await PostAsync(client, token, $"/quotations/{qId}/create-tax-invoice");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var tiId = firstBody.RootElement.GetProperty("tax_invoice_id").GetInt64();

        using var postResp = await PostAsync(client, token, $"/tax-invoices/{tiId}/post");
        postResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var second = await PostAsync(client, token, $"/quotations/{qId}/create-tax-invoice");
        ((int)second.StatusCode).Should().Be(409);
        var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("title").GetString().Should().Be("quotation.already_invoiced");
    }
}
