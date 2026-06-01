namespace Accounting.Application.Payroll;

/// <summary>P-D #2 — fills the official RD ภ.ง.ด.1 (monthly WHT return for ม.40(1) salary) + ใบแนบ
/// from a posted payroll run.</summary>
public interface IPnd1FilingService
{
    Task<byte[]> BuildPnd1MonthlyAsync(long runId, CancellationToken ct);

    /// <summary>P-D #3 — fills ภ.ง.ด.1ก (annual WHT summary, ม.58(1)) + ใบแนบ by aggregating every
    /// POSTED run in the given CE tax year, per employee (whole-year income + tax + address).</summary>
    Task<byte[]> BuildPnd1aAnnualAsync(int year, CancellationToken ct);
}
