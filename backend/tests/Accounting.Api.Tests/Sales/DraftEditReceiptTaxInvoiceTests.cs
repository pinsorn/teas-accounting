using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Audit;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// specs/draft-edit-receipt-taxinvoice.md WP-1 — T1..T10. Web draft-edit for Receipt and Tax
/// Invoice: PUT /receipts/{id}, PUT /tax-invoices/{id}, GET .../draft-input, plus the
/// edit-vs-post race fix (row lock FOR UPDATE + Version++ in both UpdateDraftAsync). T11 (RBAC) is
/// RbacAuthMapTests/RbacCartesianTests, which enumerate the new routes by themselves.
/// Behavioural tests drive the real transitions (service/HTTP); only T9 uses a raw status flip
/// because its point is the row lock.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DraftEditReceiptTaxInvoiceTests
{
    private readonly PostgresFixture _fx;
    public DraftEditReceiptTaxInvoiceTests(PostgresFixture fx) => _fx = fx;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private const string RcPerm = "sales.receipt.create";
    private const string TiPerm = "sales.tax_invoice.create";

    // ── Harness ──────────────────────────────────────────────────────────────

    private ServiceProvider Sp(TestCompanyFactory.SeededCompany co) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);

    private static DateOnly Today() => new SystemClock().TodayInBangkok();

    private Task<TestCompanyFactory.SeededCompany> VatCo() =>
        TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    private static string Token(TestCompanyFactory.SeededCompany co) =>
        Issuer().Issue(new TokenClaims(
            UserId: 990_777, Username: "draft-edit-test", CompanyId: co.CompanyId, BranchId: co.BranchId,
            IsSuperAdmin: false, Roles: ["DRAFT_EDIT_TEST"], Permissions: [RcPerm, TiPerm])).Token;

    private static async Task<(int Status, string Body)> SendAsync(
        HttpClient http, HttpMethod method, string route, string token, string? json = null)
    {
        using var req = new HttpRequestMessage(method, route);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await http.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private static Task<(int Status, string Body)> PutAsync(HttpClient h, string route, string token, object body) =>
        SendAsync(h, HttpMethod.Put, route, token, JsonSerializer.Serialize(body, Web));

    private static Task<(int Status, string Body)> GetAsync(HttpClient h, string route, string token) =>
        SendAsync(h, HttpMethod.Get, route, token);

    // Request builders
    private static TaxInvoiceLineInput TiLine(string desc, decimal qty = 1m, decimal price = 1000m,
        decimal discount = 0m, string? code = "VAT7", decimal rate = 0.07m, string? type = null) =>
        new(null, null, desc, qty, 1, "หน่วย", price, discount, null, code, rate, type);

    private static CreateTaxInvoiceRequest TiReq(TestCompanyFactory.SeededCompany co, string? notes,
        params TaxInvoiceLineInput[] lines) =>
        new(Today(), co.CustomerId, false, "THB", 1m, notes, null, null, lines);

    private static CreateReceiptRequest RcApplyReq(TestCompanyFactory.SeededCompany co, string? notes,
        IReadOnlyList<ReceiptApplicationInput> apps, IReadOnlyList<ReceiptWhtLineInput>? wht = null,
        PaymentMethod pm = PaymentMethod.Transfer) =>
        new(Today(), co.CustomerId, pm, null, null, null, "THB", 1m, notes, apps, WhtLines: wht);

    private static CreateReceiptRequest RcLinesReq(TestCompanyFactory.SeededCompany co, string? notes,
        params ReceiptLineInput[] lines) =>
        new(Today(), co.CustomerId, PaymentMethod.Cash, null, null, null, "THB", 1m, notes,
            Applications: [], Lines: lines);

    private async Task<long> CreateTiAsync(TestCompanyFactory.SeededCompany co, CreateTaxInvoiceRequest req)
    {
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CreateDraftAsync(req, default);
    }

    /// <summary>A POSTED TI of 1,000 + 7% = 1,070.</summary>
    private async Task<long> PostedTiAsync(TestCompanyFactory.SeededCompany co)
    {
        var id = await CreateTiAsync(co, TiReq(co, null, TiLine("สินค้า")));
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().PostAsync(id, default);
        return id;
    }

    private async Task<long> CreateRcAsync(TestCompanyFactory.SeededCompany co, CreateReceiptRequest req)
    {
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        return await s.ServiceProvider.GetRequiredService<IReceiptService>().CreateDraftAsync(req, default);
    }

    private async Task<int> SvcWhtTypeIdAsync(TestCompanyFactory.SeededCompany co)
    {
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.WhtTypes.Where(w => w.CompanyId == co.CompanyId && w.Code == "SVC")
            .Select(w => w.WhtTypeId).FirstAsync();
    }

    // Snapshot helpers ─ reflection over scalar columns, so a NEW persisted column is covered
    // automatically (the whole point of the I2/I4 invariants).
    private static string Fmt(object o, params string[] skip)
    {
        var parts = new List<string>();
        foreach (var p in o.GetType().GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (skip.Contains(p.Name)) continue;
            var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (!(t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
                  || t == typeof(DateOnly) || t == typeof(DateTimeOffset) || t == typeof(DateTime))) continue;
            parts.Add($"{p.Name}={Convert.ToString(p.GetValue(o), CultureInfo.InvariantCulture)}");
        }
        return string.Join(";", parts);
    }

    private static readonly string[] EditNoise = ["UpdatedAt", "UpdatedBy", "Version"];
    private static readonly string[] CreateNoise =
    [
        "UpdatedAt", "UpdatedBy", "Version", "CreatedAt", "CreatedBy", "ReceiptId", "TaxInvoiceId",
        "DocDate", "TaxPointDate", "CreatedViaApiKeyId", "CreatedViaApiKeyName",
        "IdempotencyKey", "IdempotencyRequestHash",
    ];

    private async Task<List<string>> RcGraphAsync(TestCompanyFactory.SeededCompany co, long id, string[] headerSkip)
    {
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var r = await db.Receipts.AsNoTracking().Include(x => x.Applications).Include(x => x.Lines)
            .Include(x => x.WhtLines).FirstAsync(x => x.ReceiptId == id);
        var g = new List<string> { "H:" + Fmt(r, headerSkip) };
        g.AddRange(r.Applications.OrderBy(a => a.ApplicationId).Select(a => "A:" + Fmt(a, "ApplicationId", "ReceiptId")));
        g.AddRange(r.Lines.OrderBy(l => l.LineNo).Select(l => "L:" + Fmt(l, "ReceiptLineId", "ReceiptId")));
        g.AddRange(r.WhtLines.OrderBy(w => w.ReceiptWhtLineId).Select(w => "W:" + Fmt(w, "ReceiptWhtLineId", "ReceiptId")));
        return g;
    }

    private async Task<List<string>> TiGraphAsync(TestCompanyFactory.SeededCompany co, long id, string[] headerSkip)
    {
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var t = await db.TaxInvoices.AsNoTracking().Include(x => x.Lines).FirstAsync(x => x.TaxInvoiceId == id);
        var g = new List<string> { "H:" + Fmt(t, headerSkip) };
        g.AddRange(t.Lines.OrderBy(l => l.LineNo).Select(l => "L:" + Fmt(l, "LineId", "TaxInvoiceId")));
        return g;
    }

    private async Task<(int Jes, List<string> Seqs)> CountersAsync(TestCompanyFactory.SeededCompany co)
    {
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var jes = await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId);
        var seqs = (await db.NumberSequences.AsNoTracking().Where(n => n.CompanyId == co.CompanyId).ToListAsync())
            .Select(n => Fmt(n)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return (jes, seqs);
    }

    private async Task<int> UpdatedActivityAsync(TestCompanyFactory.SeededCompany co, string entityType, long id)
    {
        await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Set<ActivityLog>().CountAsync(a =>
            a.EntityType == entityType && a.EntityId == id && a.ActivityType == "Updated");
    }

    // ── T1 — TI edit via HTTP ────────────────────────────────────────────────

    [SkippableFact]
    public async Task T1_TaxInvoice_edit_via_http_equals_fresh_create_and_books_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        var id = await CreateTiAsync(co, TiReq(co, "orig", TiLine("A", 1m, 1000m), TiLine("B", 2m, 300m)));
        var before = await CountersAsync(co);

        var edited = TiReq(co, "edited",
            TiLine("A", 3m, 1000m),
            TiLine("B", 2m, 300m, discount: 10m),
            TiLine("C exempt", 1m, 500m, code: "EXEMPT-AGRI", rate: 0m, type: "EXEMPT_GOOD"));

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var (status, body) = await PutAsync(http, $"/tax-invoices/{id}", Token(co), edited);
        status.Should().Be(204, body);

        var after = await CountersAsync(co);
        after.Jes.Should().Be(before.Jes, "an edit books no journal entry");
        after.Seqs.Should().Equal(before.Seqs, "an edit allocates no document number");
        (await UpdatedActivityAsync(co, "TaxInvoice", id)).Should().Be(1);

        var freshId = await CreateTiAsync(co, edited);
        var a = await TiGraphAsync(co, id, CreateNoise);
        var b = await TiGraphAsync(co, freshId, CreateNoise);
        a.Should().Equal(b, "I2: edit == fresh create for the same body");
        a[0].Should().Contain("DocNo=;", "DocNo stays NULL on a draft");
    }

    // ── T2 — TI round-trip ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task T2_TaxInvoice_round_trip_is_a_no_op()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var token = Token(co);

        // (a) POST-created: discount + inclusive + notes + paymentTerms + dueDate
        var ra = new CreateTaxInvoiceRequest(Today(), co.CustomerId, true, "THB", 1m,
            "หมายเหตุ", "Net 30", Today().AddDays(30),
            [TiLine("L1", 2m, 535m, discount: 10m), TiLine("L2 exempt", 1m, 100m, code: "EXEMPT-AGRI", rate: 0m, type: "EXEMPT_GOOD")]);
        var idA = await CreateTiAsync(co, ra);

        // (b) from an Accepted quotation (carries QuotationId)
        long qId;
        await using (var sp = Sp(co))
        await using (var s = sp.CreateAsyncScope())
        {
            var q = s.ServiceProvider.GetRequiredService<IQuotationService>();
            qId = await q.CreateDraftAsync(new CreateQuotationRequest(
                Today(), Today().AddDays(30), co.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "q line", 2m, "ชิ้น", 1250.00m, 15m, 1, "VAT7", 0.07m)]), default);
            await q.SendAsync(qId, default);
            await q.AcceptAsync(qId, default);
        }
        long idB;
        await using (var sp = Sp(co))
        await using (var s = sp.CreateAsyncScope())
            idB = await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().CreateFromQuotationAsync(qId, default);

        foreach (var id in new[] { idA, idB })
        {
            var snap1 = await TiGraphAsync(co, id, EditNoise);
            var (gs, json) = await GetAsync(http, $"/tax-invoices/{id}/draft-input", token);
            gs.Should().Be(200, json);
            var (ps, pbody) = await PutAsync(http, $"/tax-invoices/{id}", token, JsonDocument.Parse(json).RootElement);
            ps.Should().Be(204, pbody);
            var snap2 = await TiGraphAsync(co, id, EditNoise);
            snap2.Should().Equal(snap1, $"I4: PUT(GET draft-input) of TI {id} must change no persisted column");
        }

        await using var sp2 = Sp(co); await using var s2 = sp2.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.TaxInvoices.AsNoTracking().Where(t => t.TaxInvoiceId == idB).Select(t => t.QuotationId).FirstAsync())
            .Should().Be(qId, "the quotation link survives the round trip");
    }

    // ── T3 — RC edit (VAT, money) ────────────────────────────────────────────

    [SkippableFact]
    public async Task T3_Receipt_edit_replaces_applications_then_post_settles_the_edited_amount()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        var tiA = await PostedTiAsync(co);
        var tiB = await PostedTiAsync(co);
        var wht = await SvcWhtTypeIdAsync(co);
        var rcId = await CreateRcAsync(co, RcApplyReq(co, "orig",
            [new ReceiptApplicationInput(tiA, 500m)], [new ReceiptWhtLineInput(wht, 400m)]));
        var before = await CountersAsync(co);

        var edited = RcApplyReq(co, "edited",
            [new ReceiptApplicationInput(tiA, 1070m)], [new ReceiptWhtLineInput(wht, 1000m)]);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var (status, body) = await PutAsync(http, $"/receipts/{rcId}", Token(co), edited);
        status.Should().Be(204, body);

        var after = await CountersAsync(co);
        after.Jes.Should().Be(before.Jes);
        after.Seqs.Should().Equal(before.Seqs);

        await using (var sp = Sp(co))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var rc = await db.Receipts.AsNoTracking().Include(r => r.Applications).Include(r => r.WhtLines)
                .FirstAsync(r => r.ReceiptId == rcId);
            rc.DocNo.Should().BeNull();
            rc.Amount.Should().Be(1070m);
            rc.Applications.Should().ContainSingle().Which.AppliedAmount.Should().Be(1070m);
            rc.WhtLines.Should().ContainSingle().Which.BaseAmount.Should().Be(1000m);
            rc.WhtAmount.Should().Be(rc.WhtLines.Single().WhtAmount).And.BeGreaterThan(0m);
            var a = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiA);
            a.AmountPaid.Should().Be(0m, "I3: a draft reserves nothing");
            a.PaymentStatus.Should().Be("UNPAID");
        }

        var freshId = await CreateRcAsync(co, edited);
        (await RcGraphAsync(co, rcId, CreateNoise)).Should().Equal(await RcGraphAsync(co, freshId, CreateNoise),
            "I2: edit == fresh create for the same body");

        ReceiptPostedResult posted;
        await using (var sp = Sp(co))
        await using (var s = sp.CreateAsyncScope())
            posted = await s.ServiceProvider.GetRequiredService<IReceiptService>().PostAsync(rcId, default);
        await using (var sp = Sp(co))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var a = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiA);
            a.AmountPaid.Should().Be(1070m, "post settles the EDITED amount");
            a.PaymentStatus.Should().Be("PAID");
            posted.CashReceived.Should().Be(1070m - posted.WhtAmount);
            var b = await db.TaxInvoices.AsNoTracking().FirstAsync(t => t.TaxInvoiceId == tiB);
            b.AmountPaid.Should().Be(0m);
        }
    }

    // ── T4 — RC round-trip ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task T4_Receipt_round_trip_is_a_no_op_vat_invoice_and_standalone()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        async Task RoundTripAsync(HttpClient http, TestCompanyFactory.SeededCompany co, long rcId)
        {
            var snap1 = await RcGraphAsync(co, rcId, EditNoise);
            var (gs, json) = await GetAsync(http, $"/receipts/{rcId}/draft-input", Token(co));
            gs.Should().Be(200, json);
            var (ps, pbody) = await PutAsync(http, $"/receipts/{rcId}", Token(co), JsonDocument.Parse(json).RootElement);
            ps.Should().Be(204, pbody);
            (await RcGraphAsync(co, rcId, EditNoise)).Should().Equal(snap1,
                $"I4: PUT(GET draft-input) of RC {rcId} must change no persisted column");
        }

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        // (a) VAT receipt: cheque + notes + BU + WHT + cert
        var vat = await VatCo();
        int buId;
        await using (var sp = Sp(vat)) await using (var s = sp.CreateAsyncScope())
            buId = await s.ServiceProvider.GetRequiredService<IBusinessUnitService>()
                .CreateAsync(new CreateBusinessUnitRequest("DE1", "หน่วย DE1", "DE1", null), default);
        var tiA = await PostedTiAsync(vat);
        var wht = await SvcWhtTypeIdAsync(vat);
        var rcA = await CreateRcAsync(vat, new CreateReceiptRequest(
            Today(), vat.CustomerId, PaymentMethod.Cheque, "CHQ-001", Today(), null, "THB", 1m, "โน้ต",
            Applications: [new ReceiptApplicationInput(tiA, 800m)], BusinessUnitId: buId,
            CustomerWhtCertNo: "50T-1", CustomerWhtCertDate: Today(),
            WhtLines: [new ReceiptWhtLineInput(wht, 500m)]));
        await RoundTripAsync(http, vat, rcA);

        // (b) non-VAT: issued Invoice (BillingNote) -> receipt applying it; (c) standalone 2 lines
        var non = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        long bnId;
        await using (var sp = Sp(non)) await using (var s = sp.CreateAsyncScope())
        {
            var bn = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
            bnId = await bn.CreateDraftAsync(new CreateBillingNoteRequest(
                Today(), Today().AddDays(30), non.CustomerId, null, null, null, "THB", 1m, null, null,
                [new BillingLineInput(null, null, "งาน", 1m, "งาน", 1000m, 0m, null, null, 0m)]), default);
            await bn.IssueAsync(bnId, default);
        }
        var rcB = await CreateRcAsync(non, RcApplyReq(non, "bn",
            [new ReceiptApplicationInput(null, 1000m, null, bnId)]));
        await RoundTripAsync(http, non, rcB);

        long productId;
        await using (var sp = Sp(non)) await using (var s = sp.CreateAsyncScope())
            productId = await s.ServiceProvider.GetRequiredService<IProductService>().CreateAsync(new CreateProductRequest(
                TestIds.ProductCode(), "สินค้า DE", null, "GOOD", "ชิ้น", 50m,
                null, null, null, null, null, IsSaleable: true), default);
        var rcC = await CreateRcAsync(non, RcLinesReq(non, "standalone",
            new ReceiptLineInput("บรรทัด 1", 2m, 100m, 200m, productId, null, "GOOD", "ชิ้น"),
            new ReceiptLineInput("บรรทัด 2", 1m, 50m, 50m, null, null, "SERVICE", null)));
        await RoundTripAsync(http, non, rcC);
    }

    // ── T5 — over-application ────────────────────────────────────────────────

    [SkippableFact]
    public async Task T5_Receipt_edit_over_application_is_refused_and_leaves_db_unchanged()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        var ti = await PostedTiAsync(co);
        var firstRc = await CreateRcAsync(co, RcApplyReq(co, "first", [new ReceiptApplicationInput(ti, 500m)]));
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IReceiptService>().PostAsync(firstRc, default);   // outstanding = 570

        var rcId = await CreateRcAsync(co, RcApplyReq(co, "draft", [new ReceiptApplicationInput(ti, 100m)]));
        var snap = await RcGraphAsync(co, rcId, []);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var (s600, b600) = await PutAsync(http, $"/receipts/{rcId}", Token(co),
            RcApplyReq(co, "over", [new ReceiptApplicationInput(ti, 600m)]));
        s600.Should().Be(422, b600);
        b600.Should().Contain("rc.overpaid");
        (await RcGraphAsync(co, rcId, [])).Should().Equal(snap, "a refused edit leaves row + children untouched");

        var (s570, b570) = await PutAsync(http, $"/receipts/{rcId}", Token(co),
            RcApplyReq(co, "ok", [new ReceiptApplicationInput(ti, 570m)]));
        s570.Should().Be(204, b570);
    }

    // ── T6 — posted ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task T6_Posted_documents_cannot_be_edited_or_prefilled()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        var tiId = await PostedTiAsync(co);
        var rcId = await CreateRcAsync(co, RcApplyReq(co, "x", [new ReceiptApplicationInput(tiId, 100m)]));
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IReceiptService>().PostAsync(rcId, default);
        var tiSnap = await TiGraphAsync(co, tiId, []);
        var rcSnap = await RcGraphAsync(co, rcId, []);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var token = Token(co);

        var (s1, b1) = await PutAsync(http, $"/tax-invoices/{tiId}", token, TiReq(co, "nope", TiLine("z")));
        s1.Should().Be(422, b1); b1.Should().Contain("ti.cannot_edit_after_post");
        var (s2, b2) = await PutAsync(http, $"/receipts/{rcId}", token,
            RcApplyReq(co, "nope", [new ReceiptApplicationInput(tiId, 100m)]));
        s2.Should().Be(422, b2); b2.Should().Contain("rc.cannot_edit_after_post");
        var (s3, b3) = await GetAsync(http, $"/tax-invoices/{tiId}/draft-input", token);
        s3.Should().Be(422, b3); b3.Should().Contain("ti.cannot_edit_after_post");
        var (s4, b4) = await GetAsync(http, $"/receipts/{rcId}/draft-input", token);
        s4.Should().Be(422, b4); b4.Should().Contain("rc.cannot_edit_after_post");

        (await TiGraphAsync(co, tiId, [])).Should().Equal(tiSnap);
        (await RcGraphAsync(co, rcId, [])).Should().Equal(rcSnap);
    }

    // ── T7 — cross-company + RLS ─────────────────────────────────────────────

    [SkippableFact]
    public async Task T7_Cross_company_is_404_and_the_row_lock_is_rls_scoped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var a = await VatCo();
        var b = await VatCo();
        var rcA = await CreateRcAsync(a, RcLinesReq(a, "a-rc", new ReceiptLineInput("x", 1m, 100m, 100m)));
        var tiA = await CreateTiAsync(a, TiReq(a, "a-ti", TiLine("x")));
        var rcSnap = await RcGraphAsync(a, rcA, []);
        var tiSnap = await TiGraphAsync(a, tiA, []);

        await using (var factory = new RbacApiFactory(_fx.ConnectionString))
        {
            using var http = factory.CreateClient();
            var tokenB = Token(b);
            (await PutAsync(http, $"/receipts/{rcA}", tokenB, RcLinesReq(b, "b", new ReceiptLineInput("y", 1m, 5m, 5m)))).Status.Should().Be(404);
            (await GetAsync(http, $"/receipts/{rcA}/draft-input", tokenB)).Status.Should().Be(404);
            (await PutAsync(http, $"/tax-invoices/{tiA}", tokenB, TiReq(b, "b", TiLine("y")))).Status.Should().Be(404);
            (await GetAsync(http, $"/tax-invoices/{tiA}/draft-input", tokenB)).Status.Should().Be(404);
        }
        (await RcGraphAsync(a, rcA, [])).Should().Equal(rcSnap);
        (await TiGraphAsync(a, tiA, [])).Should().Equal(tiSnap);

        // RLS leg — pg_database_owner has no implicit privileges and FOR UPDATE needs UPDATE.
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        async Task Exec(string sql) { await using var c = new NpgsqlCommand(sql, conn); await c.ExecuteNonQueryAsync(); }
        async Task<long> LockCount(string table, string idCol, long id)
        {
            await using var c = new NpgsqlCommand(
                $"SELECT count(*) FROM (SELECT {idCol} FROM {table} WHERE {idCol} = {id} FOR UPDATE) s", conn);
            return Convert.ToInt64(await c.ExecuteScalarAsync());
        }
        await Exec("GRANT USAGE ON SCHEMA sales TO pg_database_owner; " +
                   "GRANT SELECT, UPDATE ON sales.receipts, sales.tax_invoices TO pg_database_owner;");
        try
        {
            await Exec("SET ROLE pg_database_owner");
            await using var tx = await conn.BeginTransactionAsync();
            await Exec($"SELECT set_config('app.company_id', '{b.CompanyId}', false), set_config('app.bypass_rls', 'false', false)");
            (await LockCount("sales.receipts", "receipt_id", rcA)).Should().Be(0, "company B cannot lock A's receipt");
            (await LockCount("sales.tax_invoices", "tax_invoice_id", tiA)).Should().Be(0, "company B cannot lock A's TI");
            await Exec($"SELECT set_config('app.company_id', '{a.CompanyId}', false)");
            (await LockCount("sales.receipts", "receipt_id", rcA)).Should().Be(1);
            (await LockCount("sales.tax_invoices", "tax_invoice_id", tiA)).Should().Be(1);
            await tx.RollbackAsync();
        }
        finally
        {
            await Exec("RESET ROLE");
        }
    }

    // ── T8 — idempotency fence untouched ─────────────────────────────────────

    [SkippableFact]
    public async Task T8_Edit_does_not_touch_the_idempotency_fence_columns()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        string apiKey;
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            apiKey = (await s.ServiceProvider.GetRequiredService<IApiKeyService>()
                .CreateAsync(new CreateApiKeyRequest(TestIds.Name("de-fence"), [RcPerm, TiPerm]), default)).Plaintext;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var token = Token(co);

        async Task<HttpResponseMessage> V1Post(string path, string key, string json)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            return await http.SendAsync(req);
        }
        static long IdOf(HttpResponseMessage r) => long.Parse(r.Headers.Location!.ToString().Split('/').Last());

        var rcBody = JsonSerializer.Serialize(RcLinesReq(co, "t8-rc", new ReceiptLineInput("x", 1m, 100m, 100m)), Web);
        var tiBody = JsonSerializer.Serialize(TiReq(co, "t8-ti", TiLine("x")), Web);
        var rcKey = $"t8rc-{Guid.NewGuid():N}";
        var tiKey = $"t8ti-{Guid.NewGuid():N}";
        var rcResp = await V1Post("/api/v1/receipts", rcKey, rcBody);
        rcResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var tiResp = await V1Post("/api/v1/tax-invoices", tiKey, tiBody);
        tiResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var rcId = IdOf(rcResp); var tiId = IdOf(tiResp);

        async Task<(string Rc, string Ti)> FenceAsync()
        {
            await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var r = await db.Receipts.AsNoTracking().FirstAsync(x => x.ReceiptId == rcId);
            var t = await db.TaxInvoices.AsNoTracking().FirstAsync(x => x.TaxInvoiceId == tiId);
            return ($"{r.CreatedViaApiKeyId}|{r.CreatedViaApiKeyName}|{r.IdempotencyKey}|{r.IdempotencyRequestHash}",
                    $"{t.CreatedViaApiKeyId}|{t.CreatedViaApiKeyName}|{t.IdempotencyKey}|{t.IdempotencyRequestHash}");
        }
        var before = await FenceAsync();
        before.Rc.Should().Contain(rcKey); before.Ti.Should().Contain(tiKey);

        (await PutAsync(http, $"/receipts/{rcId}", token,
            RcLinesReq(co, "t8-rc-edited", new ReceiptLineInput("changed", 2m, 100m, 200m)))).Status.Should().Be(204);
        (await PutAsync(http, $"/tax-invoices/{tiId}", token,
            TiReq(co, "t8-ti-edited", TiLine("changed", 2m)))).Status.Should().Be(204);

        (await FenceAsync()).Should().Be(before, "I7: an edit never rewrites the fence tuple");

        var rcReplay = await V1Post("/api/v1/receipts", rcKey, rcBody);
        rcReplay.StatusCode.Should().Be(HttpStatusCode.Created);
        IdOf(rcReplay).Should().Be(rcId, "replay of the original keyed create converges on the edited draft");
        var tiReplay = await V1Post("/api/v1/tax-invoices", tiKey, tiBody);
        tiReplay.StatusCode.Should().Be(HttpStatusCode.Created);
        IdOf(tiReplay).Should().Be(tiId);

        await using var sp2 = Sp(co); await using var s2 = sp2.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db2.Receipts.CountAsync(r => r.IdempotencyKey == rcKey)).Should().Be(1);
        (await db2.TaxInvoices.CountAsync(t => t.IdempotencyKey == tiKey)).Should().Be(1);
    }

    // ── T9 — edit-vs-post race (discriminating) ──────────────────────────────
    // WITHOUT the FOR UPDATE row lock, the first half's edit does not block on the row and
    // completes (and, on a real post, would rewrite a posted VAT receipt's applications — no
    // trigger guards them). WITHOUT Version++, the second half's post-side UPDATE still matches
    // WHERE version = old and no DbUpdateConcurrencyException is raised.

    private async Task<NpgsqlConnection> OpenConnAsync()
    {
        var c = new NpgsqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        return c;
    }

    private static async Task ExecAsync(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task T9_Receipt_edit_blocks_on_the_row_lock_then_refuses_a_now_posted_row_and_a_stale_post_loses()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        var tiA = await PostedTiAsync(co);
        var tiB = await PostedTiAsync(co);

        // Half 1 — post holds the row lock (simulated), edit must wait, then see Posted.
        var rcId = await CreateRcAsync(co, RcApplyReq(co, "t9", [new ReceiptApplicationInput(tiA, 1070m)]));
        await using (var x = await OpenConnAsync())
        {
            await ExecAsync(x, "BEGIN");
            await ExecAsync(x, $"SELECT receipt_id FROM sales.receipts WHERE receipt_id = {rcId} FOR UPDATE");

            var edit = Task.Run(async () =>
            {
                await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
                await s.ServiceProvider.GetRequiredService<IReceiptService>().UpdateDraftAsync(rcId,
                    RcApplyReq(co, "changed", [new ReceiptApplicationInput(tiB, 1070m)]), default);
            });
            await Task.Delay(1000);
            edit.IsCompleted.Should().BeFalse("the edit must wait on the row lock held by the 'post'");

            // test-only raw transition - the point is the lock
            await ExecAsync(x, $"UPDATE sales.receipts SET status='POSTED', doc_no='T9-' || receipt_id, posted_at=now() WHERE receipt_id={rcId}");
            await ExecAsync(x, "COMMIT");

            var act = async () => await edit;
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("rc.cannot_edit_after_post");
        }
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var apps = await db.ReceiptApplications.AsNoTracking().Where(a => a.ReceiptId == rcId).ToListAsync();
            apps.Should().ContainSingle().Which.TaxInvoiceId.Should().Be(tiA, "the posted receipt's settlement rows were not re-pointed");
        }

        // Half 2 — the edit commits between post's load and post's UPDATE: post must lose.
        var rc2 = await CreateRcAsync(co, RcApplyReq(co, "t9b", [new ReceiptApplicationInput(tiA, 1070m)]));
        await using var staleSp = Sp(co); await using var staleScope = staleSp.CreateAsyncScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var stale = await staleDb.Receipts.Include(r => r.Applications).FirstAsync(r => r.ReceiptId == rc2);

        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IReceiptService>().UpdateDraftAsync(rc2,
                RcApplyReq(co, "edited-wins", [new ReceiptApplicationInput(tiB, 1070m)]), default);

        stale.MarkPosted("T9B-" + rc2, 1, DateTimeOffset.UtcNow);
        var post = () => staleDb.SaveChangesAsync();
        await post.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the edit bumped Version, so the stale post's UPDATE ... WHERE version = old misses");
    }

    [SkippableFact]
    public async Task T9_TaxInvoice_edit_blocks_on_the_row_lock_then_refuses_a_now_posted_row_and_a_stale_post_loses()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();

        var tiId = await CreateTiAsync(co, TiReq(co, "t9", TiLine("orig", 1m, 1000m)));
        await using (var x = await OpenConnAsync())
        {
            await ExecAsync(x, "BEGIN");
            await ExecAsync(x, $"SELECT tax_invoice_id FROM sales.tax_invoices WHERE tax_invoice_id = {tiId} FOR UPDATE");

            var edit = Task.Run(async () =>
            {
                await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
                await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().UpdateDraftAsync(tiId,
                    TiReq(co, "changed", TiLine("changed", 5m, 1000m)), default);
            });
            await Task.Delay(1000);
            edit.IsCompleted.Should().BeFalse("the edit must wait on the row lock held by the 'post'");

            await ExecAsync(x, $"UPDATE sales.tax_invoices SET status='POSTED', doc_no='T9TI-' || tax_invoice_id, posted_at=now() WHERE tax_invoice_id={tiId}");
            await ExecAsync(x, "COMMIT");

            var act = async () => await edit;
            (await act.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("ti.cannot_edit_after_post");
        }
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var lines = await db.TaxInvoiceLines.AsNoTracking().Where(l => l.TaxInvoiceId == tiId).ToListAsync();
            lines.Should().ContainSingle().Which.Quantity.Should().Be(1m, "the posted TI's lines were not rewritten");
        }

        var ti2 = await CreateTiAsync(co, TiReq(co, "t9b", TiLine("orig", 1m, 1000m)));
        await using var staleSp = Sp(co); await using var staleScope = staleSp.CreateAsyncScope();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var stale = await staleDb.TaxInvoices.Include(t => t.Lines).FirstAsync(t => t.TaxInvoiceId == ti2);

        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>().UpdateDraftAsync(ti2,
                TiReq(co, "edited-wins", TiLine("new", 2m, 1000m)), default);

        stale.MarkPosted("T9TIB-" + ti2, 1, DateTimeOffset.UtcNow);
        var post = () => staleDb.SaveChangesAsync();
        await post.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "the edit bumped Version, so the stale post's UPDATE ... WHERE version = old misses");
    }

    // ── T10 — BN guard + exit ────────────────────────────────────────────────

    [SkippableFact]
    public async Task T10_Draft_TI_linked_from_a_billing_note_is_frozen_until_the_note_is_deleted_or_cancelled()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await VatCo();
        var tiId = await CreateTiAsync(co, TiReq(co, "t10", TiLine("orig")));

        async Task<long> NewBn()
        {
            await using var sp = Sp(co); await using var s = sp.CreateAsyncScope();
            return await s.ServiceProvider.GetRequiredService<IBillingNoteService>().CreateDraftAsync(
                new CreateBillingNoteRequest(Today(), Today().AddDays(30), co.CustomerId, null, null, [tiId],
                    "THB", 1m, null, null, []), default);
        }

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var token = Token(co);
        var edit = TiReq(co, "t10-edit", TiLine("orig", 2m));

        // Draft BN -> frozen; delete the BN -> editable.
        var bn1 = await NewBn();
        var (s1, b1) = await PutAsync(http, $"/tax-invoices/{tiId}", token, edit);
        s1.Should().Be(422, b1); b1.Should().Contain("ti.linked_to_billing_note");
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBillingNoteService>().DeleteDraftAsync(bn1, default);
        (await PutAsync(http, $"/tax-invoices/{tiId}", token, edit)).Status.Should().Be(204);

        // Issued BN (reachable: issue does not require posted TIs) -> frozen; cancel the BN -> editable.
        var bn2 = await NewBn();
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBillingNoteService>().IssueAsync(bn2, default);
        var (s2, b2) = await PutAsync(http, $"/tax-invoices/{tiId}", token, edit);
        s2.Should().Be(422, b2); b2.Should().Contain("ti.linked_to_billing_note");
        await using (var sp = Sp(co)) await using (var s = sp.CreateAsyncScope())
            await s.ServiceProvider.GetRequiredService<IBillingNoteService>().CancelAsync(bn2, "t10", default);
        (await PutAsync(http, $"/tax-invoices/{tiId}", token, edit)).Status.Should().Be(204);
    }
}
