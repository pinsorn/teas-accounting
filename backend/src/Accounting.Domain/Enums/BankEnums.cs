namespace Accounting.Domain.Enums;

/// <summary>Bank reconciliation (specs/bank-reconciliation.md D3) — account-holder POV.
/// MoneyIn = deposit/ฝากเงิน / balance INCREASES / matches a Receipt.
/// MoneyOut = withdrawal/ถอนเงิน / balance DECREASES / matches a PaymentVoucher.</summary>
public enum StatementDirection
{
    MoneyIn,
    MoneyOut,
}

/// <summary>Bank reconciliation (D10) — outcome of a statement import parse. On any
/// integrity-assertion mismatch the import is Failed and persists nothing.</summary>
public enum ImportStatus
{
    Parsed,
    Failed,
}

/// <summary>Bank reconciliation (D8) — statement_lines.match_status lifecycle. Matched =
/// confirmed link only (reversible). Posted = JE-backed (immutable JE — unmatch blocked).
/// Ignored = user-excluded non-reconciling row (reversible).</summary>
public enum MatchStatus
{
    Unmatched,
    Matched,
    Posted,
    Ignored,
}
