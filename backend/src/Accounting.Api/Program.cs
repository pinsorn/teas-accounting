using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;
using Accounting.Api;
using Accounting.Api.Authorization;
using Accounting.Api.BackgroundServices;
using Accounting.Api.Endpoints;
using Accounting.Api.Middleware;
using Accounting.Api.OAuth;
using Accounting.Api.Scheduling;
using Accounting.Api.Tenancy;
using Accounting.Application;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Quartz;

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

// Move-jobs-to-api (2026-07-04) — the 2 Quartz jobs formerly hosted by the separate
// Accounting.Workers process (never deployed to prod; see the design doc's "Why") now run
// here, same composition root as the hosted services above. No StartNow — the VAT snapshot
// fires at the next 02:00 Bangkok, not on boot; unchanged from the Workers host.
//
// Quartz:Enabled (default true) gates only AddQuartzHostedService — the piece that actually
// STARTS the scheduler — not the job/trigger registrations above it. Found empirically: the
// test suite boots 75+ independent WebApplicationFactory<Program> hosts in the SAME process
// (RbacApiFactory/McpApiFactory); Quartz's internal Microsoft.Extensions.Logging bridge caches
// the FIRST host's ILoggerFactory in process-wide static state, so every LATER host's scheduler
// start throws ObjectDisposedException the instant it tries to log (quartznet/quartznet#1136 —
// confirmed fixed by not starting the scheduler in a test host). RbacApiFactory/McpApiFactory
// set Quartz:Enabled=false; real dotnet run/prod never sets it, so it defaults on there.
builder.Services.AddQuartz(q =>
{
    // Daily VAT register snapshot — 02:00 Asia/Bangkok
    var vatSnapshot = new JobKey(nameof(VatRegisterSnapshotJob));
    q.AddJob<VatRegisterSnapshotJob>(opts => opts.WithIdentity(vatSnapshot));
    q.AddTrigger(t => t
        .ForJob(vatSnapshot)
        .WithIdentity("vat_snapshot_daily")
        .WithCronSchedule("0 0 2 * * ?", c => c.InTimeZone(
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok"))));

    // ภ.พ.30 deadline alert — every day at 09:00 between day 12 and 15
    var pnd30Alert = new JobKey(nameof(Pnd30DeadlineAlertJob));
    q.AddJob<Pnd30DeadlineAlertJob>(opts => opts.WithIdentity(pnd30Alert));
    q.AddTrigger(t => t
        .ForJob(pnd30Alert)
        .WithIdentity("pnd30_deadline_alert")
        .WithCronSchedule("0 0 9 12-15 * ?"));
});
if (builder.Configuration.GetValue("Quartz:Enabled", true))
    builder.Services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

// Multi-tenant context: per-request scope, populated from the JWT claim set. Falls back to a
// settable per-scope company pin when there is no HttpContext (the jobs above) — see
// AmbientTenantContext's class comment. Registered as its own concrete type AND mapped as
// ITenantContext to the SAME scoped instance (mirrors the retired WorkerTenantContext wiring)
// so a job can resolve AmbientTenantContext to call SetCompany, and the DbContext (which
// constructor-injects the interface) reads that exact mutation in the same scope.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AmbientTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());

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
// The single MCP resource (RFC 8707 aud). Registered on the server (so a client `resource` param
// validates) and enforced on validation (AddAudiences). Matches McpPrincipalFactory + the seeder.
var mcpResource = $"{(builder.Configuration["App:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/')}/mcp";
builder.Services.AddOpenIddict()
    .AddServer(o =>
    {
        // Issuer = the PUBLIC origin, not the request host: the backend sits behind the Next
        // passthrough (prod Host=127.0.0.1:5180), so host-derived discovery URLs would advertise
        // an unreachable endpoint to every OAuth client. Caught by the local E2E via :3000.
        o.SetIssuer(new Uri((builder.Configuration["App:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/') + "/"));
        o.SetAuthorizationEndpointUris("oauth/authorize")
         .SetTokenEndpointUris("oauth/token")
         .SetConfigurationEndpointUris(".well-known/openid-configuration",
                                        ".well-known/oauth-authorization-server");
        o.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
        o.RequireProofKeyForCodeExchange();                 // PKCE S256 mandatory
        o.RegisterScopes(McpScopes.All.ToArray());
        o.RegisterResources(mcpResource);   // RFC 8707 — else invalid_target/ID2190 on a `resource` param
        o.SetAccessTokenLifetime(TimeSpan.FromMinutes(10)); // 5–15 min (spec §6b)
        o.SetRefreshTokenLifetime(TimeSpan.FromHours(8));   // ~1 workday absolute
        o.UseReferenceRefreshTokens();                      // reference refresh → family revocation
        o.SetRefreshTokenReuseLeeway(TimeSpan.Zero);        // strict reuse detection (no replay window)
        // Persistent X509 certs when configured (prod — tokens must survive API restarts);
        // ephemeral keys otherwise (dev/test). Prod without certs = hard fail, never silent ephemeral.
        var signPfx = builder.Configuration["Oauth:SigningCertPath"];
        var encPfx = builder.Configuration["Oauth:EncryptionCertPath"];
        var pfxPass = builder.Configuration["Oauth:CertPassword"];
        if (!string.IsNullOrEmpty(signPfx) && !string.IsNullOrEmpty(encPfx))
        {
            o.AddSigningCertificate(X509CertificateLoader.LoadPkcs12FromFile(signPfx, pfxPass));
            o.AddEncryptionCertificate(X509CertificateLoader.LoadPkcs12FromFile(encPfx, pfxPass));
        }
        else if (builder.Environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Oauth:SigningCertPath/EncryptionCertPath are required in Production — " +
                "ephemeral OAuth keys would invalidate all tokens on every restart.");
        }
        else
        {
            o.AddEphemeralEncryptionKey().AddEphemeralSigningKey();
        }
        o.UseAspNetCore()
         .EnableAuthorizationEndpointPassthrough()          // /oauth/authorize handled by our endpoint
         // Token endpoint is NOT passed through: OpenIddict auto-issues the access token from the
         // stored authorization-code principal (code→token). Refresh-flow hardening (M11) hooks the
         // server's ProcessSignInContext event via RefreshTokenRevalidationHandler below, not a
         // passthrough handler.
         // TLS is terminated upstream (Cloudflare → Next passthrough → this backend over HTTP), so
         // OpenIddict must not reject the plain-HTTP hop. Same posture as the rest of the API.
         .DisableTransportSecurityRequirement();

        // M11 — on grant_type=refresh_token, reload the subject user and reject if
        // inactive/off-boarded, then re-derive scopes against current RBAC (shares H4's
        // McpConsentScopes). Order MUST run before OpenIddict prepares the per-token principals
        // (PrepareAccessTokenPrincipal et al.) so the re-derived scope set lands in the issued
        // access token — verified empirically by the M11 proving test.
        o.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(builder =>
            builder.UseScopedHandler<RefreshTokenRevalidationHandler>()
                   .SetOrder(int.MinValue + 100_000));

        // MCP DCR finding (specs/mcp-dcr-client-registration.md) — Claude's connector docs require
        // discovery to list "none" in token_endpoint_auth_methods_supported for a public/PKCE client
        // (Option 3: teas-mcp is ClientTypes.Public, no secret). OpenIddict's built-in
        // AttachClientAuthenticationMethods handler never adds "none" (verified empirically — only
        // client_secret_basic/post + private_key_jwt were advertised). This is metadata ADVERTISING
        // only — it changes nothing about actual token-endpoint auth enforcement (a public client
        // already authenticates via PKCE with no secret today). Order runs LATE so it appends after
        // the built-in handler populates the list, not before (would be overwritten).
        //
        // ORDER FIX (empirical, DCR implementation 2026-07-05 — supersedes the original int.MaxValue -
        // 100_000 used here): that value ties EXACTLY with OpenIddict's own AttachIssuer handler
        // (OpenIddictServerHandlers.Discovery.AttachIssuer.Descriptor.Order == int.MaxValue - 100_000,
        // confirmed against the 7.5.0 source), which runs BEFORE AttachEndpoints (Order + 1_000) sets
        // context.AuthorizationEndpoint. TokenEndpointAuthenticationMethods.Add(None) still worked at
        // that tier because it's a HashSet read only at the very end of the whole dispatch (order-
        // independent) — but the registration_endpoint code below READS context.AuthorizationEndpoint
        // synchronously, so at the old order the guard was silently null and the key was NEVER added
        // (proven: DiscoveryEndpointsTests T6/T7 dumped the raw discovery JSON — no registration_endpoint
        // key, before OR after switching context.Metadata vs context.Transaction.Response — the sink
        // was never the problem). Moved to int.MaxValue - 50_000, safely after AttachEndpoints.
        o.AddEventHandler<OpenIddictServerEvents.HandleConfigurationRequestContext>(builder =>
            builder.UseInlineHandler(context =>
            {
                context.TokenEndpointAuthenticationMethods.Add(OpenIddictConstants.ClientAuthenticationMethods.None);

                // RFC 7591 — advertise the DCR endpoint in BOTH discovery docs (this handler fires for
                // openid-configuration AND oauth-authorization-server). Build it from AuthorizationEndpoint so it
                // carries the request/BACKEND origin → the FE .well-known rewrite swaps it to the public origin,
                // exactly like authorization_endpoint/token_endpoint. Guard the null (a null-forgiving `!` here
                // would 500 the whole discovery response).
                if (context.AuthorizationEndpoint is { } authz)
                    context.Metadata["registration_endpoint"] = new Uri(authz, "/oauth/register").AbsoluteUri;

                return default;
            }).SetOrder(int.MaxValue - 50_000));
    })
    .AddValidation(o =>
    {
        o.UseLocalServer();                                 // validate access tokens in-process
        o.AddAudiences(mcpResource);                        // RFC 8707 — token aud MUST be our /mcp
        o.UseAspNetCore();
    });
// Seeds OAuth scopes + the pre-registered `teas-mcp` client (server-fixed permissions).
builder.Services.AddHostedService<OpenIddictSeeder>();
// Defense-in-depth on the /mcp Bearer principal (reject company/branch<=0, super-admin — spec §6b).
builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, McpBearerClaimsTransform>();

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
        .RequireAuthenticatedUser())
    // /mcp mount — ApiKey OR the OAuth Bearer (per-tool scopes gated by mcpperm:*).
    .AddPolicy(ApiV1Endpoints.McpAuthPolicy, p => p
        .AddAuthenticationSchemes(
            ApiKeyAuthenticationHandler.SchemeName,
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
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
// M4 fix (review 2026-07-04) — AddFixedWindowLimiter("login", ...) used to create ONE limiter
// with a single GLOBAL partition (no partition-key function), so one client's burst locked out
// every legitimate user. Rewritten as a per-IP AddPolicy, mirroring the /api/v1 policy below.
// Requires M5 (UseForwardedHeaders, below) so RemoteIpAddress reflects the real caller through
// the Cloudflare→Next→backend chain, not the Next passthrough address — doing this alone
// (without M5) would just collapse every caller onto the Next server's own IP instead.
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit          = 10,
            Window               = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = 0,
        }));

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
        string partitionKey;
        if (Accounting.Infrastructure.Identity.ApiKeyGenerator.PrefixOf(presented) is { } prefix)
            partitionKey = prefix;
        else
        {
            // OAuth Bearer (on /mcp) — partition per token (stable hash, never logged) so it never
            // shares the unkeyed "__no_api_key" bucket with anonymous callers.
            var auth = ctx.Request.Headers.Authorization.ToString();
            partitionKey = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? "bearer:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(auth["Bearer ".Length..])))[..16]
                : "__no_api_key";
        }
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 120,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0,
            });
    });

    // DCR (/oauth/register) is anonymous + world-reachable → per-IP fixed window, mirroring "login".
    // Relies on M5 UseForwardedHeaders for the real caller IP through Cloudflare→Next→backend (else all
    // DCR shares the Next passthrough IP bucket — still bounded, just coarser). 10/min/IP is ample for a
    // connector that registers once.
    o.AddPolicy("dcr", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10, Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0,
        }));

    // §A (public PDF links) — /public/pdf is anonymous + world-reachable, so it gets its own
    // per-IP fixed window (spec §A.5), copied verbatim from the "login"/"dcr" pattern above.
    // Relies on the same M5 UseForwardedHeaders for the real caller IP through
    // Cloudflare→Next→backend. 30/min/IP is generous for a human opening a link, tight enough
    // to bound a token-guessing/enumeration burst.
    o.AddPolicy(Accounting.Api.Endpoints.PublicPdfEndpoints.RateLimitPolicy, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0,
            }));

    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// §A (public PDF links, spec mcp-expansion.md §A.4) — this is the app's FIRST DataProtection
