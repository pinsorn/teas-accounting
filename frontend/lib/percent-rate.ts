// WP1.1 (F15/D5) — percent-presentation conversion for rate fields whose payload/storage
// stays a FRACTION (0.07, never 7). Pure, dependency-free (imported by
// components/ui/PercentRateInput.tsx) so it is unit-testable under this repo's vitest setup,
// which has no `@/` path-alias resolution (no vitest.config.ts) — any module that pulls in an
// `@/…` import fails to load in a test. Conversion pinned exactly (float footgun) — do not
// "simplify" the multipliers.
export function fractionToPercent(f: number): number {
  if (!Number.isFinite(f)) return 0;
  return Math.round(f * 1e6) / 1e4; // 0.07 -> 7 (keeps <=4dp, no float dust)
}
export function percentToFraction(p: number): number {
  if (!Number.isFinite(p)) return 0;
  return Math.round(p * 1e6) / 1e8; // 7 -> 0.07; 1.5 -> 0.015
}
export function clampPercent(p: number, max: number): number {
  if (!Number.isFinite(p)) return 0;
  return Math.min(Math.max(p, 0), max);
}
