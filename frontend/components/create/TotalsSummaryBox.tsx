'use client';

import { formatTHB } from '@/lib/utils';

// Create-page redesign (cont.80) — the dark charcoal totals box. Caller passes
// its own rows (subtotal / discount / vat / wht …) + a grand total; the box
// renders muted-white label/value rows and the grand total big in peach. Values
// are numbers (formatted as THB here) — pass a pre-signed number for a discount
// row if you want it shown negative (formatTHB renders the sign).
export interface TotalRow {
  label: string;
  value: number;
  /** Slightly dimmer — e.g. a discount line. */
  muted?: boolean;
}

export function TotalsSummaryBox({
  rows,
  grandLabel,
  grandValue,
}: {
  rows: TotalRow[];
  grandLabel: string;
  grandValue: number;
}) {
  return (
    <div className="rounded-card bg-ink-900 p-5 text-white">
      <dl className="space-y-2">
        {rows.map((r, i) => (
          <div key={i} className="flex items-center justify-between text-sm">
            <dt className={r.muted ? 'text-white/55' : 'text-white/70'}>{r.label}</dt>
            <dd className="tabular-nums text-white/85">{formatTHB(r.value)}</dd>
          </div>
        ))}
        <div className="mt-3 flex items-center justify-between border-t border-white/10 pt-3">
          <dt className="text-[15px] font-semibold text-white">{grandLabel}</dt>
          <dd className="text-2xl font-bold tabular-nums text-peach-500">{formatTHB(grandValue)}</dd>
        </div>
      </dl>
    </div>
  );
}
