import { describe, it, expect, afterEach, vi } from 'vitest';
import { bangkokMonthStart, bangkokMonthEnd, dayGap } from './utils';

// GL live-testing Finding 1 (2026-07-07) — the old monthStart/monthEnd used
// `new Date(y, m, 1).toISOString().slice(0, 10)`, which converts through UTC
// and shifts the date by the browser's local offset. In TZ+07:00 that turned
// 2026-07-01 into 2026-06-30. These helpers must return the Bangkok-local
// month bounds regardless of the *host* machine's own timezone, so tests pin
// the system clock to a UTC instant and never touch the `TZ` env var.
describe('bangkokMonthStart / bangkokMonthEnd', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('resolves the first/last day of July when Bangkok wall-clock is mid-July', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-07-15T10:00:00Z')); // 17:00 Bangkok, still July 15
    expect(bangkokMonthStart()).toBe('2026-07-01');
    expect(bangkokMonthEnd()).toBe('2026-07-31');
  });

  it('does not shift a Bangkok month boundary the way toISOString() did', () => {
    vi.useFakeTimers();
    // 2026-06-30T18:00:00Z is already 2026-07-01T01:00 in Bangkok (+7) — the
    // exact class of instant that used to produce "2026-06-30".
    vi.setSystemTime(new Date('2026-06-30T18:00:00Z'));
    expect(bangkokMonthStart()).toBe('2026-07-01');
    expect(bangkokMonthEnd()).toBe('2026-07-31');
  });

  it('handles a 30-day month and a leap-year February correctly', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-10T10:00:00Z'));
    expect(bangkokMonthEnd()).toBe('2026-09-30');

    vi.setSystemTime(new Date('2028-02-15T10:00:00Z')); // 2028 is a leap year
    expect(bangkokMonthEnd()).toBe('2028-02-29');
  });
});

// Bank reconciliation match-window warning (specs/bank-reconciliation.md D4 —
// auto-suggest window is ±7 days). Pins the boundary so a future edit can't
// silently loosen the FE warning threshold.
describe('dayGap', () => {
  it('is 0 for identical dates', () => {
    expect(dayGap('2026-07-10', '2026-07-10')).toBe(0);
  });

  it('is order-independent (absolute value)', () => {
    expect(dayGap('2026-07-01', '2026-07-08')).toBe(7);
    expect(dayGap('2026-07-08', '2026-07-01')).toBe(7);
  });

  it('does not exceed the window at exactly 7 days, but does at 8', () => {
    expect(dayGap('2026-07-01', '2026-07-08')).toBe(7);
    expect(dayGap('2026-07-01', '2026-07-09')).toBe(8);
  });
});
