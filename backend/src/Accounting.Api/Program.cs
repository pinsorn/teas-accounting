using System.Text;
using System.Threading.RateLimiting;
using Accounting.Api;
using Accounting.Api.Authorization;
using Accounting.Api.BackgroundServices;
using Accounting.Api.Endpoints;
using Accounting.Api.Middleware;
using Accounting.Api.OAuth;
using Accounting.Api.Tenancy;
using Accounting.Application;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// First-run instance secrets (MFA AES key, JWT access-token lifetime). Loaded AFTER the
// env-specific appsettings so it OVERRIDES them, and git-ignored so the real key is never
// committed. The onboarding setup endpoint (POST /system/setup/instance-keys) writes this
// file at ContentRootPath; reloadOnChange:true makes the new values take effect live via
// IOptionsMonitor (no restart). See OtpNetTotpService / JwtTokenIssuer + InstanceSetupEndpoints.
builder.Configuration.AddJsonFile(
    InstanceSecrets.FileName, optional: true, reloadOnChange: true);

// QuestPDF Community licence — required before any PDF is generated (TI /pdf endpoint).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Sprint 13j-PDF — register the bundled Thai font (Sarabun, SIL OFL) so QuestPDF
// renders Thai glyphs on ANY host. The server has no system Thai font and SkiaSharp
// can't fall back to one; both weights register under family "Sarabun" (use via
// DefaultTextStyle(FontFamily("Sarabun")) in the PaperDocument renderer).
var fontDir = Path.Combine(AppContext.BaseDirectory, "Fonts");
if (Directory.Exists(fontDir))
    foreach (var ttf in Directory.EnumerateFiles(fontDir, "*.ttf"))
        using (var fs = File.OpenRead(ttf))
            QuestPDF.Drawing.FontManager.RegisterFont(fs);

// Accept/emit enums as strings (DTOs use enums e.g. PaymentMethod, TaxAdjustmentNoteType;
// the frontend sends names like "Transfer"/"Credit"). Default is int → 400 otherwise.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

// Tax/VAT environment configuration — DO NOT expose via UI (see CLAUDE.md §4.6)
builder.Services.Configure<TaxConfig>(builder.Configuration.GetSection("Tax"));

// M2 (MCP) — App:BaseUrl backs the human-approval deep-link a create-draft MCP tool
// returns ({BaseUrl}/<route>/{id}?action=approve). See Accounting.Api.Mcp.AppOptions.
builder.Services.Configure<Accounting.Api.Mcp.AppOptions>(
    builder.Configuration.GetSection(Accounting.Api.Mcp.AppOptions.SectionName));

// Layer registrations
builder.Services.AddInfrastructure(builder.Configuration);

// Sprint 13c — in-process e-Tax retry worker (composition root owns hosting).
builder.Services.AddHostedService<ETaxRetryHostedService>();
builder.Services.AddHostedService<IdempotencyCleanupHostedService>();   // Sprint 14 P4
builder.Services.AddApplication();

// Multi-tenant context: per-request scope, populated from the JWT claim set.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();

// JWT bearer
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Configuration section 'Jwt' is required.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    })
    // Sprint 14 — external API key scheme (X-Api-Key). /api/v1/* uses this;
    // root/BFF routes stay JWT-only (auth isolation enforced per route group).
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddPermissionAuthorization();

// OAuth 2.1 Authorization Server (TEAS Connect MCP native-connector auth). Core (EF stores on the
// oauth schema) is registered in Infrastructure; here we add the server (authorize/token +
// RFC 8414/OIDC discovery, PKCE, refresh) and the token-VALIDATION scheme /mcp uses for Bearer.
// AddOpenIddict() is safe to call again — it augments the same registration.
// DCR (RFC 7591 /oauth/register) is deferred: clients fall back to the pre-registered `teas-mcp`
// application the seeder installs (spec §6/§6b — pre-registration is the sanctioned fallback).
builder.Services.AddOpenIddict()
    .AddServer(o =>
    {
        o.SetAuthorizationEndpointUris("oauth/authorize")
         .SetTokenEndpointUris("oauth/token")
         .SetConfigurationEndpointUris(".well-known/openid-configuration",
                                        ".well-known/oauth-authorization-server");
        o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
        o.RequireProofKeyForCodeExchange();                 // PKCE S256 mandatory
        o.RegisterScopes(McpScopes.All.ToArray());
        o.SetAccessTokenLifetime(TimeSpan.FromMinutes(10)); // 5–15 min (spec §6b)
        o.SetRefreshTokenLifetime(TimeSpan.FromHours(8));   // ~1 workday absolute
        o.UseReferenceRefreshTokens();                      // reference refresh → family revocation
        // ponytail: ephemeral DEV keys — Ham installs persistent X509 signing+encryption certs
        // (SetSigningCertificate/SetEncryptionCertificate) before prod deploy (P5 deploy gate).
        o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        o.UseAspNetCore()
         .EnableAuthorizationEndpointPassthrough()          // /oauth/authorize handled by our endpoint (T2)
         .EnableTokenEndpointPassthrough()
         // TLS is terminated upstream (Cloudflare → Next passthrough → this backend over HTTP), so
         // OpenIddict must not reject the plain-HTTP hop. Same posture as the rest of the API.
         .DisableTransportSecurityRequirement();
    })
    .AddValidation(o =>
    {
        o.UseLocalServer();                                 // validate access tokens in-process
        o.UseAspNetCore();
    });
