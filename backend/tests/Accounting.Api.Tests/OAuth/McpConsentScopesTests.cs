using Accounting.Api.OAuth;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.OAuth;

/// <summary>
/// H4/M11 §0 — pure unit tests for the scope↔RBAC translation table (no DB). The 3 non-identical
/// mappings (sales.quotation.read/create → sales.quotation.manage; sys.system_info.read → none) are
/// the load-bearing cases: a naive identity-only filter would wrongly strip all three from every user.
/// </summary>
public class McpConsentScopesTests
{
    [Fact]
    public void Identity_mapped_scope_is_dropped_when_the_user_lacks_the_matching_permission()
    {
        var granted = McpConsentScopes.FilterToRbac(
            grantedScopes: ["sales.tax_invoice.read", "sales.tax_invoice.create"],
            userPermissions: new HashSet<string>(StringComparer.Ordinal) { "sales.tax_invoice.read" });

        granted.Should().BeEquivalentTo(["sales.tax_invoice.read"]);
    }

    [Fact]
    public void Quotation_read_and_create_scopes_are_kept_when_the_user_holds_quotation_manage()
    {
        var granted = McpConsentScopes.FilterToRbac(
            grantedScopes: ["sales.quotation.read", "sales.quotation.create"],
            userPermissions: new HashSet<string>(StringComparer.Ordinal) { "sales.quotation.manage" });

        granted.Should().BeEquivalentTo(["sales.quotation.read", "sales.quotation.create"]);
    }

    [Fact]
    public void System_info_read_is_kept_regardless_of_the_users_permissions()
    {
        var granted = McpConsentScopes.FilterToRbac(
            grantedScopes: ["sys.system_info.read"],
            userPermissions: new HashSet<string>(StringComparer.Ordinal));

        granted.Should().BeEquivalentTo(["sys.system_info.read"]);
    }

    [Fact]
    public void Everything_is_dropped_when_the_user_has_no_matching_permissions()
    {
        var granted = McpConsentScopes.FilterToRbac(
            grantedScopes: ["master.customer.manage"],
            userPermissions: new HashSet<string>(StringComparer.Ordinal));

        granted.Should().BeEmpty();
    }
}
