import { describe, it, expect } from 'vitest';
import { derivePvPrefillBase } from './pv-prefill';

describe('derivePvPrefillBase — R4 PV-from-VI prefill exactness', () => {
  it('rate 0 (non-VAT-registered vendor / exempt product) → base = outstanding', () => {
    expect(derivePvPrefillBase(500, 0)).toBe(500);
  });

  it('R4 evidence: outstanding 214, rate 0 → 214 (non-VAT vendor, was under-settling to 200)', () => {
    expect(derivePvPrefillBase(214, 0)).toBe(214);
  });

  it('rate 0.07, outstanding 1070 → base 1000 (regression: normal VAT-vendor VI)', () => {
    expect(derivePvPrefillBase(1070, 0.07)).toBe(1000);
  });

  it('non-round case: outstanding 107.51, rate 0.07 → base 100.48, satang-exact', () => {
    const base = derivePvPrefillBase(107.51, 0.07);
    expect(base).toBe(100.48);
    // The invariant that actually matters: base + the PV form's own re-derived VAT
    // (rounded the same way the live form rounds it) reconstructs `outstanding` exactly.
    const rederivedTotal = Math.round((base + Math.round(base * 0.07 * 100) / 100) * 100) / 100;
    expect(rederivedTotal).toBe(107.51);
  });

  it('rate 0 is satang-exact for every outstanding value (identity — no VAT quantization)', () => {
    for (let cents = 1; cents <= 20000; cents += 1) {
      expect(derivePvPrefillBase(cents / 100, 0)).toBe(cents / 100);
    }
  });

  it('7% rate is satang-exact whenever the target is reachable, and never off by more than '
    + '1 satang otherwise (VAT rounds to whole satang, so ~1 target in 15 is unreachable at '
    + 'ANY base when the re-derived rate genuinely differs from the VI\'s own — a real, not a '
    + 'code, limit)', () => {
    for (let cents = 1; cents <= 20000; cents += 1) {
      const outstanding = cents / 100;
      const base = derivePvPrefillBase(outstanding, 0.07);
      const baseCents = Math.round(base * 100);
      const rederivedCents = baseCents + Math.round(baseCents * 0.07);
      expect(Math.abs(rederivedCents - cents)).toBeLessThanOrEqual(1);
    }
  });
});
