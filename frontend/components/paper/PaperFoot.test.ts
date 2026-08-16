import { describe, it, expect } from 'vitest';
import fixture from '../../fixtures/paper-foot-plan.json';
import { computeFootTotals, type PaperSummary } from './types';

// PLAN-test-hardening.md WS-4 / C4 — mirror-contract test for the foot math. The 700/850 drift:
// FE and BE each read PaperSummary.Total with the OPPOSITE meaning, and each side's OWN tests
// passed because each side tested against its own (mis-)understanding. This fixture is emitted by
// a C# test (backend/tests/Accounting.Api.Tests/Pdf/PaperFootMirrorFixtureTests.cs) from the
// REAL PaperFootPlan.Build(...) output — the canonical BE side of the mirror. This test asserts
// PaperFoot's OWN extracted math (computeFootTotals) produces the identical grand/net values for
// the SAME inputs, so the two sides are compared against a SHARED artifact instead of against
// themselves — the drift can never silently return without failing here first.
describe('computeFootTotals matches the shared BE fixture (mirror-contract: PaperFootPlan.cs)', () => {
  for (const c of fixture.cases) {
    it(c.name, () => {
      const result = computeFootTotals(c.summary as PaperSummary);
      expect(result.grandTotal).toBe(c.grandTotal);
      expect(result.netTotal).toBe(c.netTotal);
      expect(result.hasWht).toBe(c.hasWht);
    });
  }
});

// F9 (PLAN-fix-findings-2026-08-16.md Unit B, PROGRESS-local-hard-test.md) — payment-vouchers/new
// page.tsx was passing `total: subtotal + vat` (the GRAND total) instead of `total: net`
// (จ่ายสุทธิ), so the live preview showed Grand ฿11,000.00 / Net ฿10,700.00 for a
// 10,000.00 + 700.00 VAT / 300.00 WHT voucher, when the saved document and ledger both show
// Grand ฿10,700.00 / Net ฿10,400.00. These cases assert computeFootTotals against the summary
// shape the page now produces (`total: net`), for all three WHT states it must not regress.
describe('PV create-page summary contract (F9 — total must be net, not grand)', () => {
  it('normal WHT: 10,000.00 + 700.00 VAT, 3% WHT (300.00) → Grand 10,700.00 / Net 10,400.00', () => {
    const subtotal = 10000;
    const vat = 700;
    const wht = 300;
    const selfWithhold = false;
    const net = selfWithhold ? subtotal + vat : subtotal + vat - wht;
    const summary: PaperSummary = {
      subtotal,
      beforeVat: subtotal,
      vat,
      total: net,
      wht: wht > 0 && !selfWithhold ? wht : null,
    };
    const result = computeFootTotals(summary);
    expect(result.grandTotal).toBe(10700);
    expect(result.netTotal).toBe(10400);
    expect(result.hasWht).toBe(true);
  });

  it('no WHT: 10,000.00 + 700.00 VAT → Grand 10,700.00 / Net 10,700.00 (unchanged)', () => {
    const subtotal = 10000;
    const vat = 700;
    const wht = 0;
    const selfWithhold = false;
    const net = selfWithhold ? subtotal + vat : subtotal + vat - wht;
    const summary: PaperSummary = {
      subtotal,
      beforeVat: subtotal,
      vat,
      total: net,
      wht: wht > 0 && !selfWithhold ? wht : null,
    };
    const result = computeFootTotals(summary);
    expect(result.grandTotal).toBe(10700);
    expect(result.netTotal).toBe(10700);
    expect(result.hasWht).toBe(false);
  });

  it('self-withhold: 10,000.00 + 700.00 VAT, WHT absorbed → vendor paid 10,700.00 in full (unchanged)', () => {
    const subtotal = 10000;
    const vat = 700;
    const wht = 300; // absorbed by the company, never deducted from the vendor
    const selfWithhold = true;
    const net = selfWithhold ? subtotal + vat : subtotal + vat - wht;
    const summary: PaperSummary = {
      subtotal,
      beforeVat: subtotal,
      vat,
      total: net,
      wht: wht > 0 && !selfWithhold ? wht : null,
    };
    const result = computeFootTotals(summary);
    expect(result.grandTotal).toBe(10700);
    expect(result.netTotal).toBe(10700);
    expect(result.hasWht).toBe(false);
  });
});
