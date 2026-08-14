using System.Net.Http.Headers;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Identity;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// R3/H3 — every create-from/convert route in the sales chain authorized on the SOURCE
/// document's permission only, letting a source-manage holder mint a TARGET document (a tax
/// invoice / billing note / sales order) they hold no create permission for. Verified live: a
/// user holding only delivery-order manage minted a tax invoice while POST /tax-invoices
/// correctly 403'd them. Fix requires the target's create permission IN ADDITION TO the source
/// (AND, not OR). Mirrors AttachmentDownloadDeleteGuardTests' HTTP-level RBAC harness — real
/// companies/documents, tokens carrying an explicit permission set.
///
/// Four of the five routes have a STATICALLY known target (stacked perm: policy —
/// PermissionHandler reads JWT claims only, so any real user + an explicit claim list proves
/// the gate regardless of that user's actual DB role). The fifth (SO create-invoice) picks its
/// target by company VAT mode at runtime, so it is gated by a DYNAMIC IPermissionLookup check
/// (mirrors AttachmentEndpoints.ParentGuard) — that one needs a REAL seeded role (SALES_STAFF),
/// since the dynamic check reads the caller's actual DB grants, not the JWT.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SalesChainConversionAuthorizationTests
{
    private readonly PostgresFixture _fx;
    public SalesChainConversionAuthorizationTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    // SERVICE type — the SO→Invoice skip-DO paths (both VAT and non-VAT) reject any GOOD/
    // EXEMPT_GOOD line with so.delivery_required; SERVICE avoids that unrelated guard everywhere.
    private static ChainLineInput SoLine(decimal qty, decimal price) =>
        new(null, "line", qty, "ชิ้น", price, 0m, 1, "VAT7", 0.07m, "SERVICE");
    private static DeliveryLineInput DoLine(decimal qty, decimal price) =>
        new(null, null, "line", qty, "ชิ้น", price, 0m, 1, "VAT7", 0.07m, "SERVICE");

    // ── document fixtures (test setup — never the caller under test) ──────────────

    private async Task<long> CreateAcceptedQuotationAsync(int companyId, long customerId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId: 1);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IQuotationService>();
        var id = await svc.CreateDraftAsync(new CreateQuotationRequest(
            Today, Today.AddDays(30), customerId, null, "THB", 1m, null, null, [SoLine(1m, 1000m)]), default);
        await svc.SendAsync(id, default);
        await svc.AcceptAsync(id, default);
        return id;
    }

    private async Task<long> CreatePostedSalesOrderAsync(int companyId, long customerId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId: 1);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
        var id = await svc.CreateDraftAsync(new CreateSalesOrderRequest(
            Today, null, customerId, null, "THB", 1m, null, null, [SoLine(1m, 1000m)]), default);
        await svc.PostAsync(id, default);
        return id;
    }

    private async Task<long> CreateDraftBillingNoteAsync(int companyId, long customerId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId: 1);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IBillingNoteService>();
        return await svc.CreateDraftAsync(new CreateBillingNoteRequest(
            Today, Today, customerId, null, null, null, "THB", 1m, null, null,
            [new BillingLineInput(null, null, "line", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]), default);
    }

    private async Task<long> CreateDraftDeliveryOrderAsync(int companyId, long customerId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId: 1);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
        return await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            Today, customerId, null, false, null, null, [DoLine(1m, 1000m)]), default);
    }

    private async Task<long> CreateDeliveredDeliveryOrderAsync(int companyId, long customerId)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId: 1);
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
        var id = await svc.CreateDraftAsync(new CreateDeliveryOrderRequest(
            Today, customerId, null, false, null, null, [DoLine(1m, 1000m)]), default);
        await svc.IssueAsync(id, default);
        await svc.MarkDeliveredAsync(id, default);
        return id;
    }

    // ── RBAC fixtures (mirrors AttachmentDownloadDeleteGuardTests) ─────────────────

    private async Task<int> RoleIdAsync(int companyId, string roleCode)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Roles.Where(r => r.CompanyId == companyId && r.RoleCode == roleCode)
            .Select(r => r.RoleId).FirstAsync();
    }

    /// <summary>A real sys.users row (+ optional real UserRole grants for the DYNAMIC-gate
    /// route) — the shape IPermissionLookup.LoadAsync needs. For the 4 STATIC routes below,
    /// the caller is created with NO roles: PermissionHandler is JWT-claims-only, so the DB
    /// role is irrelevant there and the token below carries the exact claims under test.</summary>
    private async Task<long> CreateUserAsync(int companyId, params int[] roleIds)
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

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var rid in roleIds)
            db.UserRoles.Add(new UserRole
            {
                UserId = user.UserId, RoleId = rid, CompanyId = companyId,
                BranchId = 0, ValidFrom = today, ValidTo = null,
            });
        if (roleIds.Length > 0) await db.SaveChangesAsync();
        return user.UserId;
    }

    private static string Token(long userId, int companyId, IEnumerable<string> perms) =>
        new JwtTokenIssuer(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer, Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey, AccessTokenMinutes = 60,
        })).Issue(new TokenClaims(
            UserId: userId, Username: $"scca-{userId}", CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: false, Roles: [], Permissions: perms.ToList())).Token;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string token, string path)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(req);
    }

    // ═══════════════════ Quotation → Sales Order (convert-to-so) ═══════════════════

    [SkippableFact]
    public async Task ConvertToSo_denies_a_caller_holding_only_quotation_manage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        // Real, correctly-staged (Accepted) quotation — pre-fix this call SUCCEEDS (200) and
        // actually mints a Sales Order; a fake id would only prove "not 404", not "not exploitable".
        var qId = await CreateAcceptedQuotationAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.quotation.manage"]);

        using var resp = await PostAsync(client, token, $"/quotations/{qId}/convert-to-so");
        ((int)resp.StatusCode).Should().Be(403,
            "sales.quotation.manage lets the caller READ/manage the source, but converting also "
            + "MINTS a Sales Order — sales.sales_order.manage is required too (AND, not OR)");
    }

    [SkippableFact]
    public async Task ConvertToSo_succeeds_for_a_caller_holding_both_source_and_target_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var qId = await CreateAcceptedQuotationAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.quotation.manage", "sales.sales_order.manage"]);

        using var resp = await PostAsync(client, token, $"/quotations/{qId}/convert-to-so");
        ((int)resp.StatusCode).Should().Be(200, "holding BOTH permissions must still convert successfully");
    }

    // ═══════════════════ Billing Note → Tax Invoice (create-tax-invoice) ═══════════

    [SkippableFact]
    public async Task BnCreateTaxInvoice_denies_a_caller_holding_only_billing_note_manage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        // Real Draft billing note — pre-fix this SUCCEEDS (200) and mints a real Tax Invoice.
        var bnId = await CreateDraftBillingNoteAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.billing_note.manage"]);

        using var resp = await PostAsync(client, token, $"/billing-notes/{bnId}/create-tax-invoice");
        ((int)resp.StatusCode).Should().Be(403,
            "sales.billing_note.manage does not authorize MINTING a Tax Invoice — "
            + "sales.tax_invoice.create is required too (this is the exact live H3 finding)");
    }

    [SkippableFact]
    public async Task BnCreateTaxInvoice_succeeds_for_a_caller_holding_both_source_and_target_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var bnId = await CreateDraftBillingNoteAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.billing_note.manage", "sales.tax_invoice.create"]);

        using var resp = await PostAsync(client, token, $"/billing-notes/{bnId}/create-tax-invoice");
        ((int)resp.StatusCode).Should().Be(200, "holding BOTH permissions must still convert successfully");
    }

    // ═══════════════════ Delivery Order → Tax Invoice (create-ti) ══════════════════

    [SkippableFact]
    public async Task DoCreateTi_denies_a_caller_holding_only_delivery_order_manage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        // Real Delivered DO — pre-fix this SUCCEEDS (200) and mints a real Tax Invoice.
        var doId = await CreateDeliveredDeliveryOrderAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.delivery_order.manage"]);

        using var resp = await PostAsync(client, token, $"/delivery-orders/{doId}/create-ti");
        ((int)resp.StatusCode).Should().Be(403,
            "sales.delivery_order.manage does not authorize MINTING a Tax Invoice — this is the "
            + "exact live H3 finding: 'a user with only delivery-order manage minted a tax invoice'");
    }

    [SkippableFact]
    public async Task DoCreateTi_succeeds_for_a_caller_holding_both_source_and_target_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var doId = await CreateDeliveredDeliveryOrderAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.delivery_order.manage", "sales.tax_invoice.create"]);

        using var resp = await PostAsync(client, token, $"/delivery-orders/{doId}/create-ti");
        ((int)resp.StatusCode).Should().Be(200, "holding BOTH permissions must still convert successfully");
    }

    // ═══════════════════ Delivery Order → Invoice/Billing Note (create-invoice) ════

    [SkippableFact]
    public async Task DoCreateInvoice_denies_a_caller_holding_only_delivery_order_manage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        // Real Draft DO — pre-fix this SUCCEEDS (200) and mints a real Billing Note.
        var doId = await CreateDraftDeliveryOrderAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.delivery_order.manage"]);

        using var resp = await PostAsync(client, token, $"/delivery-orders/{doId}/create-invoice");
        ((int)resp.StatusCode).Should().Be(403,
            "sales.delivery_order.manage does not authorize MINTING a Billing Note — "
            + "sales.billing_note.manage is required too (AND, not OR)");
    }

    [SkippableFact]
    public async Task DoCreateInvoice_succeeds_for_a_caller_holding_both_source_and_target_permission()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var doId = await CreateDraftDeliveryOrderAsync(co.CompanyId, co.CustomerId);
        var userId = await CreateUserAsync(co.CompanyId);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.delivery_order.manage", "sales.billing_note.manage"]);

        using var resp = await PostAsync(client, token, $"/delivery-orders/{doId}/create-invoice");
        ((int)resp.StatusCode).Should().Be(200, "holding BOTH permissions must still convert successfully");
    }

    // ═══════════════════ Sales Order → Invoice (polymorphic, dynamic gate) ═════════

    /// <summary>The one dynamic-target route. SALES_STAFF (real seeded role) holds
    /// sales.sales_order.manage (source, static) + sales.billing_note.manage, but NOT
    /// sales.tax_invoice.create (270/321/530 seed — the exact real-world gap this fix
    /// closes; SALES_STAFF's own role description is "Quotation, Sales Order"). On a
    /// VAT-registered company the target flips to TaxInvoiceCreate → 403.</summary>
    [SkippableFact]
    public async Task SoCreateInvoice_denies_sales_staff_on_a_vat_company_lacking_tax_invoice_create()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var soId = await CreatePostedSalesOrderAsync(co.CompanyId, co.CustomerId);
        var salesStaffRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.SalesStaff);
        var userId = await CreateUserAsync(co.CompanyId, salesStaffRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        // Static gate only needs the source claim — the dynamic target check reads the REAL
        // DB role (SALES_STAFF), not this list.
        var token = Token(userId, co.CompanyId, ["sales.sales_order.manage"]);

        using var resp = await PostAsync(client, token, $"/sales-orders/{soId}/create-invoice");
        ((int)resp.StatusCode).Should().Be(403,
            "VAT company → target is a Tax Invoice; SALES_STAFF holds no sales.tax_invoice.create");
    }

    /// <summary>Matched pair with the VAT-company case above, same SALES_STAFF role — on a
    /// NON-VAT company the target flips to BillingNoteManage, which SALES_STAFF DOES hold
    /// (321_seed_billing_note_perms.sql). Proves the gate follows the ACTUAL target document
    /// (VAT mode), not a blanket require-both.</summary>
    [SkippableFact]
    public async Task SoCreateInvoice_succeeds_for_sales_staff_on_a_nonvat_company_holding_billing_note_manage()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        var soId = await CreatePostedSalesOrderAsync(co.CompanyId, co.CustomerId);
        var salesStaffRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.SalesStaff);
        var userId = await CreateUserAsync(co.CompanyId, salesStaffRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.sales_order.manage"]);

        using var resp = await PostAsync(client, token, $"/sales-orders/{soId}/create-invoice");
        ((int)resp.StatusCode).Should().Be(200,
            "non-VAT company → target is a Billing Note; SALES_STAFF holds sales.billing_note.manage");
    }

    /// <summary>No dead end on the VAT side either — ACCOUNTANT (real seeded role) holds
    /// sales.tax_invoice.create (530_seed_rbac_grant_reconcile.sql), so the fix does not
    /// strand the normal accounting-tier path.</summary>
    [SkippableFact]
    public async Task SoCreateInvoice_succeeds_for_accountant_on_a_vat_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var soId = await CreatePostedSalesOrderAsync(co.CompanyId, co.CustomerId);
        var accountantRole = await RoleIdAsync(co.CompanyId, Role.SystemRoles.Accountant);
        var userId = await CreateUserAsync(co.CompanyId, accountantRole);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(userId, co.CompanyId, ["sales.sales_order.manage"]);

        using var resp = await PostAsync(client, token, $"/sales-orders/{soId}/create-invoice");
        ((int)resp.StatusCode).Should().Be(200,
            "ACCOUNTANT holds sales.tax_invoice.create — the fix must not break the normal path");
    }
}
