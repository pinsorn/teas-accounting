namespace Accounting.Domain.Common;

/// <summary>Tracks who created/modified the row and when. All TIMESTAMPTZ(3) UTC offset, displayed in Asia/Bangkok.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    long? CreatedBy { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    long? UpdatedBy { get; set; }
}
