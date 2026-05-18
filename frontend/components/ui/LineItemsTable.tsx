'use client';

import { Plus, Trash2 } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { AmountInput } from './AmountInput';
import { formatTHB } from '@/lib/utils';

// component-patterns.md §8 — editable rows, add/remove, auto-recalc total per row.
export interface LineItem {
  descriptionTh: string;
  quantity: number;
  unitPrice: number;
  taxRate: number;
}

export const EMPTY_LINE: LineItem = {
  descriptionTh: '',
  quantity: 1,
  unitPrice: 0,
  taxRate: 0.07,
};

export function LineItemsTable({
  value,
  onChange,
}: {
  value: LineItem[];
  onChange: (lines: LineItem[]) => void;
}) {
  const t = useTranslations('ti.form');

  const set = (i: number, patch: Partial<LineItem>) =>
    onChange(value.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="font-semibold">{t('lines')}</h2>
        <button
          type="button"
          className="btn btn-ghost btn-sm gap-1"
          onClick={() => onChange([...value, { ...EMPTY_LINE }])}
        >
          <Plus className="h-4 w-4" aria-hidden /> {t('addLine')}
        </button>
      </div>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table">
          <thead>
            <tr>
              <th>{t('description')}</th>
              <th className="w-24 text-right">{t('qty')}</th>
              <th className="w-32 text-right">{t('unitPrice')}</th>
              <th className="w-24 text-right">{t('taxRate')}</th>
              <th className="w-32 text-right">{t('lines')}</th>
              <th className="w-10" />
            </tr>
          </thead>
          <tbody>
            {value.map((l, i) => {
              const lineTotal = l.quantity * l.unitPrice * (1 + l.taxRate);
              return (
                <tr key={i}>
                  <td>
                    <input
                      className="input input-sm input-bordered w-full"
                      value={l.descriptionTh}
                      onChange={(e) => set(i, { descriptionTh: e.target.value })}
                      aria-label={`${t('description')} ${i + 1}`}
                    />
                  </td>
                  <td>
                    <AmountInput
                      value={l.quantity}
                      step={1}
                      onValueChange={(n) => set(i, { quantity: n })}
                      aria-label={`${t('qty')} ${i + 1}`}
                    />
                  </td>
                  <td>
                    <AmountInput
                      value={l.unitPrice}
                      onValueChange={(n) => set(i, { unitPrice: n })}
                      aria-label={`${t('unitPrice')} ${i + 1}`}
                    />
                  </td>
                  <td>
                    <AmountInput
                      value={l.taxRate}
                      step={0.01}
                      onValueChange={(n) => set(i, { taxRate: n })}
                      aria-label={`${t('taxRate')} ${i + 1}`}
                    />
                  </td>
                  <td className="text-right tabular-nums">{formatTHB(lineTotal)}</td>
                  <td>
                    <button
                      type="button"
                      className="btn btn-ghost btn-xs text-error"
                      disabled={value.length === 1}
                      onClick={() => onChange(value.filter((_, idx) => idx !== i))}
                      aria-label={`remove line ${i + 1}`}
                    >
                      <Trash2 className="h-4 w-4" aria-hidden />
                    </button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
