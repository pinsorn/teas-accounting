using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using FluentAssertions;

namespace Accounting.Domain.Tests;

/// <summary>
/// Sprint-8 domain surface for the Business Unit dimension. The entity is an anemic
/// tag (behavior lives in the service layer + GL snapshot), so the domain-level
/// invariants worth pinning are: a new BU is active by default (soft-deactivate
/// depends on this), and the dimension is opt-in (nullable) on every wired carrier.
/// </summary>
public class BusinessUnitTests
{
    [Fact]
    public void New_business_unit_is_active_by_default()
    {
        var bu = new BusinessUnit { CompanyId = 1, Code = "ECOM", NameTh = "อีคอมเมิร์ซ" };
        bu.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Journal_line_business_unit_is_optional_and_unset_by_default()
    {
        var line = new JournalLine { LineNo = 1, AccountId = 1, DebitAmount = 100m };
        line.BusinessUnitId.Should().BeNull();
    }
}