// Seeds OAuth scopes + the pre-registered `teas-mcp` client (server-fixed permissions).
builder.Services.AddHostedService<OpenIddictSeeder>();

// M2 (MCP) — in-process Model Context Protocol server. Stateless HTTP transport
// means each tool resolves its scoped services from the per-request
// HttpContext.RequestServices scope already populated by the X-Api-Key auth handler
// (company_id claim → ITenantContext → RLS), so tenant isolation is automatic and
// tools need no manual company filter. AddAuthorizationFilters() enables the
// [Authorize(Policy = "apiperm:<scope>")] gating on each tool, resolved by the same
// PermissionPolicyProvider the /api/v1 endpoints use. The /mcp endpoint itself is
// pinned to the X-Api-Key scheme + per-key rate-limit at MapMcp (below).
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .AddAuthorizationFilters()
    .WithTools<Accounting.Api.Mcp.TeasMcpTools>();

// Sprint 14 — /api/v1/* is ApiKey-scheme-only (auth isolation: root/BFF stays
// JWT-default, so an X-Api-Key on a root route → 401, and a JWT on v1 → 401).
// ponytail: global FallbackPolicy — any route with no auth metadata requires an authenticated
// user by default. Intentionally-public routes must carry explicit AllowAnonymous (see below:
// /health, /auth/login, /system/setup/bootstrap-admin). This is defense-in-depth: a future
// endpoint that forgets RequireAuthorization is denied, not silently world-readable.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build())
    .AddPolicy(ApiV1Endpoints.ApiKeyOnlyPolicy, p => p
        .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser());

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
// NIT-08: use full type names as schema ids so distinct nested types that share a
// short name (e.g. PurchaseOrderEndpoints+ReasonBody vs SalesChainEndpoints+ReasonBody)
// don't collide and 500 the whole /swagger/v1/swagger.json document.
builder.Services.AddSwaggerGen(c => c.CustomSchemaIds(t => t.FullName?.Replace("+", ".")));
builder.Services.AddOpenApi();

// CORS for frontend — origin-constrained + explicit methods/headers.
// ponytail: AllowAnyHeader/AllowAnyMethod removed; explicit list covers all BFF + API calls.
// Frontend:Origin must be set per-environment in production (no localhost fallback in prod).
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
     .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
     .WithHeaders("Content-Type", "Authorization", "X-Api-Key", "X-Idempotency-Key", "Accept")
     .AllowCredentials()));

builder.Services.AddHealthChecks();

// ponytail: fixed-window rate-limit on /auth/login only — no new packages (native ASP.NET Core).
// 10 attempts per IP per minute is generous for human users but stops credential-stuffing bursts.
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit         = 10;
        opt.Window              = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit          = 0;
    });

    // M1 (MCP) — per-key rate-limit on the external /api/v1/* surface. The
    // limiter runs BEFORE authentication (UseRateLimiter precedes
    // UseAuthentication), so there is no principal yet → partition by the
    // X-Api-Key header (its stable lookup prefix, never the full secret).
    // Each key gets its own 120/min fixed window; unkeyed requests share one
    // bucket (they 401 at auth anyway). No new package (native limiter).
    o.AddPolicy(Accounting.Api.Endpoints.ApiV1Endpoints.PerApiKeyRateLimitPolicy, ctx =>
    {
        var presented = ctx.Request.Headers[
            Accounting.Api.Authorization.ApiKeyAuthenticationHandler.HeaderName].ToString();
        var partitionKey = Accounting.Infrastructure.Identity.ApiKeyGenerator.PrefixOf(presented)
            ?? "__no_api_key";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 120,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            });
    });

    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Bootstrap schema + triggers + RLS + seed (idempotent).
// Phase 1: this runs at startup. After the first EF migration is generated,
// replace with `await db.Database.MigrateAsync()` and drop DbInitializer.
if (builder.Configuration.GetValue("Database:RunInitializerOnStartup", true))
{
    await DbInitializer.InitializeAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("frontend");

// ponytail: security response headers (no new package — ASP.NET Core Use/Run).
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]        = "DENY";
    ctx.Response.Headers["Referrer-Policy"]        = "no-referrer";
    await next();
});

app.UseRateLimiter();

