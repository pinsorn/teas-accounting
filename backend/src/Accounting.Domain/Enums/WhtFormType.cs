namespace Accounting.Domain.Enums;

/// <summary>Which monthly WHT return the deduction is reported on.</summary>
public enum WhtFormType
{
    /// <summary>ภ.ง.ด.3 — withheld from individual (non-corporate) payee.</summary>
    Pnd3,
    /// <summary>ภ.ง.ด.53 — withheld from corporate payee.</summary>
    Pnd53,
    /// <summary>ภ.ง.ด.1 — payroll (employer).</summary>
    Pnd1,
    /// <summary>Sprint 9 C1 — ภ.ง.ด.54: WHT on payments to a FOREIGN payee
    /// (FOR-SVC / FOR-ROYAL, 15%). Deferred from Sprint 8.7; required by C4.</summary>
    Pnd54,
}