// consumer (grep of AddDataProtection|PersistKeys|IDataProtector over backend/ was 0 hits before
// this). Without an explicit key ring, the default keyring risks living in-memory on the pm2
// deploy whose unpacked/ is REPLACED each deploy: every restart/redeploy would invalidate every
// public PDF link, and cluster instances wouldn't share keys (a link minted by instance A gets
// rejected by instance B). PersistKeysToFileSystem to a STABLE path outside unpacked/ (deploy
// script creates it ONCE, chowns it, never wipes it). Dev/test (no DataProtection:KeyPath
// configured) fall back to a temp-dir keyring so a bare `dotnet run`/test host needs no config;
// Production keeps the spec's literal default path.
var dpKeyPath = builder.Configuration["DataProtection:KeyPath"]
    ?? (builder.Environment.IsProduction()
        ? "/var/teas/dp-keys"
        : Path.Combine(Path.GetTempPath(), "teas-dp-keys-dev"));
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeyPath))
    .SetApplicationName("teas");   // pinned so a future rename can't orphan the ring

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

// M5 fix (review 2026-07-04) — recover the real client IP through the Cloudflare→Next→backend
// chain so per-IP rate-limit partitioning (M4, above) sees the caller, not the Next passthrough
// address. Next and the backend always talk over loopback (BACKEND_API_URL=http://localhost:5080,
// prod included — same VPS, manual plink deploy), which is ASP.NET Core's DEFAULT trusted
// KnownNetworks/KnownProxies (127.0.0.0/8 + ::1) — no extra proxy config needed for this topology.
// Placed as the very first pipeline middleware per Microsoft's guidance (must run before anything
// that reads RemoteIpAddress, including UseRateLimiter below).
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor,
});

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

