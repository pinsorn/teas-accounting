'use client';

import { forwardRef } from 'react';
import { cn } from '@/lib/utils';

// component-patterns.md §4 — numeric, tabular-nums, 2 decimals, no negatives
// (use a discount field instead). Controlled number; emits number via onValueChange.
interface Props {
  value: number;
  onValueChange: (n: number) => void;
  step?: number;
  disabled?: boolean;
  className?: string;
  'aria-label'?: string;
}

export const AmountInput = forwardRef<HTMLInputElement, Props>(function AmountInput(
  { value, onValueChange, step = 0.01, disabled, className, ...rest },
  ref,
) {
  return (
    <input
      ref={ref}
      type="number"
      inputMode="decimal"
      min={0}
      step={step}
      disabled={disabled}
      value={Number.isFinite(value) ? value : 0}
      onChange={(e) => {
        const n = Number(e.target.value);
        onValueChange(Number.isFinite(n) && n >= 0 ? n : 0);
      }}
      className={cn(
        'input input-bordered input-sm w-full text-right tabular-nums',
        className,
      )}
      {...rest}
    />
  );
});
