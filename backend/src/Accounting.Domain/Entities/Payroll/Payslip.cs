using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Payroll;

/// <summary>
/// One employee's pay for one <see cref="PayrollRun"/>. Snapshots the identity + bank +
/// address the run needs (like <c>WhtCertificate</c>) so a later change to the Employee master
/// never rewrites a posted payslip. Amounts are decimal(4dp). The PIT figure is produced by the
/// pure <c>ThaiPitCalculator</c> (ม.50(1)); YTD fields record the cumulative income/PIT through
/// this run (Thai PIT calendar year) for the next month's projection + the ภ.ง.ด.1ก/50ทวิ totals.
/// </summary>
public class Payslip : ITenantOwned
{
    public long PayslipId    { get; set; }
    public long PayrollRunId { get; set; }
    public int  CompanyId    { get; set; }
    public long EmployeeId   { get; set; }

    // ---- Employee snapshot (frozen at draft) ----
    public required string EmployeeCode { get; set; }
    public required string EmployeeName { get; set; }   // Thai full name (title + first + last)
    public required string NationalId   { get; set; }
    public string?         AddressText  { get; set; }   // composed structured address (payslip/PDF)
    public string?         BankName        { get; set; }
    public string?         BankAccountNo   { get; set; }
    public string?         BankAccountName { get; set; }

    // ---- Earnings / withholdings (money = decimal 4dp) ----
    public decimal GrossTaxable    { get; set; }
    public decimal GrossNonTaxable { get; set; }
    public decimal PitWithheld     { get; set; }
    public decimal SsoEmployee     { get; set; }
    public decimal SsoEmployer     { get; set; }
    public decimal OtherDeductions { get; set; }
    public decimal NetPay          { get; set; }

    // ---- YTD (cumulative through this run, calendar year) ----
    public decimal YtdIncome { get; set; }
    public decimal YtdPit    { get; set; }

    public PayrollRun? Run { get; set; }

    /// <summary>Net = (taxable + non-taxable) − PIT − employee-SSO − other. Employer SSO is a
    /// company cost, not deducted from the employee.</summary>
    public void ComputeNet() =>
        NetPay = GrossTaxable + GrossNonTaxable - PitWithheld - SsoEmployee - OtherDeductions;
}
