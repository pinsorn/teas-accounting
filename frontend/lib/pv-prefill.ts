// R4 (confirm-round 2026-07-15) — PV-from-VI prefill must land the PV's grand total
// EXACTLY on the VI's outstanding amount, in every vendor/VAT combo, so the user opens a
// PV that already balances (they can still edit it from there).
//
// The PV form always RE-DERIVES its line VAT (rate = vendor.vatRegistered
// ? taxRateForProductType(productType) : 0) and adds it on top of the line "amount"
// (the pre-VAT base) — it never accepts a VAT-inclusive figure directly. The old prefill
// scaled the VI's own subtotal by the outstanding ratio and assumed the form's own VAT
// re-add would restore the total; that only holds when the re-derived rate happens to
// match whatever rate the VI itself used. It doesn't for e.g. a non-VAT-registered
// vendor's VI carrying non-recoverable VAT (re-derived rate 0%, VI's own rate > 0%).
//
// Given the rate the form will ACTUALLY apply to this row (computed by the caller, same
// branch as the live form), solve base = outstanding / (1 + rate) instead, so
// base + round(base * rate) reconstructs `outstanding` satang-exact.
//
// Works in integer satang throughout to sidestep float drift. At rate 0 this is the
// identity (always exact — the common non-VAT-vendor case this fix targets). At a
// nonzero rate it's exact whenever `outstanding` itself is reachable as base+round(base
// * rate) for some integer-satang base — true whenever the VI was built under that same
// rate (the normal regression case); VAT's satang rounding means a handful of targets per
// 1000 are mathematically unreachable at ANY base when the re-derived rate genuinely
// differs from the VI's own rate, landing off by at most 1 satang (verified exhaustively
// in the test file).
export function derivePvPrefillBase(outstanding: number, rate: number): number {
  const outCents = Math.round(outstanding * 100);
  if (rate <= 0) return outCents / 100;
  return Math.round(outCents / (1 + rate)) / 100;
}
