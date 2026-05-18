namespace Accounting.Domain.Common;

/// <summary>Optimistic concurrency token (BIGINT version column).</summary>
public interface IConcurrencyVersioned
{
    long Version { get; set; }
}
