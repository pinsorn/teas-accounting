'use client';

import { Plus, Trash2 } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { AmountInput } from './AmountInput';
import { ProductPicker, taxRateForProductType } from '@/components/forms/ProductPicker';
import { formatTHB } from '@/lib/utils';
import { useSystemInfo } from '@/lib/queries';
import type { ProductTypeStr } from '@/lib/types';

// component-patterns.md §8 — editable rows, add/remove, auto-recalc total per row.
// Sprint 13j-FE — VAT rate is no longer a free-text field: it's a dropdown
// (7% / 0%), and product-picked lines lock to the product's tax code. Rates are
// fractions internally (0.07). Wider columns + roomier inputs (prev UI clipped).
export interface LineItem {
  descriptionTh: string;
  quantity: number;
  unitPrice: number;
  taxRate: number;
  productId?: number | null;
  productCode?: string | null;
  // Snapshotted from the picked product — drives WHT classification downstream
  // (SERVICE → withholdable). Free-text lines default to GOOD.
  productType?: ProductTypeStr;
  uomText?: string;
  discountPercent?: number;
}

export const EMPTY_LINE: LineItem = {
  descriptionTh: '',
  quantity: 1,
  unitPrice: 0,
  taxRate: 0.07,
  productId: null,
  productCode: null,
  productType: 'GOOD',
  uomText: 'หน่วย',
  discountPercent: 0,
};

// Thai VAT: the standard rate (zero-rated 0% always available). The standard
// rate is NOT hardcoded — it comes from the backend config (Tax:VatRate, exposed
// via /system/info), per CLAUDE.md §4.6 (rate is env-driven, never a UI setting).
// Fallback 0.07 only until /system/info loads.
const FALLBACK_VAT = 0.07;
function vatOptions(stdRate: number): { label: string; value: number }[] {
  return [
    { label: `VAT ${Math.round(stdRate * 100)}%`, value: stdRate },
    { label: '0% (ยกเว้น/ส่งออก)', value: 0 },
  ];
}

/** Net of per-line discount. VAT-inclusive in VAT mode; net only for a non-VAT
 *  company (ม.86 — the line carries a default rate but no VAT is ever charged). */
export function lineTotal(l: LineItem, vatMode = true): number {
  const gross = l.quantity * l.unitPrice;
  const net = gross * (1 - (l.discountPercent ?? 0) / 100);
  return vatMode ? net * (1 + l.taxRate) : net;
}

