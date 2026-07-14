import { describe, it, expect } from 'vitest';
import { fractionToPercent, percentToFraction, clampPercent } from './percent-rate';

// WP1.1 (F15/D5) — pin the exact conversion so a stray refactor can't reintroduce the F15
// defect (typing 7 must never store 7.0 / imply 700%).
describe('fractionToPercent / percentToFraction', () => {
  it('round-trips the standard VAT rate exactly (no float dust)', () => {
    expect(fractionToPercent(0.07)).toBe(7);
    expect(percentToFraction(7)).toBe(0.07);
  });

  it('round-trips a fractional-percent WHT rate (1.5%)', () => {
    expect(fractionToPercent(0.015)).toBe(1.5);
    expect(percentToFraction(1.5)).toBe(0.015);
  });

  it('handles empty/zero', () => {
    expect(fractionToPercent(0)).toBe(0);
    expect(percentToFraction(0)).toBe(0);
  });

  it('rejects non-finite input as 0', () => {
    expect(fractionToPercent(NaN)).toBe(0);
    expect(percentToFraction(Infinity)).toBe(0);
  });

  it('never produces float dust for a 3% WHT rate', () => {
    // 0.1 + 0.2 !== 0.3 territory — assert the exact value, not an epsilon compare.
    expect(percentToFraction(3)).toBe(0.03);
    expect(fractionToPercent(0.03)).toBe(3);
  });

  it('clamps an out-of-range percent to max (F15: typing 700 must never store 7.0)', () => {
    expect(clampPercent(700, 30)).toBe(30);
    expect(percentToFraction(clampPercent(700, 30))).toBe(0.3);
    expect(clampPercent(-5, 30)).toBe(0);
  });
});
