using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Master;

/// <summary>
/// พนักงาน — payroll subject. Fully standalone (NO link to <c>User</c>, per Ham 2026-05-31).
/// Carries the data the monthly payroll run + PIT (ม.50(1)) + ภ.ง.ด.1/1ก + employee 50ทวิ +
/// salary-transfer evidence all need: identity, structured address, monthly base salary, bank
/// destination, SSO (ม.33) status, and the MINIMAL ค่าลดหย่อน inputs (marital/spouse/children).
/// See docs/superpowers/specs/payroll-module-design-2026-05-31.md.
/// </summary>
public class Employee : ITenantOwned
{
    public long EmployeeId { get; set; }
    public int  CompanyId  { get; set; }

    public required string EmployeeCode { get; set; }

    // Name — TH required (legal forms are Thai); EN optional. ภ.ง.ด.1ก / 50ทวิ use these.
    public string?         TitleTh     { get; set; }
    public required string FirstNameTh { get; set; }
    public required string LastNameTh  { get; set; }
    public string?         TitleEn     { get; set; }
    public string?         FirstNameEn { get; set; }
    public string?         LastNameEn  { get; set; }

    /// <summary>13-digit national id — the ภ.ง.ด.1/1ก PIN. TaxId usually equals it.</summary>
    public required string NationalId { get; set; }
    public string?         TaxId      { get; set; }

    // Structured address — ภ.ง.ด.1ก needs อำเภอ/จังหวัด/รหัสไปรษณีย์ broken out (same shape
    // planned for the Vendor/PND3 address work, so the RD-form serializer is reused).
    public string? AddressNo   { get; set; }
    public string? Moo         { get; set; }
    public string? Soi         { get; set; }
    public string? Street      { get; set; }
    public string? SubDistrict { get; set; }   // ตำบล/แขวง
    public string? District     { get; set; }  // อำเภอ/เขต
    public string? Province     { get; set; }
    public string? PostalCode   { get; set; }

    public DateOnly  HireDate        { get; set; }
    public DateOnly? TerminationDate { get; set; }

    /// <summary>Monthly base salary (ม.40(1)). Money = decimal(4dp).</summary>
    public decimal BaseSalary { get; set; }

    // Salary-transfer destination — drives the monthly payment-evidence document (Ham requirement).
    public string? BankName        { get; set; }
    public string? BankAccountNo   { get; set; }
    public string? BankAccountName { get; set; }

    // Social Security (ม.33). Rate/ceiling are config-driven (not stored here).
    public bool    SsoApplicable { get; set; } = true;
    public string? SsoNumber     { get; set; }

    // Minimal PIT allowance inputs (ค่าลดหย่อน v1).
    public MaritalStatus MaritalStatus   { get; set; } = MaritalStatus.Single;
    public bool          SpouseHasIncome { get; set; }
    public int           ChildrenCount   { get; set; }

    public bool           IsActive  { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Domain invariants checked before persist.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(EmployeeCode))
            throw new DomainException("employee.code_required", "Employee code is required.");
        if (string.IsNullOrWhiteSpace(FirstNameTh) || string.IsNullOrWhiteSpace(LastNameTh))
            throw new DomainException("employee.name_required", "Thai first and last name are required.");
        var nid = new string((NationalId ?? "").Where(char.IsDigit).ToArray());
        if (nid.Length != 13)
            throw new DomainException("employee.national_id_invalid", "National id must be 13 digits.");
        if (BaseSalary < 0m)
            throw new DomainException("employee.salary_negative", "Base salary cannot be negative.");
        if (ChildrenCount < 0)
            throw new DomainException("employee.children_negative", "Children count cannot be negative.");
        if (TerminationDate is { } t && t < HireDate)
            throw new DomainException("employee.termination_before_hire", "Termination date precedes hire date.");
    }
}
