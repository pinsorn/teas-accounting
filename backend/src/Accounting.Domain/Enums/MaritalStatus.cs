using System.Diagnostics.CodeAnalysis;

namespace Accounting.Domain.Enums;

/// <summary>Drives the minimal PIT spouse allowance (ม.47 — ฿60,000 when married and the
/// spouse has no income). Kept deliberately small for payroll v1.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "'Single' is the correct domain term for marital status; the DB value is 'SINGLE'.")]
public enum MaritalStatus
{
    Single = 1,
    Married = 2,
}
