namespace Accounting.Domain.Enums;

/// <summary>
/// Sprint 10 — Product taxonomy. Drives WHT-base aggregation (service vs goods)
/// and is a quick-tag for ม.81 exempt items. Actual VAT-exempt status is still
/// driven by the linked tax_code (EXEMPT_* is a hint, not the source of truth).
/// </summary>
public enum ProductType
{
    Good,
    Service,
    ExemptGood,
    ExemptService,
}
