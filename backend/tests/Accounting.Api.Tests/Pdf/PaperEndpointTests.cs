using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Application.Purchase;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Pdf;

/// <summary>
/// cont.121 — canonical paper-DTO unification (spec 2026-07-02). GET /{doc}/{id}/paper
/// returns the SAME PaperDocModel composition the QuestPDF renderer consumes, so the FE
/// PaperDocument screen == print by construction. Covers the three riskiest mappers:
/// Receipt (DisplayNotes / Wht / ShowVat-from-paid-VAT), Tax Invoice (posted-snapshot
/// seller — ม.86/4 immutability), Purchase Order (discount reconstruction + partyLabel
/// ผู้ขาย + ต้นฉบับ/สำเนา watermark as a camelCase enum string). Plus 401 unauthenticated
/// and 404 nonexistent-id. Real HTTP pipeline (RbacApiFactory) over the shared teas_test;
/// unique seeds via TestIds.* (CLAUDE.md §8).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PaperEndpointTests
{
    private readonly PostgresFixture _fx;
    public PaperEndpointTests(PostgresFixture fx) => _fx = fx;

    // ── hand-built provider (fixture writes go through the real services) ─────────
    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _fx.ConnectionString,
            }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // ── HTTP helpers (mirror RbacCartesianTests: real JWT over the full pipeline) ──
    private static string Token() => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "paper-endpoint-tests", CompanyId: 1, BranchId: 1,
            IsSuperAdmin: true, Roles: ["admin"], Permissions: [])).Token;

    private static async Task<JsonDocument> GetPaperAsync(HttpClient client, string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {path} must return the paper DTO");
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    // ── seed helpers (same shapes as Sprint86ArWhtTests / PurchasePdfTests) ────────
    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    private static async Task<int> SvcWhtTypeId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.WhtTypes.Where(w => w.Code == "SVC" && w.EffectiveTo == null)
            .Select(w => w.WhtTypeId).FirstAsync();
    }

    private static async Task<long> NewVendor(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var v = new Vendor
        {
            CompanyId = 1, VendorCode = TestIds.VendorCode(), NameTh = "ผู้ขายทดสอบ Paper",
            TaxId = TestIds.TaxId(), BranchCode = "00000",
            VendorType = CustomerType.Corporate, IsForeign = false, VatRegistered = true,
        };
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        return v.VendorId;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    // doc_date is pinned server-side (§10) — the request date is informational only.
    private static async Task<long> PostTi(ServiceProvider sp, long cust)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today, cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการทดสอบ paper", 1m, 1, "งาน", 10000m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(id, default);
        return id;
    }

    // ═══════════════════════ Receipt — DisplayNotes / Wht / ShowVat ═══════════════

    [SkippableFact]
    public async Task Receipt_paper_carries_display_notes_wht_and_show_vat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var wt = await SvcWhtTypeId(sp);
        var ti = await PostTi(sp, cust);

        long rcId;
        ReceiptDetail detail;
        await using (var s = sp.CreateAsyncScope())
        {
            var rsvc = s.ServiceProvider.GetRequiredService<IReceiptService>();
            // 10,700 applied; customer withholds 300 (3% of the 10,000 service base).
            rcId = await rsvc.CreateDraftAsync(new CreateReceiptRequest(
                Today, cust, PaymentMethod.Transfer, null, null, null,
                "THB", 1m, null, [new ReceiptApplicationInput(ti, 10700m)], null,
                300m, wt, $"WHT-{TestIds.Suffix()[..6]}", Today), default);
            await rsvc.PostAsync(rcId, default);
            detail = (await rsvc.GetDetailAsync(rcId, default))!;
        }

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        using var json = await GetPaperAsync(client, $"/receipts/{rcId}/paper");
        var root = json.RootElement;

        root.GetProperty("docNo").GetString().Should().Be(detail.DocNo);
        root.GetProperty("docType").GetString().Should().Be("ใบเสร็จรับเงิน");

        var summary = root.GetProperty("summary");
        summary.GetProperty("wht").GetDecimal().Should().Be(300m);
        summary.GetProperty("showVat").GetBoolean().Should().BeTrue("the paid amount carries VAT");
        summary.GetProperty("vat").GetDecimal().Should().Be(700m);
        summary.GetProperty("total").GetDecimal().Should().Be(10400m, "Total = net after WHT (PaperFootPlan semantics)");

        // DisplayNotes is composed ONCE in the read DTO (cont.119) — the paper DTO must carry it.
        root.GetProperty("notes").GetString().Should().Be(detail.DisplayNotes).And.NotBeNullOrWhiteSpace();

        // [JsonIgnore] — the seller logo bytes must never reach the JSON payload.
        root.GetProperty("seller").TryGetProperty("logo", out _).Should().BeFalse();
        root.GetProperty("seller").TryGetProperty("logoSvg", out _).Should().BeFalse();
    }

    // ═══════════════════════ Tax Invoice — posted snapshot (ม.86/4) ════════════════

    [SkippableFact]
    public async Task Tax_invoice_paper_uses_posted_snapshot_seller_and_doc_no()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var ti = await PostTi(sp, cust);

        TaxInvoiceDetail detail;
        await using (var s = sp.CreateAsyncScope())
            detail = (await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                .GetDetailAsync(ti, default))!;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        using var json = await GetPaperAsync(client, $"/tax-invoices/{ti}/paper");
        var root = json.RootElement;

        root.GetProperty("docNo").GetString().Should().Be(detail.DocNo).And.NotBeNullOrWhiteSpace();
        root.GetProperty("docType").GetString().Should().Be("ใบกำกับภาษี", "company 1 is VAT-registered (ม.86/4 #1)");

        // ม.86/4 #2 — seller block comes from the POSTED SNAPSHOT, not live master.
        var seller = root.GetProperty("seller");
        seller.GetProperty("name").GetString().Should().Be(detail.SupplierName);
        seller.GetProperty("branchCode").GetString().Should().Be(detail.SupplierBranchCode);
        seller.GetProperty("taxId").GetString()!.Replace("-", "").Should()
            .Be(detail.SupplierTaxId, "snapshot TaxID, display-formatted only");

        var summary = root.GetProperty("summary");
        summary.GetProperty("showVat").GetBoolean().Should().BeTrue();
        summary.GetProperty("vat").GetDecimal().Should().Be(700m, "ม.86/4 #6 — VAT shown separately");
        summary.GetProperty("total").GetDecimal().Should().Be(10700m);
    }

    // ═══════════════════════ T10 (I3, I10) — signatures carry no image bytes ══════════
    // doc-signature-and-foot-layout spec §9 T10. A posted document has a signer (I2), so the
    // /paper JSON's "signatures" object is present; its byte fields must never reach the wire
    // (mirrors :168-170's seller.logo/logoSvg JsonIgnore pattern) and the pre-existing summary/
    // notes values this file already pins (ม.86/4, I10) must stay unchanged by the addition.

    [SkippableFact]
    public async Task Posted_TI_paper_carries_a_signatures_object_without_leaking_image_bytes()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        var ti = await PostTi(sp, cust);

        TaxInvoiceDetail detail;
        await using (var s = sp.CreateAsyncScope())
            detail = (await s.ServiceProvider.GetRequiredService<ITaxInvoiceService>()
                .GetDetailAsync(ti, default))!;

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        using var json = await GetPaperAsync(client, $"/tax-invoices/{ti}/paper");
        var root = json.RootElement;

        // I10 — this dispatch adds signatures; it must not move a money figure or the notes text.
        var summary = root.GetProperty("summary");
        summary.GetProperty("subtotal").GetDecimal().Should().Be(10000m);
        summary.GetProperty("vat").GetDecimal().Should().Be(700m);
        summary.GetProperty("total").GetDecimal().Should().Be(10700m);

        // I3 — a posted document resolves a signer (PostedBy = the default provider's user),
        // so "signatures" is present, not absent/null.
        root.TryGetProperty("signatures", out var signatures).Should().BeTrue(
            "a posted Tax Invoice has a signer (I2) — the field must be present");
        signatures.ValueKind.Should().NotBe(JsonValueKind.Null);

        // [JsonIgnore] — the raw image bytes must never reach the /paper JSON, exactly like
        // seller.logo/logoSvg above.
        signatures.TryGetProperty("leftBytes", out _).Should().BeFalse();
        signatures.TryGetProperty("middleBytes", out _).Should().BeFalse();
        signatures.TryGetProperty("stampBytes", out _).Should().BeFalse();
    }

    // ═══════════════ Purchase Order — discount row + partyLabel + watermark ════════

    [SkippableFact]
    public async Task Purchase_order_paper_reconstructs_discount_and_vendor_party_label()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = await NewVendor(sp);

        long poId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPurchaseOrderService>();
            // gross = 10×100 + 3×250 = 1,750; line 2 has 5% discount → stored subtotal 1,712.50.
            poId = await svc.CreateDraftAsync(new CreatePurchaseOrderRequest(
                Today, Today.AddDays(30), vid, null, "THB", 1m, "หมายเหตุใบสั่งซื้อ paper", null,
                [
                    new PurchaseOrderLineInput(null, "สินค้า A", 10m, "ชิ้น", 100m, 0m, 1, "VAT7", 0.07m, null),
                    new PurchaseOrderLineInput(null, "สินค้า B", 3m, "กล่อง", 250m, 5m, 2, "VAT7", 0.07m, null),
                ]), default);
        }

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        using var json = await GetPaperAsync(client, $"/purchase-orders/{poId}/paper");
        var root = json.RootElement;

        // cont.120 Option A — FE BP-04 reconstruction: Subtotal=gross, Discount row, BeforeVat=stored.
        var summary = root.GetProperty("summary");
        summary.GetProperty("subtotal").GetDecimal().Should().Be(1750m);
        summary.GetProperty("discount").GetDecimal().Should().Be(37.50m);
        summary.GetProperty("beforeVat").GetDecimal().Should().Be(1712.50m);

        var party = root.GetProperty("partyLabel");
        party.GetProperty("th").GetString().Should().Be("ผู้ขาย");
        party.GetProperty("en").GetString().Should().Be("Vendor");

        // Watermark: default = ต้นฉบับ/success; enum serializes as a camelCase STRING.
        var wm = root.GetProperty("watermark");
        wm.GetProperty("text").GetString().Should().Be("ต้นฉบับ");
        wm.GetProperty("variant").GetString().Should().Be("success");

        using var copyJson = await GetPaperAsync(client, $"/purchase-orders/{poId}/paper?copy=true");
        var copyWm = copyJson.RootElement.GetProperty("watermark");
        copyWm.GetProperty("text").GetString().Should().Be("สำเนา");
        copyWm.GetProperty("variant").GetString().Should().Be("warning");
    }

    // ═══════════════════════ 401 unauthenticated / 404 nonexistent ═════════════════

    [SkippableFact]
    public async Task Paper_endpoints_return_401_without_auth_and_404_for_unknown_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        foreach (var path in new[]
                 { "/receipts/1/paper", "/tax-invoices/1/paper", "/purchase-orders/1/paper" })
        {
            using var anon = await client.GetAsync(path);
            anon.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"GET {path} without a token");
        }

        foreach (var path in new[]
                 {
                     "/receipts/999999999/paper", "/tax-invoices/999999999/paper",
                     "/purchase-orders/999999999/paper",
                 })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());
            using var resp = await client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.NotFound, $"GET {path} — *.not_found → 404");
        }
    }
}
