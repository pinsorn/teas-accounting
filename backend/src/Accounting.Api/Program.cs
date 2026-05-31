using System.Text;
using Accounting.Api.Authorization;
using Accounting.Api.BackgroundServices;
using Accounting.Api.Endpoints;
using Accounting.Api.Middleware;
using Accounting.Api.Tenancy;
using Accounting.Application;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

// Sprint 14 — /api/v1/* is ApiKey-scheme-only (auth isolation: root/BFF stays
// JWT-default, so an X-Api-Key on a root route → 401, and a JWT on v1 → 401).
builder.Services.AddAuthorizationBuilder()
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

// CORS for frontend
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddHealthChecks();

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

app.UseDomainExceptionMapper();
app.UseValidationErrorEnvelope();   // Sprint 13d P5 — ModelState 400 → unified v1 envelope
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantContext();
app.UseExternalApiIdempotency();   // Sprint 14 P4 — /api/v1/* mutations only

app.MapHealthChecks("/health");
app.MapGet("/system/info", (IConfiguration cfg) => new
{
    version = typeof(Program).Assembly.GetName().Version?.ToString(),
    vat_mode = cfg.GetValue<bool>("Tax:VatMode"),
    vat_rate = cfg.GetValue<decimal>("Tax:VatRate"),
    pnd30_submission_mode = cfg.GetValue<string>("Tax:Pnd30SubmissionMode"),
    document_number_format = "MM-YYYY-PREFIX-NNNN",
    timezone = "Asia/Bangkok",
});

// Sprint 8.5 — VAT-registration threshold (ม.85/1). Authenticated (needs tenant
// context for the TI query); no specific permission — any signed-in user.
app.MapGet("/system/vat-threshold-status",
    async (IVatThresholdService svc, CancellationToken ct) =>
        new { status = (await svc.CheckAsync(ct)).ToString() })
    .RequireAuthorization();

app.MapAuthEndpoints();
app.MapCustomerEndpoints();
app.MapMasterEndpoints();
app.MapBusinessUnitEndpoints();
app.MapEmployeeEndpoints();
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

app.Run();

public partial class Program;  // For WebApplicationFactory in tests

public sealed class TaxConfig
{
    public bool VatMode { get; init; }
    public decimal VatRate { get; init; }
    public DateOnly VatEffectiveFrom { get; init; }
    public string VatRounding { get; init; } = "HALF_UP";
    public int VatDecimalPlaces { get; init; } = 2;
    public string Pnd30SubmissionMode { get; init; } = "manual";  // "auto" | "manual"

    // Sprint 8.5 — header label for non-VAT-registered companies (VatMode=false).
    // ม.86: only VAT-registered may issue "ใบกำกับภาษี"; non-VAT must use a neutral term.
    public string NonVatDocLabelTh { get; init; } = "ใบส่งของ";
    public string NonVatDocLabelEn { get; init; } = "Delivery Order";
}
