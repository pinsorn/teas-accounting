using System.Globalization;
using System.Security.Claims;
using Accounting.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Accounting.Api.Middleware;

/// <summary>
/// §A (spec mcp-expansion.md) — the shared time-limited-token format for the ONE anonymous
/// <c>/public/pdf</c> route. Both the minting side (<c>TeasMcpTools.PublicPdfUrl</c>) and the
/// parsing side (<see cref="PublicPdfTenantMiddleware"/>) reference this so the payload
/// separator/order can never drift between the two.
/// </summary>
public static class PublicPdfTokens
{
    /// <summary><see cref="IDataProtectionProvider"/> purpose string (spec §A.2).</summary>
    public const string Purpose = "teas.pdf-link.v1";

    public static string Payload(string docType, long docId, int companyId, int branchId) =>
        $"{docType}|{docId}|{companyId}|{branchId}";
}

/// <summary>
/// §A.1 — anonymous-request tenant context for <c>/public/pdf</c>. Registered in Program.cs
/// BETWEEN <c>UseAuthentication()</c> and <c>UseAuthorization()</c> (before <c>UseTenantContext()</c>)
/// so it runs BEFORE <see cref="TenantMiddleware"/>. It unprotects the <c>t</c> query token and, on
/// success, builds a synthetic authenticated principal carrying ONLY company_id/branch_id claims
/// (never IsSuperAdmin/ApiKeyId) — mirroring <c>Accounting.Api.OAuth.McpPrincipalFactory.Build</c>'s
/// claim shape. <see cref="TenantMiddleware"/> then reads that principal exactly like any other
/// authenticated request: pins the RLS GUC (<c>app.company_id</c>) via its existing poison-guarded
/// pin/reset path, and the EF global query filter reads the same claims — both layers engaged with
/// NO hand-rolled set_config and NO IgnoreQueryFilters.
///
/// Any unprotect failure (tamper / expired / garbage / missing <c>t</c>) leaves <c>context.User</c>
/// untouched (anonymous) — this middleware NEVER throws and NEVER short-circuits the pipeline with a
/// 401/403. The endpoint sees no docType/docId in <c>context.Items</c> and returns 404, giving a
/// probing attacker zero signal about WHY a token was rejected.
/// </summary>
public sealed class PublicPdfTenantMiddleware
{
    public const string DocTypeItemKey = "PublicPdf.DocType";
    public const string DocIdItemKey   = "PublicPdf.DocId";

    private static readonly PathString PublicPdfPath = "/public/pdf";

    private readonly RequestDelegate _next;
    public PublicPdfTenantMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IDataProtectionProvider dataProtection)
    {
        if (context.Request.Path == PublicPdfPath && context.Request.Query.TryGetValue("t", out var t) && t.Count > 0)
        {
            try
            {
                var protector = dataProtection.CreateProtector(PublicPdfTokens.Purpose).ToTimeLimitedDataProtector();
                var payload = protector.Unprotect(t.ToString()!);
                var parts = payload.Split('|');
                if (parts.Length == 4 && parts[0].Length > 0
                    && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var docId) && docId > 0
                    && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var companyId) && companyId > 0
                    && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var branchId) && branchId > 0)
                {
                    var claims = new[]
                    {
                        new Claim(TenantClaims.CompanyId, companyId.ToString(CultureInfo.InvariantCulture)),
                        new Claim(TenantClaims.BranchId, branchId.ToString(CultureInfo.InvariantCulture)),
                    };
                    // Non-empty authenticationType is load-bearing: ClaimsIdentity(claims) with NO
                    // auth type has IsAuthenticated=false, and TenantMiddleware would then skip the
                    // pin entirely (silent tenant-less read). NEVER add IsSuperAdmin/ApiKeyId claims.
                    var identity = new ClaimsIdentity(claims, "PublicPdfToken");
                    context.User = new ClaimsPrincipal(identity);
                    context.Items[DocTypeItemKey] = parts[0];
                    context.Items[DocIdItemKey] = docId;
                }
            }
            catch
            {
                // Tamper / expired / garbage token — leave context.User anonymous. Do NOT throw,
                // do NOT 401. The endpoint 404s (spec §A.1/§A.2).
            }
        }

        await _next(context);
    }
}

public static class PublicPdfTenantMiddlewareExtensions
{
    public static IApplicationBuilder UsePublicPdfTenantContext(this IApplicationBuilder app) =>
        app.UseMiddleware<PublicPdfTenantMiddleware>();
}
