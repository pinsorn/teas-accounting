using Accounting.Application.Abstractions;
using FluentAssertions;

namespace Accounting.Api.Tests.OAuth;

public class McpScopesTests
{
    [Fact]
    public void Allowlist_contains_no_post_class_scope()
    {
        foreach (var scope in McpScopes.All)
            McpScopes.ForbiddenSuffixes.Any(sfx => scope.EndsWith(sfx, StringComparison.OrdinalIgnoreCase))
                .Should().BeFalse($"'{scope}' must not be a post-class scope");
    }

    [Fact]
    public void Normalize_drops_unknown_and_post_scopes()
    {
        var got = McpScopes.Normalize(["sales.tax_invoice.read", "sales.tax_invoice.post", "bogus.scope"]);
        got.Should().BeEquivalentTo(["sales.tax_invoice.read"]);
    }
}