app.UseDomainExceptionMapper();
app.UseValidationErrorEnvelope();   // Sprint 13d P5 — ModelState 400 → unified v1 envelope
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantContext();
app.UseExternalApiIdempotency();   // Sprint 14 P4 — /api/v1/* mutations only

app.MapHealthChecks("/health").AllowAnonymous(); // ponytail: explicit AllowAnonymous — required now that FallbackPolicy requires auth by default
// Per-company-vat-mode spec (2026-06-11): VAT mode/rate/ภ.พ.30 mode come from the
// caller's company row, so the endpoint needs a tenant → authenticated.
app.MapGet("/system/info", async (ICompanyTaxConfigService taxCfg, CancellationToken ct) =>
{
    var tax = await taxCfg.GetAsync(ct);
    return new
    {
        version = AppBuildInfo.Version,
        vat_mode = tax.VatMode,
        vat_rate = tax.VatRate,
        pnd30_submission_mode = tax.Pnd30SubmissionMode,
        document_number_format = "MM-YYYY-PREFIX-NNNN",
        timezone = "Asia/Bangkok",
    };
}).RequireAuthorization();

// Sprint 8.5 — VAT-registration threshold (ม.85/1). Authenticated (needs tenant
// context for the TI query); no specific permission — any signed-in user.
app.MapGet("/system/vat-threshold-status",
    async (IVatThresholdService svc, CancellationToken ct) =>
        new { status = (await svc.CheckAsync(ct)).ToString() })
    .RequireAuthorization();

app.MapBootstrapAdminEndpoints(); // first-run super-admin (anonymous, gated on zero users — fresh-install only)
app.MapInstanceSetupEndpoints();   // first-run MFA key + JWT lifetime (super-admin, writes Secrets file)
app.MapAuthEndpoints();
app.MapCustomerEndpoints();
app.MapMasterEndpoints();
app.MapBusinessUnitEndpoints();
app.MapRbacAdminEndpoints();
app.MapEmployeeEndpoints();
app.MapPayrollEndpoints();
app.MapCompanyProfileEndpoints();
app.MapMeEndpoints();
app.MapProductEndpoints();
app.MapWhtTypeEndpoints();
app.MapJournalEndpoints();
app.MapTaxInvoiceEndpoints();
app.MapPaymentVoucherEndpoints();
app.MapVendorInvoiceEndpoints();
app.MapWhtCertificateEndpoints();
app.MapReceiptEndpoints();
app.MapTaxAdjustmentNoteEndpoints();
app.MapReportEndpoints();
app.MapTaxFilingEndpoints();
app.MapCitEndpoints();
app.MapSalesChainEndpoints();
app.MapBillingNoteEndpoints();
app.MapDocumentCrossRefEndpoints();
app.MapActivityEndpoints();
app.MapPrintEndpoints();
app.MapAttachmentEndpoints();
app.MapPurchaseOrderEndpoints();
app.MapPeriodEndpoints();
app.MapEtaxEndpoints();
app.MapApiKeyEndpoints();
app.MapExternalApiV1();

// M2 (MCP) — mount the in-process MCP server at /mcp. Same auth posture as /api/v1:
//   • ApiKeyOnlyPolicy → X-Api-Key scheme required (no anonymous; a JWT can't satisfy
//     it, an X-Api-Key principal is required). Per-tool [Authorize] then checks scopes.
//   • PerApiKeyRateLimitPolicy → the M1 per-key 120/min window (partitions on the
//     X-Api-Key header pre-auth, identical to /api/v1).
// mcp-kind keys carry read + *.create scopes only (M1 guard rejects *.post), and no
// post/issue tool is exposed → an agent can only draft; a human approves & posts.
// OAuth 2.1 — RFC 9728 protected-resource metadata (anonymous). RFC 8414 / OIDC discovery,
// /oauth/token and (T2) /oauth/authorize are served by the OpenIddict middleware.
app.MapOAuthMetadata();

app.MapMcp("/mcp")
    .RequireAuthorization(ApiV1Endpoints.ApiKeyOnlyPolicy)
    .RequireRateLimiting(ApiV1Endpoints.PerApiKeyRateLimitPolicy);

app.Run();

public partial class Program;  // For WebApplicationFactory in tests

// Per-company-vat-mode spec (2026-06-11): VatMode / VatRate / Pnd30SubmissionMode
// moved to the companies row (read via ICompanyTaxConfigService) and were removed here.
public sealed class TaxConfig
{
    public DateOnly VatEffectiveFrom { get; init; }
    public string VatRounding { get; init; } = "HALF_UP";
    public int VatDecimalPlaces { get; init; } = 2;

    // Sprint 8.5 — header label for non-VAT-registered companies.
    // ม.86: only VAT-registered may issue "ใบกำกับภาษี"; non-VAT must use a neutral term.
    public string NonVatDocLabelTh { get; init; } = "ใบส่งของ";
    public string NonVatDocLabelEn { get; init; } = "Delivery Order";
}