// /mcp credential guard (OAuth): reject BOTH credentials at once (X-Api-Key + Bearer → 400), and
// ensure a /mcp 401 carries WWW-Authenticate: Bearer resource_metadata=… so MCP native connectors
// (Claude Desktop/Mobile, Codex, Gemini) can discover the AS (RFC 9728).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/mcp"))
    {
        var hasApiKey = ctx.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName);
        var hasBearer = ctx.Request.Headers.Authorization
            .Any(h => h is not null && h.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase));
        if (hasApiKey && hasBearer)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new
            {
                error = "invalid_request",
                error_description = "Present exactly one credential — X-Api-Key or Bearer, not both.",
            });
            return;
        }
        var baseUrl = ctx.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Accounting.Api.Mcp.AppOptions>>()
            .Value.BaseUrl.TrimEnd('/');
        ctx.Response.OnStarting(() =>
        {
            if (ctx.Response.StatusCode == StatusCodes.Status401Unauthorized)
                ctx.Response.Headers["WWW-Authenticate"] =
                    $"Bearer resource_metadata=\"{baseUrl}/.well-known/oauth-protected-resource\"";
            return Task.CompletedTask;
        });
    }
    await next();
});

app.UseRateLimiter();

app.UseDomainExceptionMapper();
app.UseValidationErrorEnvelope();   // Sprint 13d P5 — ModelState 400 → unified v1 envelope
app.UseAuthentication();
// §A.1 — MUST run between UseAuthentication() and UseAuthorization(), i.e. BEFORE
// UseTenantContext() below: it builds the synthetic principal for /public/pdf that
// TenantMiddleware (UseTenantContext) then pins the RLS GUC from. Registering it later
// would mean TenantMiddleware already ran and early-returned (IsAuthenticated was still
// false) — the RLS GUC would never pin and the EF filter would see CompanyId=0.
app.UsePublicPdfTenantContext();
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
app.MapBankAccountEndpoints();
app.MapStatementImportEndpoints();
app.MapBankReconciliationEndpoints();
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
app.MapExpenseClaimEndpoints();
app.MapApiKeyEndpoints();
app.MapExternalApiV1();
app.MapPublicPdfEndpoints();   // §A — anonymous, rate-limited, token-gated PDF route

// M2 (MCP) — mount the in-process MCP server at /mcp. Same auth posture as /api/v1:
//   • ApiKeyOnlyPolicy → X-Api-Key scheme required (no anonymous; a JWT can't satisfy
//     it, an X-Api-Key principal is required). Per-tool [Authorize] then checks scopes.
//   • PerApiKeyRateLimitPolicy → the M1 per-key 120/min window (partitions on the
//     X-Api-Key header pre-auth, identical to /api/v1).
// mcp-kind keys carry read + *.create scopes only (M1 guard rejects *.post), and no
// post/issue tool is exposed → an agent can only draft; a human approves & posts.
// OAuth 2.1 — RFC 9728 protected-resource metadata (anonymous) + the interactive authorize/consent
// bridge. RFC 8414 / OIDC discovery + /oauth/token are served by the OpenIddict middleware.
app.MapOAuthMetadata();
app.MapOAuthAuthorize();
app.MapOAuthRegister();

app.MapMcp("/mcp")
    .RequireAuthorization(ApiV1Endpoints.McpAuthPolicy)   // ApiKey XOR Bearer (guard middleware rejects both)
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
