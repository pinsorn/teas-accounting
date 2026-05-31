namespace Accounting.Application.Payroll;

/// <summary>P-D #2 — fills the official RD ภ.ง.ด.1 (monthly WHT return for ม.40(1) salary) + ใบแนบ
/// from a posted payroll run.</summary>
public interface IPnd1FilingService
{
    Task<byte[]> BuildPnd1MonthlyAsync(long runId, CancellationToken ct);
}
