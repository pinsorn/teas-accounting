import { describe, it, expect } from 'vitest';
import { derivePoLineVatRate } from './po-line-vat';

describe('derivePoLineVatRate', () => {
  it('prefers the PO line own taxRate when the DTO carries one', () => {
    expect(derivePoLineVatRate({ taxRate: 0.07, lineAmount: 3000, taxAmount: 0 }, true, true, 0.07))
      .toBe(0.07);
  });

  it('derives from taxAmount/lineAmount when both are present (co2 case, unchanged)', () => {
    expect(derivePoLineVatRate({ lineAmount: 3000, taxAmount: 210 }, true, true, 0.07)).toBe(0.07);
  });

  it('falls back to the company standard rate only when company AND vendor are VAT-registered', () => {
    expect(derivePoLineVatRate({ lineAmount: 3000, taxAmount: 0 }, true, true, 0.07)).toBe(0.07);
  });

  it('stays 0 when company or vendor is not VAT-registered (never blanket 0.07)', () => {
    expect(derivePoLineVatRate({ lineAmount: 3000, taxAmount: 0 }, false, true, 0.07)).toBe(0);
    expect(derivePoLineVatRate({ lineAmount: 3000, taxAmount: 0 }, true, false, 0.07)).toBe(0);
  });

  // WP-B (fix-po-vi-vat-derivation) — a PO line whose OWN taxRate is 0 must stay 0 even at a
  // VAT-registered company+vendor: the PO said exempt, so it must not fall through to the std
  // rate just because taxAmount is also 0 (that fallback is for callers with NO taxRate at all).
  it('an explicit taxRate: 0 from the PO line stays 0 at a registered company+vendor (exempt line, not blanket std rate)', () => {
    expect(derivePoLineVatRate({ taxRate: 0, lineAmount: 3000, taxAmount: 0 }, true, true, 0.07)).toBe(0);
  });
});
