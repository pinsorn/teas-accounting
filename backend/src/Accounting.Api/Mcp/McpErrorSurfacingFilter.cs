using System.Text.Json;
using Accounting.Domain.Common;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Accounting.Api.Mcp;

/// <summary>
/// specs/mcp-error-surfacing.md §1 — wraps every MCP tool invocation so a caught BUSINESS
/// exception (not a crash) reaches the client as readable text instead of the SDK's own
/// generic "An error occurred invoking '{tool}'." swallow (confirmed on prod v1.18.0 — the
/// doc comment on <see cref="McpE2Exception"/> used to claim the SDK surfaced the message;
/// it does not).
///
/// API used: <c>ModelContextProtocol.Server.McpServerOptions.Filters.Request.CallToolFilters</c>
/// (public, present in ModelContextProtocol.Core 1.4.0 — verified by decompiling the installed
/// package; no fallback per-tool <c>Surface&lt;T&gt;</c> helper was needed).
///
/// Ordering (verified by decompiling <c>McpServerImpl.ConfigureTools</c>/<c>BuildFilterPipeline</c>):
/// <list type="bullet">
/// <item>`IConfigureOptions&lt;McpServerOptions&gt;` callbacks run in DI-registration order, and
/// `BuildFilterPipeline` makes the FIRST-registered <c>CallToolFilters</c> entry the OUTERMOST
/// wrapper. Registering this filter AFTER <c>.AddAuthorizationFilters()</c> in Program.cs means
/// <c>AuthorizationFilterSetup.ConfigureCallToolFilter</c> (registered first) stays outermost and
/// this filter sits INSIDE it, wrapping only the actual tool dispatch.</item>
/// <item>An unauthorized call therefore still throws <c>McpProtocolException("Access forbidden...")</c>
/// from the auth filter BEFORE this filter's try/catch ever runs — a 401/403-equivalent is never
/// reinterpreted as a friendly business error. No auth-weakening.</item>
/// <item>The SDK's OWN built-in catch-all (the <c>initialHandler</c> factory passed to
/// <c>BuildFilterPipeline</c> in <c>ConfigureTools</c>) wraps EVERYTHING, including both auth
/// filters and this one. Any exception type this filter does NOT catch (or explicitly rethrows)
/// falls through unchanged to that outer catch, which keeps today's generic text — satisfying
/// the spec's "anything else: UNCHANGED" row for free, no extra code needed.</item>
/// </list>
///
/// Logging (Tier-2 review fix, 2026-07-13): because this filter is INSIDE the SDK's own
/// generic catch-all (previous bullet), catching a business exception here and returning a
/// normal (non-throwing) result means the SDK's own "unhandled exception" log line never
/// fires for these 4 classes — spec §1 requires "server-side logging must stay". Each catch
/// below logs BEFORE returning, at Warning (a caught business rule, not a crash).
/// </summary>
public static class McpErrorSurfacingFilterExtensions
{
    public static IMcpServerBuilder AddErrorSurfacingFilter(this IMcpServerBuilder builder)
    {
        // Options-with-dependency overload: ILoggerFactory isn't available in the plain
        // Action<McpServerOptions> Configure(...) used elsewhere in this file's earlier
        // revision, so resolve it here to build one ILogger for the filter closure.
        builder.Services.AddOptions<McpServerOptions>().Configure<ILoggerFactory>((options, loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("Accounting.Api.Mcp.McpErrorSurfacingFilter");
            options.Filters.Request.CallToolFilters.Add(next => async (context, ct) =>
            {
                var toolName = context.Params?.Name ?? "(unknown tool)";
                try
                {
                    return await next(context, ct);
                }
                catch (McpE2Exception ex)
                {
                    // Message is already "[code] detail" (see McpE2Exception ctor) — forward verbatim.
                    logger.LogWarning(ex, "\"{ToolName}\" rejected: {SurfacedText}", toolName, ex.Message);
                    return ErrorResult(ex.Message);
                }
                catch (DomainException ex)
                {
                    var text = $"[mcp.domain_rule] {ex.Message}";
                    logger.LogWarning(ex, "\"{ToolName}\" rejected: {SurfacedText}", toolName, text);
                    return ErrorResult(text);
                }
                catch (ValidationException ex)
                {
                    var joined = string.Join("; ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
                    var text = $"[mcp.validation] {joined}";
                    logger.LogWarning(ex, "\"{ToolName}\" rejected: {SurfacedText}", toolName, text);
                    return ErrorResult(text);
                }
                catch (JsonException ex)
                {
                    // ex.Message already embeds the JSON Path (e.g. "$.lines[0].uomId") — the
                    // default System.Text.Json message shape for a binding-shape mismatch.
                    var text = $"[mcp.bad_input] {ex.Message}";
                    logger.LogWarning(ex, "\"{ToolName}\" rejected: {SurfacedText}", toolName, text);
                    return ErrorResult(text);
                }
                // Anything else: deliberately NOT caught here — see the class doc's ordering note.
                // The SDK's own outer catch-all logs it (unaffected by this filter).
            });
        });
        return builder;
    }

    private static CallToolResult ErrorResult(string text) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = text }],
    };
}
