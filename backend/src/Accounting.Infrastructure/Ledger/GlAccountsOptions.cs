namespace Accounting.Infrastructure.Ledger;

/// <summary>
/// Account-code mapping used by <see cref="GlPostingService"/> to resolve
/// <c>master.chart_of_accounts.account_id</c> from a logical role.
/// Bound from <c>GlAccounts</c> section in appsettings — codes must exist in the seeded CoA.
/// </summary>
public sealed class GlAccountsOptions
{
    public string ArAccount         { get; init; } = "1130";  // Accounts Receivable
    public string ApAccount         { get; init; } = "2110";  // Accounts Payable
    public string CashAccount       { get; init; } = "1110";  // Cash
    public string BankAccount       { get; init; } = "1120";  // Bank
    public string SalesAccount      { get; init; } = "4000";  // Sales revenue
    public string OutputVatAccount  { get; init; } = "2151";  // VAT payable
    public string InputVatAccount   { get; init; } = "1170";  // VAT receivable / suspense
    public string WhtPayableAccount { get; init; } = "2152";  // ภาษีหัก ณ ที่จ่ายค้างจ่าย
    public string WhtReceivableAccount { get; init; } = "1180"; // ภาษีหัก ณ ที่จ่ายค้างรับ (AR-side, Sprint 8.6)
    public string SalesReturnAccount { get; init; } = "4100"; // Sales return / discount (for CN)

    /// <summary>ม.83/6 reverse-charge VAT (ภ.พ.36) paid by a NON-VAT-registered service
    /// receiver: it must remit the VAT but CANNOT reclaim it (no ภ.พ.30) — the VAT is a
    /// permanent sunk cost, so it is expensed here instead of debited to InputVat (1170).
    /// Must exist in the seeded chart of accounts.</summary>
    public string IrrecoverableVatExpenseAccount { get; init; } = "5350"; // ภาษีซื้อขอคืนไม่ได้
}
