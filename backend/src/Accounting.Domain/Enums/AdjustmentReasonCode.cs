namespace Accounting.Domain.Enums;

/// <summary>Credit Note reason (ใบลดหนี้ ม.86/10). Per Answer-Sana-Question-Backend4 Q3.</summary>
public enum CreditNoteReasonCode
{
    Typo,
    AmountError,
    CustomerInfo,
    Return,
    PriceReduce,
    Cancel,
}

/// <summary>Debit Note reason (ใบเพิ่มหนี้ ม.86/9). Distinct set from CN.</summary>
public enum DebitNoteReasonCode
{
    PriceIncrease,
    AdditionalCharge,
    ScopeExpansion,
    Typo,
}