export function LineItemsTable({
  value,
  onChange,
  enableProduct = false,
  vatEnabled = true,
}: {
  value: LineItem[];
  onChange: (lines: LineItem[]) => void;
  enableProduct?: boolean;
  // cont.77 — caller-side VAT gate, ANDed with the company vatMode. Purchase passes
  // the vendor's VAT-registration here: a non-VAT vendor issues no tax invoice, so
  // there is no input VAT to record → hide the column + drop VAT from the line total.
  vatEnabled?: boolean;
}) {
  const t = useTranslations('ti.form');
  const tq = useTranslations('quotation');
  const sys = useSystemInfo().data;
  const stdRate = sys?.vatRate ?? FALLBACK_VAT;
  const options = vatOptions(stdRate);
  // Non-VAT companies (ม.86): no VAT rate column at all (not merely 0%). Also off
  // when the caller disables it (e.g. a purchase from a non-VAT-registered vendor).
  const showVat = (sys?.vatMode ?? true) && vatEnabled;

  const set = (i: number, patch: Partial<LineItem>) =>
    onChange(value.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <h2 className="font-semibold text-ink-900">{t('lines')}</h2>
        <button
          type="button"
          className="btn btn-outline btn-sm gap-1 border-peach-300 text-peach-700 hover:border-peach-400 hover:bg-peach-50"
          onClick={() => onChange([...value, { ...EMPTY_LINE, taxRate: stdRate }])}
        >
          <Plus className="h-4 w-4" aria-hidden /> {t('addLine')}
        </button>
      </div>
      <div className="overflow-x-auto rounded-card border border-ink-100">
        <table className="w-full min-w-[760px] border-collapse text-sm">
          <thead>
            <tr className="border-b border-ink-100 bg-base-200 text-left text-xs font-semibold uppercase tracking-wide text-ink-500">
              <th className="px-3 py-2.5">{t('description')}</th>
              <th className="w-20 px-2 py-2.5 text-right">{t('qty')}</th>
              {enableProduct && <th className="w-24 px-2 py-2.5">{tq('uom')}</th>}
              <th className="w-32 px-2 py-2.5 text-right">{t('unitPrice')}</th>
              {enableProduct && <th className="w-20 px-2 py-2.5 text-right">{tq('discountPct')}</th>}
              {showVat && <th className="w-48 px-2 py-2.5">{t('taxRate')}</th>}
              <th className="w-32 px-3 py-2.5 text-right">{t('lines')}</th>
              <th className="w-10 px-2 py-2.5" />
            </tr>
          </thead>
          <tbody>
            {value.map((l, i) => (
              <tr key={i} className="border-b border-ink-100 last:border-0 align-top">
                <td className="px-3 py-2 min-w-[15rem]">
                  {enableProduct ? (
                    <ProductPicker
                      description={l.descriptionTh}
                      ariaLabel={`${t('description')} ${i + 1}`}
                      onDescriptionChange={(text) =>
                        set(i, { descriptionTh: text, productId: null, productCode: null, productType: 'GOOD' })
                      }
                      onSelectProduct={(p) =>
                        // Product master drives TYPE + tax code only — NOT price.
                        // Same product/service sells at a different price each time,
                        // so price/discount stay user-entered per line (sprint plan #1).
                        set(i, {
                          productId: p.productId,
                          productCode: p.productCode,
                          productType: p.productType,
                          descriptionTh: p.nameTh,
                          taxRate: taxRateForProductType(p.productType),
                        })
                      }
                    />
                  ) : (
                    <input
                      className="input input-sm input-bordered w-full"
                      value={l.descriptionTh}
                      onChange={(e) => set(i, { descriptionTh: e.target.value })}
                      aria-label={`${t('description')} ${i + 1}`}
                    />
                  )}
                </td>
                <td className="px-2 py-2">
                  <AmountInput
                    value={l.quantity}
                    step={1}
                    onValueChange={(n) => set(i, { quantity: n })}
                    aria-label={`${t('qty')} ${i + 1}`}
                  />
                </td>
                {enableProduct && (
                  <td className="px-2 py-2">
                    <input
                      className="input input-sm input-bordered w-full"
                      value={l.uomText ?? ''}
                      onChange={(e) => set(i, { uomText: e.target.value })}
                      aria-label={`${tq('uom')} ${i + 1}`}
                    />
                  </td>
                )}
                <td className="px-2 py-2">
                  <AmountInput
                    value={l.unitPrice}
                    onValueChange={(n) => set(i, { unitPrice: n })}
                    aria-label={`${t('unitPrice')} ${i + 1}`}
                  />
                </td>
                {enableProduct && (
                  <td className="px-2 py-2">
                    <AmountInput
                      value={l.discountPercent ?? 0}
                      step={1}
                      onValueChange={(n) => set(i, { discountPercent: n })}
                      aria-label={`${tq('discountPct')} ${i + 1}`}
                    />
                  </td>
                )}
                {showVat && (
                <td className="px-2 py-2">
                  {/* VAT rate: dropdown (7%/0%); product-picked lines lock to the
                      product tax code (Sprint 13i C2) and just show the rate. */}
                  {l.productId != null ? (
                    <span
                      className="inline-flex h-8 items-center rounded-field bg-base-200 px-3 text-sm font-medium text-ink-700"
                      title={t('taxRateLocked')}
                    >
                      {Math.round(l.taxRate * 100)}%
                    </span>
                  ) : (
                    <select
                      className="select select-bordered select-sm w-full"
                      value={l.taxRate}
                      onChange={(e) => set(i, { taxRate: Number(e.target.value) })}
                      aria-label={`${t('taxRate')} ${i + 1}`}
                    >
                      {options.map((o) => (
                        <option key={o.value} value={o.value}>
                          {o.label}
                        </option>
                      ))}
                    </select>
                  )}
                </td>
                )}
                <td className="px-3 py-2 text-right font-medium tabular-nums">{formatTHB(lineTotal(l, showVat))}</td>
                <td className="px-2 py-2 text-center">
                  <button
                    type="button"
                    className="btn btn-ghost btn-xs text-status-danger"
                    disabled={value.length === 1}
                    onClick={() => onChange(value.filter((_, idx) => idx !== i))}
                    aria-label={`remove line ${i + 1}`}
                  >
                    <Trash2 className="h-4 w-4" aria-hidden />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
