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

    /// <summary>R1/C6 (WP-2) — กำไรสะสม (Retained Earnings), the credit side of a non-VAT AR
    /// backfill correction for an invoice issued in a CLOSED fiscal year (never Sales — that
    /// year's P&amp;L is already final). Same code YearCloseService already sweeps into
    /// (YearCloseService.cs:139, hardcoded there); resolved dynamically here instead, per the
    /// "never hardcode a code" rule every other GL role in this options object follows.</summary>
    public string RetainedEarningsAccount { get; init; } = "3300"; // กำไรสะสม

    /// <summary>ม.83/6 reverse-charge VAT (ภ.พ.36) paid by a NON-VAT-registered service
    /// receiver: it must remit the VAT but CANNOT reclaim it (no ภ.พ.30) — the VAT is a
    /// permanent sunk cost, so it is expensed here instead of debited to InputVat (1170).
    /// Must exist in the seeded chart of accounts.</summary>
    public string IrrecoverableVatExpenseAccount { get; init; } = "5350"; // ภาษีซื้อขอคืนไม่ได้

    // ---- Payroll (P-C). Seeded by 482_seed_payroll_prefix_and_accounts.sql. ----
    public string SalaryExpenseAccount      { get; init; } = "5400"; // เงินเดือนและค่าจ้าง (DR)
    public string EmployerSsoExpenseAccount { get; init; } = "5410"; // เงินสมทบประกันสังคม-นายจ้าง (DR)
    public string PitPayableAccount         { get; init; } = "2153"; // ภ.ง.ด.1 หัก ณ ที่จ่ายค้างนำส่ง (CR)
    public string SsoPayableAccount         { get; init; } = "2160"; // เงินสมทบประกันสังคมค้างนำส่ง (CR)
    public string NetWagesPayableAccount    { get; init; } = "2170"; // เงินเดือนค้างจ่าย (CR)
    public string OtherDeductionsPayableAccount { get; init; } = "2180"; // เงินหักจากพนักงานค้างนำส่ง (CR)

    // ---- Fixed Assets (Cycle D, specs/fixed-assets.md §2). Disposal output VAT reuses the
    // existing OutputVatAccount (2151) above — no separate prop for that. ----
    public string FixedAssetCostAccount        { get; init; } = "1610"; // อุปกรณ์และเครื่องใช้สำนักงาน (DR)
    public string AccumulatedDepreciationAccount { get; init; } = "1690"; // ค่าเสื่อมราคาสะสม (CR, contra-asset)
    public string DepreciationExpenseAccount   { get; init; } = "5450"; // ค่าเสื่อมราคา (DR)
    public string GainOnAssetDisposalAccount   { get; init; } = "4200"; // กำไรจากการจำหน่ายสินทรัพย์ (CR)
    public string LossOnAssetDisposalAccount   { get; init; } = "5460"; // ขาดทุนจากการจำหน่ายสินทรัพย์ (DR)

    /// <summary>Every account code this options object maps. Master-data admin refuses to
    /// deactivate or header-flip these — GlPostingService resolves them on every post, for
    /// every document type. Kept explicit (no reflection) and pinned by GlAccountsOptionsTests.</summary>
    public IEnumerable<string> AllCodes() =>
    [
        ArAccount, ApAccount, CashAccount, BankAccount, SalesAccount, OutputVatAccount,
        InputVatAccount, WhtPayableAccount, WhtReceivableAccount, SalesReturnAccount,
        RetainedEarningsAccount,
        IrrecoverableVatExpenseAccount, SalaryExpenseAccount, EmployerSsoExpenseAccount,
        PitPayableAccount, SsoPayableAccount, NetWagesPayableAccount, OtherDeductionsPayableAccount,
        FixedAssetCostAccount, AccumulatedDepreciationAccount, DepreciationExpenseAccount,
        GainOnAssetDisposalAccount, LossOnAssetDisposalAccount,
    ];
}
