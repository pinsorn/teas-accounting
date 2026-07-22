'use client';

import { forwardRef } from 'react';
import { cn } from '@/lib/utils';
// Conversion lives in lib/percent-rate.ts (dependency-free) so it's unit-testable under this
// repo's vitest setup, which has no `@/` alias resolution — re-exported here so existing
// call-sites/imports of PercentRateInput's conversion helpers keep working unchanged.
import { fractionToPercent, percentToFraction, clampPercent } from '@/lib/percent-rate';
export { fractionToPercent, percentToFraction, clampPercent } from '@/lib/percent-rate';

interface Props {
  /** FRACTION (0.07) — the source of truth sent in the payload. */
  value: number;
  /** Receives the new FRACTION value. */
  onValueChange: (fraction: number) => void;
  /** Percent cap (e.g. 30 gives headroom over 7% without allowing 700%). Default 100. */
  max?: number;
  /** Optional percent quick-set chips (e.g. [0, 7]). */
  quickSet?: number[];
  disabled?: boolean;
  className?: string;
  'aria-label'?: string;
  /** Control height — defaults to 'sm' (every existing call site's current look, unchanged).
      F-C (specs/fix-purchase-nonvat-ux.md) — pass 'md' to match a row of default-height
      siblings (e.g. vendor-invoices/new's line row, which mixes this with the default-height
      ExpenseCategorySelector). */
  size?: 'sm' | 'md';
}

export const PercentRateInput = forwardRef<HTMLInputElement, Props>(function PercentRateInput(
  { value, onValueChange, max = 100, quickSet, disabled, className, size = 'sm', ...rest },
  ref,
) {
  const percent = fractionToPercent(value);
  return (
    <div className="flex items-center gap-2">
      <div className="relative flex-1">
        <input
          ref={ref}
          type="number"
          inputMode="decimal"
          min={0}
          max={max}
          step="0.01"
          disabled={disabled}
          value={percent}
          onChange={(e) => {
            const p = clampPercent(Number(e.target.value), max);
            onValueChange(percentToFraction(p));
          }}
          className={cn(
            'input input-bordered w-full pr-6 text-right tabular-nums',
            size === 'sm' && 'input-sm',
            className,
          )}
          {...rest}
        />
        <span className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 text-xs text-base-content/50">
          %
        </span>
      </div>
      {quickSet && quickSet.length > 0 && (
        <div className="flex shrink-0 gap-1">
          {quickSet.map((p) => (
            <button
              key={p}
              type="button"
              className="btn btn-ghost btn-xs"
              disabled={disabled}
              onClick={() => onValueChange(percentToFraction(clampPercent(p, max)))}
            >
              {p}%
            </button>
          ))}
        </div>
      )}
    </div>
  );
});
