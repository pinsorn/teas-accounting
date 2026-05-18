namespace Accounting.Domain.Common;

/// <summary>Marker for entities scoped to a single tenant (company).</summary>
public interface ITenantOwned
{
    int CompanyId { get; }
}
