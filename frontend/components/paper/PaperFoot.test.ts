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
