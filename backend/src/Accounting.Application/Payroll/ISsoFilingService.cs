namespace Accounting.Application.Payroll;

/// <summary>P-D #4 — Social-Security (ม.33) monthly contribution submission to สำนักงานประกันสังคม
/// (SSO), the แบบ สปส.1-10 ส่วนที่ 1 (employer summary) + ส่วนที่ 2 (รายชื่อผู้ประกันตน). SSO is a SEPARATE
/// agency from the RD. This service does the channel-independent AGGREGATION from a posted payroll
/// run; the chosen output channel (filled PDF vs SSO e-Service upload text file) consumes the model.
/// </summary>
public interface ISsoFilingService
{
    /// <summary>Aggregate a posted run's payslips into the สปส.1-10 model: employer header + one row
    /// per insured employee (those with a contribution &gt; 0) + the run-level totals.</summary>
    Task<SsoMonthlyModel> BuildMonthlyAsync(long runId, CancellationToken ct);

    /// <summary>Build the สปส.1-10 e-Service upload TEXT file (TIS-620) for a posted run, with its
    /// SSO-convention filename.</summary>
    Task<(byte[] Content, string FileName)> BuildMonthlyFileAsync(long runId, CancellationToken ct);

    /// <summary>Fill the official สปส.1-10 ส่วนที่ 1 PDF (print-and-sign) for a posted run — same
    /// aggregation as the file channel, overlaid onto the flat SSO form (it has no AcroForm).</summary>
    Task<byte[]> BuildMonthlyPdfAsync(long runId, CancellationToken ct);
}

/// <summary>One insured person (ผู้ประกันตน) row on ใบแนบ สปส.1-10 ส่วนที่ 2. The สปส.1-10 detail
/// record keeps คำนำหน้า (a code), ชื่อ and ชื่อสกุล in SEPARATE fields, so this carries them
/// split (unlike ภ.ง.ด.1, which merged title+first into one ชื่อ box).</summary>
/// <param name="SsoNumber">เลขที่บัตรประกันสังคม (often = the 13-digit national id).</param>
/// <param name="NationalId">เลขประจำตัวประชาชน (13 digits).</param>
/// <param name="Title">คำนำหน้า (Thai text, e.g. "นาย") — the file channel maps this to the SSO
/// prefix code.</param>
/// <param name="FirstName">ชื่อ (given name only, no title).</param>
/// <param name="LastName">ชื่อสกุล.</param>
/// <param name="Wage">ค่าจ้างที่จ่ายจริง — the ACTUAL (un-capped) wage paid. สปส.1-10 ส่วนที่ 2 reports the
/// actual wage in this column (verified vs a filled BusinessPlus form); the ฿1,650/฿15,000 clamp applies
/// only to the contribution, not this figure. (v1 payroll is salary-only, so this = the payslip's gross
/// taxable; when bonus/OT land, the SSO ค่าจ้าง per ม.5 may need its own snapshot field.)</param>
/// <param name="EmployeeContribution">เงินสมทบผู้ประกันตน (5% of the clamped base — as posted on the payslip).</param>
/// <param name="EmployerContribution">เงินสมทบนายจ้าง (5%, equal).</param>
public readonly record struct SsoLine(
    string SsoNumber,
    string NationalId,
    string Title,
    string FirstName,
    string LastName,
    decimal Wage,
    decimal EmployeeContribution,
    decimal EmployerContribution);

/// <summary>สปส.1-10 monthly model: employer (นายจ้าง) header + insured-person rows + totals.</summary>
/// <param name="EmployerTaxId">เลขประจำตัวผู้เสียภาษี / used as the employer key.</param>
/// <param name="BranchCode">ลำดับที่สาขา (00000 = สำนักงานใหญ่).</param>
/// <param name="EmployerName">ชื่อสถานประกอบการ.</param>
/// <param name="PeriodMonth">เดือน 1-12.</param>
/// <param name="PeriodYearBE">ปี พ.ศ.</param>
/// <param name="PeriodYearCE">ปี ค.ศ. (for file-format channels that want it).</param>
/// <param name="PayDate">วันที่ชำระเงิน (the run's pay date) — the file header's payment-date field.</param>
/// <param name="EmployerAccountNo">เลขที่บัญชีนายจ้าง — 10-digit SSO employer registration number (from
/// CompanyProfile; config fallback). Header field; blank → zeros (not submittable until set).</param>
/// <param name="Lines">insured-person rows (only those with a contribution &gt; 0).</param>
public sealed record SsoMonthlyModel(
    string EmployerTaxId,
    string BranchCode,
    string EmployerName,
    string? Building,
    string? RoomNo,
    string? Floor,
    string? Village,
    string? HouseNo,
    string? Moo,
    string? Soi,
    string? Street,
    string? SubDistrict,
    string? District,
    string? Province,
    string? PostalCode,
    int PeriodMonth,
    int PeriodYearBE,
    int PeriodYearCE,
    DateOnly PayDate,
    string? EmployerAccountNo,
    IReadOnlyList<SsoLine> Lines)
{
    /// <summary>จำนวนผู้ประกันตน on the submission.</summary>
    public int EmployeeCount => Lines.Count;

    /// <summary>รวมค่าจ้างที่จ่ายจริง (sum of the actual wages).</summary>
    public decimal TotalWage => Lines.Sum(l => l.Wage);

    /// <summary>รวมเงินสมทบผู้ประกันตน.</summary>
    public decimal TotalEmployeeContribution => Lines.Sum(l => l.EmployeeContribution);

    /// <summary>รวมเงินสมทบนายจ้าง.</summary>
    public decimal TotalEmployerContribution => Lines.Sum(l => l.EmployerContribution);

    /// <summary>รวมเงินสมทบทั้งสิ้น (ผู้ประกันตน + นายจ้าง) — the cash the employer remits.</summary>
    public decimal GrandTotalContribution => TotalEmployeeContribution + TotalEmployerContribution;
}
