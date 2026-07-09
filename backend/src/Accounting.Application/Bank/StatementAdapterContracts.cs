using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Application.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md D2) — format-agnostic statement adapter
/// contract. Adapters register in a keyed DI collection (<see cref="IEnumerable{T}"/> of
/// <see cref="IBankStatementAdapter"/>); the import service picks one by <see cref="CanHandle"/>.
/// Adding a new bank format (SCB/BBL/...) = a new adapter class only — core, matching, and
/// report code stay unchanged.
/// </summary>
public interface IBankStatementAdapter
{
    string AdapterCode { get; }
    bool CanHandle(string fileName, string mimeType);

    /// <summary>Parse raw statement bytes into the normalized model. Throws
    /// <see cref="DomainException"/> on any structural parse failure (missing header/metadata).
    /// Does NOT run the D10 integrity assertions — that is <see cref="BankStatementIntegrity"/>'s
    /// job, shared across every adapter.</summary>
    ParsedStatement Parse(Stream content, string? password);
}

/// <summary>D3 — direction is fixed to the account-holder's POV: MoneyIn = deposit/ฝากเงิน
/// (matches a Receipt); MoneyOut = withdrawal/ถอนเงิน (matches a PaymentVoucher).</summary>
public sealed record ParsedStatementLine(
    int LineNo, DateOnly TxnDate, TimeOnly? TxnTime, DateOnly? ValueDate,
    StatementDirection Direction, decimal Amount, decimal RunningBalance,
    string Channel, string TxnType, string Description, string? RawRef);

public sealed record ParsedStatement(
    string AdapterCode, string AccountNoRaw, DateOnly PeriodStart, DateOnly PeriodEnd,
    decimal OpeningBalance, decimal ClosingBalance, decimal? WithdrawalTotal, decimal? DepositTotal,
    int? WithdrawalCount, int? DepositCount, IReadOnlyList<ParsedStatementLine> Lines);

/// <summary>
/// Bank reconciliation D10 — parse-integrity assertions shared by BOTH adapters (KBiz CSV,
/// K-Plus PDF). Fails LOUD: throws with a diagnostic listing ONLY offending line numbers +
/// amounts — never raw descriptions/names (no PII leak). The caller (StatementImportService)
/// must run this BEFORE any DB/storage write so a failure persists nothing.
/// </summary>
public static class BankStatementIntegrity
{
    private const decimal Tolerance = 0.005m;

    public static void Validate(ParsedStatement stmt)
    {
        var problems = new List<string>();
        var prevBalance = stmt.OpeningBalance;
        decimal sumIn = 0, sumOut = 0;

        foreach (var line in stmt.Lines)
        {
            // (a) each line's Amount == |balanceDelta| within tolerance.
            var delta = Math.Abs(line.RunningBalance - prevBalance);
            if (Math.Abs(delta - line.Amount) > Tolerance)
                problems.Add($"line {line.LineNo}: amount {line.Amount} does not match balance movement {delta}");

            if (line.Direction == StatementDirection.MoneyIn) sumIn += line.Amount;
            else sumOut += line.Amount;
            prevBalance = line.RunningBalance;
        }

        // (b) declared totals (when present) match the summed lines.
        if (stmt.DepositTotal is { } dep && Math.Abs(sumIn - dep) > Tolerance)
            problems.Add($"deposits sum {sumIn} does not match declared total {dep}");
        if (stmt.WithdrawalTotal is { } wd && Math.Abs(sumOut - wd) > Tolerance)
            problems.Add($"withdrawals sum {sumOut} does not match declared total {wd}");

        // (c) opening + in - out == closing.
        var expectedClosing = stmt.OpeningBalance + sumIn - sumOut;
        if (Math.Abs(expectedClosing - stmt.ClosingBalance) > Tolerance)
            problems.Add(
                $"opening {stmt.OpeningBalance} + deposits {sumIn} - withdrawals {sumOut} = " +
                $"{expectedClosing}, expected closing {stmt.ClosingBalance}");

        if (problems.Count > 0)
            throw new DomainException("bank.import_integrity_failed",
                "Statement integrity check failed: " + string.Join("; ", problems));
    }
}
