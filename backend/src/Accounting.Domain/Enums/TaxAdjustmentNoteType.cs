namespace Accounting.Domain.Enums;

/// <summary>Whether the adjustment reduces (Credit) or increases (Debit) the original TI total.</summary>
public enum TaxAdjustmentNoteType
{
    /// <summary>ใบลดหนี้ (Credit Note) — ม.86/10. Reduces customer obligation.</summary>
    Credit,
    /// <summary>ใบเพิ่มหนี้ (Debit Note) — ม.86/9. Increases customer obligation.</summary>
    Debit,
}
