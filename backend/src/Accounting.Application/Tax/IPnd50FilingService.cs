namespace Accounting.Application.Tax;

/// <summary>
/// ภ.ง.ด.50 §4 attestation — v1 renders page 1 + page 2 รายการที่ 1 only; รายการที่ 2–9 print
/// BLANK and a blank box on an RD form asserts zero. The filer must therefore attest a first
/// (not amended) filing AND accept that the blank schedules will be completed manually before
/// submission. Mirrors <see cref="Pnd51Attestation"/>.
/// </summary>
public sealed record Pnd50Attestation(bool FirstFiling, bool AcceptBlankSchedules);

public interface IPnd50FilingService
{
    /// <summary>
    /// Build the v1 ภ.ง.ด.50 PDF for the fiscal year. <paramref name="isSme"/> null ⇒ auto-detect
    /// from the CIT profile (paid-up ≤5M ∧ revenue ≤30M). Throws <c>pnd50.not_attestable</c> when
    /// the year cannot be honestly rendered in the v1 scope (see Pnd50FilingService.BuildSheet).
    /// </summary>
    Task<byte[]> BuildPnd50Async(
        int year, bool? isSme, bool hasRelatedPartyOver200M,
        Pnd50Attestation? attest, CancellationToken ct);
}
