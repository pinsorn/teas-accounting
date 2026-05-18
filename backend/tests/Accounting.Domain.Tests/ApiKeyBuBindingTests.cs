using Accounting.Domain.Common;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests;

/// <summary>Sprint 14 P7 — per-key BU binding rule (pure).</summary>
public sealed class ApiKeyBuBindingTests
{
    [Fact]
    public void No_key_binding_passes_request_through()
    {
        ApiKeyBuBinding.Resolve(requestBu: 7, keyBu: null).Should().Be((7, (string?)null));
        ApiKeyBuBinding.Resolve(requestBu: null, keyBu: null).Should().Be(((int?)null, (string?)null));
    }

    [Fact]
    public void Bound_key_auto_fills_when_request_omits()
        => ApiKeyBuBinding.Resolve(requestBu: null, keyBu: 3).Should().Be((3, (string?)null));

    [Fact]
    public void Bound_key_same_bu_ok()
        => ApiKeyBuBinding.Resolve(requestBu: 3, keyBu: 3).Should().Be((3, (string?)null));

    [Fact]
    public void Bound_key_different_bu_is_locked_mismatch()
    {
        var (eff, err) = ApiKeyBuBinding.Resolve(requestBu: 9, keyBu: 3);
        eff.Should().BeNull();
        err.Should().Be("business_unit.locked_mismatch");
    }
}
